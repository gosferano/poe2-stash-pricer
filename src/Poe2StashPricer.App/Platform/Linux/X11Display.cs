using System;
using System.Runtime.InteropServices;
using System.Text;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// One connection to the X server. Xlib is not thread-safe, so a connection is never shared without the
/// lock: every call here takes <see cref="Sync"/>, and a caller that needs several calls to belong together
/// (reading a selection, say) holds it around the lot. The clipboard's event loop blocks in XNextEvent and
/// so gets a connection of its own rather than stalling everyone else.
/// </summary>
internal sealed class X11Display : IDisposable
{
    public IntPtr Handle { get; private set; }

    /// <summary>Held for the whole of any sequence of calls on this connection.</summary>
    public object Sync { get; } = new object();

    public IntPtr Root { get; }

    private X11Display(IntPtr handle)
    {
        Handle = handle;
        Root = X11.XDefaultRootWindow(handle);
    }

    public static X11Display Open()
    {
        X11Errors.Install();
        IntPtr h = X11.XOpenDisplay(null);
        if (h == IntPtr.Zero)
            throw new PlatformNotSupportedException(
                "cannot reach the X server (DISPLAY=" + (Environment.GetEnvironmentVariable("DISPLAY") ?? "unset") + "). " +
                "The game runs under XWayland, so an X server has to be reachable.");
        X11Errors.Own(h);
        return new X11Display(h);
    }

    public ulong Atom(string name)
    {
        lock (Sync) return X11.XInternAtom(Handle, name, false);
    }

    public bool HasExtension(string name)
    {
        lock (Sync) return X11.XQueryExtension(Handle, name, out _, out _, out _);
    }

    /// <summary>The whole desktop's extent: an X screen spans every monitor.</summary>
    public System.Drawing.Size ScreenSize()
    {
        lock (Sync)
        {
            if (X11.XGetWindowAttributes(Handle, Root, out X11.XWindowAttributes a) == 0)
                return System.Drawing.Size.Empty;
            return new System.Drawing.Size(a.width, a.height);
        }
    }

    // ---- property reads ----

    public IntPtr[] GetWindows(IntPtr w, string property)
    {
        lock (Sync)
        {
            ulong atom = X11.XInternAtom(Handle, property, true);
            if (atom == 0) return Array.Empty<IntPtr>();
            if (X11.XGetWindowProperty(Handle, w, atom, 0, 4096, false, X11.XA_WINDOW,
                                       out _, out int fmt, out ulong n, out _, out IntPtr prop) != 0
                || prop == IntPtr.Zero) return Array.Empty<IntPtr>();
            try
            {
                if (fmt != 32) return Array.Empty<IntPtr>();
                IntPtr[] res = new IntPtr[n];
                for (ulong i = 0; i < n; i++) res[i] = (IntPtr)Marshal.ReadInt64(prop, (int)i * 8);
                return res;
            }
            finally { X11.XFree(prop); }
        }
    }

    /// <summary>A text property. WM_CLASS holds two strings separated by a NUL; both are returned, joined by '|'.</summary>
    public string? GetText(IntPtr w, string property)
    {
        lock (Sync)
        {
            ulong atom = X11.XInternAtom(Handle, property, true);
            if (atom == 0) return null;
            if (X11.XGetWindowProperty(Handle, w, atom, 0, 1024, false, 0 /* AnyPropertyType */,
                                       out _, out _, out ulong n, out _, out IntPtr prop) != 0
                || prop == IntPtr.Zero) return null;
            try
            {
                if (n == 0) return null;
                byte[] raw = new byte[n];
                Marshal.Copy(prop, raw, 0, (int)n);
                return Encoding.UTF8.GetString(raw).Replace('\0', '|').Trim('|');
            }
            finally { X11.XFree(prop); }
        }
    }

    public uint GetCardinal(IntPtr w, string property)
    {
        lock (Sync)
        {
            ulong atom = X11.XInternAtom(Handle, property, true);
            if (atom == 0) return 0;
            if (X11.XGetWindowProperty(Handle, w, atom, 0, 1, false, X11.XA_CARDINAL,
                                       out _, out _, out ulong n, out _, out IntPtr prop) != 0
                || prop == IntPtr.Zero || n == 0) return 0;
            try { return (uint)Marshal.ReadInt32(prop); }
            finally { X11.XFree(prop); }
        }
    }

    public void Dispose()
    {
        lock (Sync)
        {
            if (Handle != IntPtr.Zero)
            {
                X11Errors.Disown(Handle);
                X11.XCloseDisplay(Handle);
                Handle = IntPtr.Zero;
            }
        }
    }
}

/// <summary>
/// X errors arrive later than the call that caused them and the default handler ends the process, which is
/// no way to treat "that window just closed". Ours notes the error and lets the caller decide; errors from
/// connections that are not ours (Avalonia's, say) go on to whichever handler was installed before.
/// </summary>
internal static class X11Errors
{
    private static readonly object _sync = new object();
    private static readonly X11.ErrorHandler _handler = OnError;
    private static readonly System.Collections.Generic.HashSet<IntPtr> _ours = new System.Collections.Generic.HashSet<IntPtr>();
    private static IntPtr _previous;
    private static bool _installed;

    public static void Own(IntPtr display) { lock (_sync) _ours.Add(display); }

    public static void Disown(IntPtr display) { lock (_sync) _ours.Remove(display); }

    private static bool IsOurs(IntPtr display) { lock (_sync) return _ours.Contains(display); }

    [ThreadStatic] private static string? last;

    /// <summary>The last X error on this thread, if any, since <see cref="Clear"/>.</summary>
    public static string? Last => last;

    public static void Clear() { last = null; }

    public static void Install()
    {
        lock (_sync)
        {
            if (_installed) return;
            // Whatever was installed before is kept as a plain pointer: Avalonia's X11 backend puts its own
            // managed delegate there, and .NET will not hand that back as a delegate of our type. Calling
            // through the pointer sidesteps the question of whose delegate it was.
            _previous = X11.XSetErrorHandler(_handler);
            _installed = true;
        }
    }

    private static int OnError(IntPtr display, IntPtr ev)
    {
        // Not our connection (Avalonia's, say): leave it to whoever handled it before us.
        if (!IsOurs(display))
        {
            IntPtr chain = _previous;
            if (chain == IntPtr.Zero) return 0;
            unsafe { return ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)chain)(display, ev); }
        }

        // XErrorEvent: int type(0); Display *display(8); XID resourceid(16); unsigned long serial(24);
        // unsigned char error_code(32), request_code(33), minor_code(34).
        try
        {
            byte code = Marshal.ReadByte(ev, 32);
            byte request = Marshal.ReadByte(ev, 33);
            StringBuilder sb = new StringBuilder(256);
            X11.XGetErrorText(display, code, sb, sb.Capacity);
            last = sb + " (request " + request + ")";
            Log.Write("X error: " + last);
        }
        catch { }
        return 0;
    }
}
