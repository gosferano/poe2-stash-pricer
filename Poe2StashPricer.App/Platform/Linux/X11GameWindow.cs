using System;
using System.Drawing;
using System.IO;
using Poe2StashPricer.Platform;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// Finds Path of Exile 2 among the X clients. Under Proton the game is an XWayland window whose WM_CLASS
/// is steam_app_&lt;id&gt;, so it is found the same way on every compositor. Both the class and the title are
/// settings: a non-Steam copy, or another language, names its window differently.
/// </summary>
internal sealed class X11GameWindow : IGameWindow
{
    private readonly X11Display display;
    private readonly string wmClass;
    private readonly string title;
    private IntPtr window;

    public X11GameWindow(X11Display display, string wmClass, string title)
    {
        this.display = display;
        this.wmClass = wmClass ?? "";
        this.title = title ?? "";
    }

    public IntPtr Handle => window;

    public bool Find()
    {
        // A window we already found stays good while it is still listed and still viewable.
        if (window != IntPtr.Zero && Describe(window) != null) return true;

        window = IntPtr.Zero;
        foreach (IntPtr w in display.GetWindows(display.Root, "_NET_CLIENT_LIST"))
        {
            string cls = display.GetText(w, "WM_CLASS") ?? "";
            string name = display.GetText(w, "_NET_WM_NAME") ?? display.GetText(w, "WM_NAME") ?? "";
            bool matches = (wmClass.Length > 0 && cls.Contains(wmClass, StringComparison.OrdinalIgnoreCase))
                           || (title.Length > 0 && name.Contains(title, StringComparison.OrdinalIgnoreCase));
            if (!matches) continue;
            window = w;
            Log.Write("game window 0x" + w.ToString("x") + " found: class " + cls + ", title " + name
                      + ", " + (Describe(w) ?? "no process"));
            return true;
        }
        return false;
    }

    /// <summary>The process behind a window, for the log. Also confirms the window is still there.</summary>
    private string? Describe(IntPtr w)
    {
        uint pid = display.GetCardinal(w, "_NET_WM_PID");
        if (pid == 0) return null;
        try { return "pid " + pid + " (" + File.ReadAllText("/proc/" + pid + "/comm").Trim() + ")"; }
        catch { return null; }
    }

    /// <summary>
    /// The window's drawable area in screen coordinates. A fullscreen game has no decorations, so this is
    /// the window itself, translated to the root.
    /// </summary>
    public Rectangle ClientRectOnScreen()
    {
        if (window == IntPtr.Zero && !Find()) return Rectangle.Empty;
        lock (display.Sync)
        {
            X11Errors.Clear();
            if (X11.XGetWindowAttributes(display.Handle, window, out X11.XWindowAttributes a) == 0
                || X11Errors.Last != null)
            {
                window = IntPtr.Zero;   // it went away between the search and now
                return Rectangle.Empty;
            }
            if (a.map_state != X11.IsViewable) return Rectangle.Empty;
            if (!X11.XTranslateCoordinates(display.Handle, window, a.root, 0, 0, out int x, out int y, out _))
                return Rectangle.Empty;
            return new Rectangle(x, y, a.width, a.height);
        }
    }

    /// <summary>
    /// True while the game has the keyboard. _NET_ACTIVE_WINDOW on the root is the EWMH answer and is kept
    /// up to date by every compositor we target.
    /// </summary>
    public bool IsForeground()
    {
        if (window == IntPtr.Zero) return false;
        IntPtr[] active = display.GetWindows(display.Root, "_NET_ACTIVE_WINDOW");
        return active.Length > 0 && active[0] == window;
    }

    /// <summary>What has the keyboard instead, for the diagnostic log.</summary>
    public string ForegroundDescription()
    {
        IntPtr[] active = display.GetWindows(display.Root, "_NET_ACTIVE_WINDOW");
        if (active.Length == 0 || active[0] == IntPtr.Zero) return "nothing";
        if (active[0] == window) return "the game";
        string cls = display.GetText(active[0], "WM_CLASS") ?? "unknown";
        return cls + " (not the game)";
    }
}
