using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Poe2StashPricer.Platform;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// The Wayland clipboard, through the data-control protocol: the one a clipboard manager uses, so it can
/// read and set the selection without the app ever holding focus.
///
/// This is where the clipboard really lives on a Wayland desktop. The game is an XWayland client, and the
/// compositor does mirror what it copies to here (measured: every item of a scan arrives), so this one
/// connection covers both the game's copies and the user's own clipboard.
/// </summary>
internal sealed class WaylandClipboard : IClipboard, IDisposable
{
    private const string Libc = "libc";

    [DllImport(Libc, SetLastError = true)] private static extern int pipe2(int[] fds, int flags);
    [DllImport(Libc, SetLastError = true)] private static extern int close(int fd);
    [DllImport(Libc, SetLastError = true)] private static extern nint read(int fd, byte[] buf, nint count);
    [DllImport(Libc, SetLastError = true)] private static extern nint write(int fd, byte[] buf, nint count);

    private const int O_CLOEXEC = 0x80000;

    /// <summary>What a text selection is offered as; the first is what we ask for when reading.</summary>
    private static readonly string[] TextMimes =
    {
        "text/plain;charset=utf-8", "text/plain", "UTF8_STRING", "STRING", "TEXT",
    };

    /// <summary>Clipboard managers skip an offer carrying this, which is how "do not store" is said here.</summary>
    private const string PasswordHint = "x-kde-passwordManagerHint";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OfferFn(IntPtr data, IntPtr proxy, IntPtr mime);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ObjectFn(IntPtr data, IntPtr proxy, IntPtr obj);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidFn(IntPtr data, IntPtr proxy);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SendFn(IntPtr data, IntPtr proxy, IntPtr mime, int fd);

    private readonly IntPtr _deviceListener, _offerListener, _sourceListener;

    private readonly WaylandConnection _connection;
    private readonly DataControl _protocol;
    private readonly Thread _loop;
    private volatile bool _stop;

    private readonly object _sync = new object();
    private IntPtr _device;
    private IntPtr _currentOffer;
    private readonly Dictionary<IntPtr, List<string>> _offerMimes = new Dictionary<IntPtr, List<string>>();
    private readonly HashSet<IntPtr> _liveOffers = new HashSet<IntPtr>();
    private IntPtr _source;
    private string _offered = "";
    private long _changes;

    private WaylandClipboard(WaylandConnection connection, DataControl protocol)
    {
        _connection = connection;
        _protocol = protocol;

        _deviceListener = Wl.Listener(new ObjectFn(OnDataOffer), new ObjectFn(OnSelection),
                                     new VoidFn(OnFinished), new ObjectFn(OnPrimarySelection));
        _offerListener = Wl.Listener(new OfferFn(OnOfferMime));
        _sourceListener = Wl.Listener(new SendFn(OnSend), new VoidFn(OnCancelled));

        _device = Wl.Request(connection.Manager, DataControl.ManagerGetDataDevice, protocol.Device.Ptr,
                            new[] { WlArgument.NewId(), WlArgument.Ptr(connection.Seat) });
        Wl.wl_proxy_add_listener(_device, _deviceListener, IntPtr.Zero);
        Wl.wl_display_roundtrip(connection.Display);

        _loop = new Thread(Loop) { IsBackground = true, Name = "wayland-clipboard" };
        _loop.Start();
    }

    public static WaylandClipboard? TryCreate()
    {
        WaylandConnection? c = WaylandConnection.TryOpen();
        if (c == null) return null;
        if (c.Protocol == null || c.Manager == IntPtr.Zero || c.Seat == IntPtr.Zero)
        {
            Log.Write("the compositor offers no data-control protocol, so the clipboard cannot be put back");
            c.Dispose();
            return null;
        }
        try { return new WaylandClipboard(c, c.Protocol); }
        catch (Exception ex)
        {
            Log.Write("the Wayland clipboard could not be set up: " + ex.Message);
            c.Dispose();
            return null;
        }
    }

    public string ProtocolName => _protocol.Manager.Name;

    /// <summary>Goes up whenever the compositor reports a new selection.</summary>
    public ulong ChangeCount => (ulong)Interlocked.Read(ref _changes);

    private void Loop()
    {
        while (!_stop)
        {
            if (Wl.wl_display_dispatch(_connection.Display) < 0)
            {
                Log.Write("the Wayland connection ended (" + Wl.wl_display_get_error(_connection.Display) + ")");
                return;
            }
        }
    }

    // ---- events ----

    private void OnDataOffer(IntPtr data, IntPtr proxy, IntPtr offer)
    {
        lock (_sync)
        {
            _offerMimes[offer] = new List<string>();
            _liveOffers.Add(offer);
        }
        Wl.wl_proxy_add_listener(offer, _offerListener, IntPtr.Zero);
    }

    /// <summary>Caller holds the lock. Destroying an offer twice would take the process with it.</summary>
    private void DropOffer(IntPtr offer)
    {
        if (offer == IntPtr.Zero || !_liveOffers.Remove(offer)) return;
        _offerMimes.Remove(offer);
        Wl.Destructor(offer, DataControl.OfferDestroy);
    }

    private void OnOfferMime(IntPtr data, IntPtr offer, IntPtr mime)
    {
        string? m = Marshal.PtrToStringAnsi(mime);
        if (m == null) return;
        lock (_sync)
        {
            if (_offerMimes.TryGetValue(offer, out List<string>? list)) list.Add(m);
        }
    }

