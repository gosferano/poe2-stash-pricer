using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Poe2StashPricer.Platform;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// The CLIPBOARD selection, as the scanner needs it.
///
/// <see cref="ChangeCount"/> is what makes a scan quick: after Ctrl+C the scanner waits for the count to
/// move, which says the game answered even when it copied the same text as last time. XFixes reports every
/// change of the selection's owner, which is exactly what the game does on each copy, so the count is a real
/// counter and not a comparison of the text.
///
/// Putting the user's clipboard back means owning the selection ourselves and answering requests for it,
/// which needs an event loop; that loop blocks, so it gets a connection of its own. Reading happens on the
/// shared connection instead, so a scan never waits on the loop.
/// </summary>
internal sealed class X11Clipboard : IClipboard, IDisposable
{
    private readonly X11Display shared;          // reading; used by the scan thread
    private X11Display? owned;                   // the event loop's own connection
    private readonly Thread loop;
    private readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);
    private volatile bool stop;

    private IntPtr readWindow;                   // on the shared connection
    private IntPtr ownerWindow;                  // on the owned connection
    private readonly XAtoms atoms;
    private int selectionOwnerEvent = -1;   // the XFixes event number for a change of selection owner
    private long changes;

    // What we are serving, when we own the selection.
    private readonly object offerSync = new object();
    private string? offered;
    private bool offeredSensitive;
    private string? pending;
    private bool pendingSensitive;

    public X11Clipboard(X11Display shared)
    {
        this.shared = shared;
        atoms = new XAtoms(shared);

        lock (shared.Sync)
            readWindow = X11.XCreateSimpleWindow(shared.Handle, shared.Root, 0, 0, 1, 1, 0, 0, 0);

        loop = new Thread(Loop) { IsBackground = true, Name = "clipboard" };
        loop.Start();
        if (!ready.Wait(5000)) Log.Write("the clipboard watcher did not start in time");
    }

    /// <summary>The interned atoms this class works with, named so that an atom never reads as a value.</summary>
    private sealed class XAtoms
    {
        public readonly ulong Clipboard, Utf8String, Targets, Incr, PlainUtf8, Text, PasswordHint, ReadProperty, Wake;

        public XAtoms(X11Display d)
        {
            Clipboard = d.Atom("CLIPBOARD");
            Utf8String = d.Atom("UTF8_STRING");
            Targets = d.Atom("TARGETS");
            Incr = d.Atom("INCR");
            PlainUtf8 = d.Atom("text/plain;charset=utf-8");
            Text = d.Atom("TEXT");
            // Clipboard managers (Klipper, and cliphist through wl-paste) skip anything offering this.
            PasswordHint = d.Atom("x-kde-passwordManagerHint");
            ReadProperty = d.Atom("POE2_CLIPBOARD_READ");
            Wake = d.Atom("POE2_CLIPBOARD_WAKE");
        }
    }

    /// <summary>Goes up on every clipboard change, whoever made it.</summary>
    public ulong ChangeCount => (ulong)Interlocked.Read(ref changes);

    // ---- reading, on the shared connection ----

    public string? GetText()
    {
        return GetText(300);
    }

    public string? GetText(int timeoutMs)
    {
        lock (shared.Sync)
        {
            if (readWindow == IntPtr.Zero) return null;
            X11.XDeleteProperty(shared.Handle, readWindow, atoms.ReadProperty);
            X11.XConvertSelection(shared.Handle, atoms.Clipboard, atoms.Utf8String, atoms.ReadProperty, readWindow, 0 /* CurrentTime */);
            X11.XFlush(shared.Handle);

            if (!WaitForSelectionNotify(timeoutMs)) return null;

            if (X11.XGetWindowProperty(shared.Handle, readWindow, atoms.ReadProperty, 0, 4 * 1024 * 1024, true,
                                       0 /* AnyPropertyType */, out ulong actualType, out int fmt,
                                       out ulong n, out _, out IntPtr prop) != 0 || prop == IntPtr.Zero)
                return null;
            try
            {
                // An item's text is a few hundred bytes, far short of the server's limit, so the chunked
                // INCR protocol never comes up. If it ever did, treating it as "nothing" is the safe answer.
                if (actualType == atoms.Incr) { Log.Write("clipboard offered an INCR transfer, which is not handled"); return null; }
                if (fmt != 8 || n == 0) return null;
                byte[] raw = new byte[n];
                Marshal.Copy(prop, raw, 0, (int)n);
                return Encoding.UTF8.GetString(raw);
            }
            finally { X11.XFree(prop); }
        }
    }

    /// <summary>Caller holds the shared lock.</summary>
    private bool WaitForSelectionNotify(int timeoutMs)
    {
        byte[] ev = new byte[256];
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            while (X11.XPending(shared.Handle) > 0)
            {
                X11.XNextEvent(shared.Handle, ev);
                if (BitConverter.ToInt32(ev, 0) == X11.SelectionNotify)
                    return BitConverter.ToInt64(ev, 56) != 0;   // property None means the owner refused
            }
            Thread.Sleep(1);
        }
        return false;
    }

    // ---- writing: we become the owner and answer requests ----

    public void SetText(string text, bool sensitive)
    {
        if (text == null) return;
        lock (offerSync)
        {
            pending = text;
            pendingSensitive = sensitive;
        }
        Wake();
    }

    /// <summary>
    /// Nudges the event loop from another thread. The window lives on the server, so an event may be sent to
    /// it from any connection; each thread still only ever calls Xlib on its own.
    /// </summary>
    private void Wake()
    {
        IntPtr target = ownerWindow;
        if (target == IntPtr.Zero) return;
        byte[] ev = new byte[96];
        BitConverter.GetBytes(33).CopyTo(ev, 0);              // ClientMessage
        BitConverter.GetBytes((long)target).CopyTo(ev, 32);
        BitConverter.GetBytes((long)atoms.Wake).CopyTo(ev, 40);
        BitConverter.GetBytes(32).CopyTo(ev, 48);
        lock (shared.Sync)
        {
            X11.XSendEvent(shared.Handle, target, false, 0, ev);
            X11.XFlush(shared.Handle);
        }
    }

    private void Loop()
    {
        try
        {
            owned = X11Display.Open();
            lock (owned.Sync)
            {
                ownerWindow = X11.XCreateSimpleWindow(owned.Handle, owned.Root, 0, 0, 1, 1, 0, 0, 0);
                X11.XSelectInput(owned.Handle, ownerWindow, X11.PropertyChangeMask);
                if (!X11.XFixesQueryExtension(owned.Handle, out int xfixesEvent, out _))
                {
                    Log.Write("XFixes is missing: clipboard changes cannot be watched");
                    ready.Set();
                    return;
                }
                X11.XFixesSelectSelectionInput(owned.Handle, ownerWindow, atoms.Clipboard, X11.XFixesSetSelectionOwnerNotifyMask);
                X11.XSync(owned.Handle, false);
                selectionOwnerEvent = xfixesEvent;
            }
            ready.Set();

            byte[] ev = new byte[256];
            while (!stop)
            {
                // XNextEvent blocks, which is why this connection is not shared with anyone.
                X11.XNextEvent(owned.Handle, ev);
                int type = BitConverter.ToInt32(ev, 0);
                if (type == selectionOwnerEvent) Interlocked.Increment(ref changes);
                else if (type == X11.SelectionRequest) Answer(ev);
                else if (type == X11.SelectionClear) lock (offerSync) offered = null;
                TakeOwnershipIfAsked();
            }
        }
        catch (Exception ex) { Log.Write("the clipboard watcher stopped: " + ex.Message); }
        finally
        {
            ready.Set();
            owned?.Dispose();
        }
    }

    private void TakeOwnershipIfAsked()
    {
        string? want;
        bool sensitive;
        lock (offerSync)
        {
            if (pending == null) return;
            want = pending;
            sensitive = pendingSensitive;
            pending = null;
            offered = want;
            offeredSensitive = sensitive;
        }
        if (owned == null) return;
        lock (owned.Sync)
        {
            X11.XSetSelectionOwner(owned.Handle, atoms.Clipboard, ownerWindow, 0 /* CurrentTime */);
            X11.XFlush(owned.Handle);
        }
    }

    /// <summary>Answers one SelectionRequest with whatever we are offering.</summary>
    private void Answer(byte[] request)
    {
        // XSelectionRequestEvent: owner(32), requestor(40), selection(48), target(56), property(64), time(72)
        IntPtr requestor = (IntPtr)BitConverter.ToInt64(request, 40);
        ulong target = (ulong)BitConverter.ToInt64(request, 56);
        ulong property = (ulong)BitConverter.ToInt64(request, 64);
        ulong time = (ulong)BitConverter.ToInt64(request, 72);
        if (property == 0) property = target;   // an old-style requestor

        string? give;
        bool sensitive;
        lock (offerSync)
        {
            give = offered;
            sensitive = offeredSensitive;
        }

        bool ok = false;
        if (give != null && owned != null)
        {
            lock (owned.Sync)
            {
                if (target == atoms.Targets)
                {
                    ulong[] list = sensitive
                        ? new[] { atoms.Targets, atoms.Utf8String, atoms.PlainUtf8, X11.XA_STRING, atoms.Text, atoms.PasswordHint }
                        : new[] { atoms.Targets, atoms.Utf8String, atoms.PlainUtf8, X11.XA_STRING, atoms.Text };
                    byte[] data = new byte[list.Length * 8];
                    for (int i = 0; i < list.Length; i++) BitConverter.GetBytes((long)list[i]).CopyTo(data, i * 8);
                    X11.XChangeProperty(owned.Handle, requestor, property, X11.XA_ATOM, 32, X11.PropModeReplace, data, list.Length);
                    ok = true;
                }
                else if (sensitive && target == atoms.PasswordHint)
                {
                    byte[] data = Encoding.UTF8.GetBytes("secret");
                    X11.XChangeProperty(owned.Handle, requestor, property, target, 8, X11.PropModeReplace, data, data.Length);
                    ok = true;
                }
                else if (target == atoms.Utf8String || target == atoms.PlainUtf8 || target == X11.XA_STRING || target == atoms.Text)
                {
                    byte[] data = Encoding.UTF8.GetBytes(give);
                    X11.XChangeProperty(owned.Handle, requestor, property, target, 8, X11.PropModeReplace, data, data.Length);
                    ok = true;
                }
            }
        }

        // XSelectionEvent: requestor(32), selection(40), target(48), property(56), time(64)
        byte[] ev = new byte[96];
        BitConverter.GetBytes(X11.SelectionNotify).CopyTo(ev, 0);
        BitConverter.GetBytes((long)requestor).CopyTo(ev, 32);
        BitConverter.GetBytes((long)atoms.Clipboard).CopyTo(ev, 40);
        BitConverter.GetBytes((long)target).CopyTo(ev, 48);
        BitConverter.GetBytes((long)(ok ? property : 0)).CopyTo(ev, 56);
        BitConverter.GetBytes((long)time).CopyTo(ev, 64);
        if (owned != null)
            lock (owned.Sync)
            {
                X11.XSendEvent(owned.Handle, requestor, false, 0, ev);
                X11.XFlush(owned.Handle);
            }
    }

    public void Dispose()
    {
        stop = true;
        Wake();
        loop.Join(1000);
        lock (shared.Sync)
        {
            if (readWindow != IntPtr.Zero)
            {
                X11.XDestroyWindow(shared.Handle, readWindow);
                readWindow = IntPtr.Zero;
            }
        }
        ready.Dispose();
    }
}