    private void OnSelection(IntPtr data, IntPtr proxy, IntPtr offer)
    {
        lock (_sync)
        {
            if (_currentOffer != offer) DropOffer(_currentOffer);
            _currentOffer = offer;
        }
        Interlocked.Increment(ref _changes);
    }

    /// <summary>Middle-click paste, which this app has no use for; the offer is dropped.</summary>
    private void OnPrimarySelection(IntPtr data, IntPtr proxy, IntPtr offer)
    {
        lock (_sync) DropOffer(offer);
    }

    private void OnFinished(IntPtr data, IntPtr proxy)
    {
        Log.Write("the compositor took the data-control device away");
    }

    /// <summary>The compositor wants the text we are offering, written to its pipe.</summary>
    private void OnSend(IntPtr data, IntPtr proxy, IntPtr mime, int fd)
    {
        try
        {
            string m = Marshal.PtrToStringAnsi(mime) ?? "";
            string give = m == PasswordHint ? "secret" : _offered;
            byte[] bytes = Encoding.UTF8.GetBytes(give);
            int written = 0;
            while (written < bytes.Length)
            {
                byte[] slice = written == 0 ? bytes : bytes[written..];
                nint n = write(fd, slice, slice.Length);
                if (n <= 0) break;
                written += (int)n;
            }
        }
        catch (Exception ex) { Log.Write("the clipboard could not be handed over: " + ex.Message); }
        finally { close(fd); }
    }

    private void OnCancelled(IntPtr data, IntPtr proxy)
    {
        lock (_sync)
        {
            if (_source == proxy)
            {
                Wl.Destructor(_source, DataControl.SourceDestroy);
                _source = IntPtr.Zero;
            }
        }
    }

    // ---- reading ----

    /// <summary>
    /// The selection's text. The whole read is held under the lock: the dispatch thread destroys an offer as
    /// soon as the next selection arrives, and during a scan that happens every few milliseconds, so reading
    /// from an offer picked up a moment earlier would be reading freed memory. The compositor writes to the
    /// pipe from its own process, so nothing here waits on our dispatch thread.
    /// </summary>
    public string? GetText()
    {
        lock (_sync) return GetTextLocked();
    }

    private string? GetTextLocked()
    {
        IntPtr offer = _currentOffer;
        if (offer == IntPtr.Zero) return null;
        string? mime = null;
        if (_offerMimes.TryGetValue(offer, out List<string>? mimes))
            foreach (string candidate in TextMimes)
                if (mimes.Contains(candidate)) { mime = candidate; break; }
        if (mime == null) return null;

        int[] fds = new int[2];
        if (pipe2(fds, O_CLOEXEC) != 0) return null;
        try
        {
            IntPtr mimePtr = Marshal.StringToHGlobalAnsi(mime);
            try
            {
                Wl.Request(offer, DataControl.OfferReceive, IntPtr.Zero,
                           new[] { WlArgument.Str(mimePtr), WlArgument.Fd(fds[1]) });
                Wl.wl_display_flush(_connection.Display);
            }
            finally { Marshal.FreeHGlobal(mimePtr); }

            // The write end must be closed here or the read below never sees the end of the data.
            close(fds[1]);
            fds[1] = -1;

            using System.IO.MemoryStream buffer = new System.IO.MemoryStream();
            byte[] chunk = new byte[4096];
            while (true)
            {
                nint n = read(fds[0], chunk, chunk.Length);
                if (n <= 0) break;
                buffer.Write(chunk, 0, (int)n);
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        finally
        {
            if (fds[0] >= 0) close(fds[0]);
            if (fds[1] >= 0) close(fds[1]);
        }
    }

    // ---- writing ----

    public void SetText(string text, bool sensitive)
    {
        lock (_sync)
        {
            _offered = text;
            if (_source != IntPtr.Zero)
            {
                Wl.Destructor(_source, DataControl.SourceDestroy);
                _source = IntPtr.Zero;
            }
            _source = Wl.Request(_connection.Manager, DataControl.ManagerCreateDataSource, _protocol.Source.Ptr,
                                new[] { WlArgument.NewId() });
            Wl.wl_proxy_add_listener(_source, _sourceListener, IntPtr.Zero);

            foreach (string mime in TextMimes) Offer(_source, mime);
            if (sensitive) Offer(_source, PasswordHint);

            Wl.Request(_device, DataControl.DeviceSetSelection, IntPtr.Zero, new[] { WlArgument.Ptr(_source) });
            Wl.wl_display_flush(_connection.Display);
        }
    }

    private static void Offer(IntPtr source, string mime)
    {
        IntPtr p = Marshal.StringToHGlobalAnsi(mime);
        try { Wl.Request(source, DataControl.SourceOffer, IntPtr.Zero, new[] { WlArgument.Str(p) }); }
        finally { Marshal.FreeHGlobal(p); }
    }

    public void Dispose()
    {
        _stop = true;
        lock (_sync)
        {
            if (_source != IntPtr.Zero) Wl.Destructor(_source, DataControl.SourceDestroy);
            DropOffer(_currentOffer);
            _currentOffer = IntPtr.Zero;
            if (_device != IntPtr.Zero) Wl.Destructor(_device, DataControl.DeviceDestroy);
            _source = _device = IntPtr.Zero;
        }
        Wl.wl_display_flush(_connection.Display);
        _loop.Join(500);
        _connection.Dispose();
    }
}
