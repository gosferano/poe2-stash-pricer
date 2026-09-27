using System;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>Makes a window invisible to the mouse, and invisible to the window manager.</summary>
internal static class ClickThrough
{
    /// <param name="xid">The window's X11 id, from Avalonia's platform handle.</param>
    public static bool Apply(X11Display display, IntPtr xid)
    {
        if (xid == IntPtr.Zero) return false;
        lock (display.Sync)
        {
            X11Errors.Clear();
            // An empty input region: the window is drawn, but nothing can be pointed at it.
            X11.XShapeCombineRectangles(display.Handle, xid, X11.ShapeInput, 0, 0, IntPtr.Zero, 0, X11.ShapeSet, X11.Unsorted);
            X11.XSync(display.Handle, false);
            if (X11Errors.Last != null)
            {
                Log.Write("the overlay could not be made click-through: " + X11Errors.Last);
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Takes the window out of the window manager's hands (override-redirect). An overlay is not a window
    /// anyone wants managed: tiled, centred, animated, focused or given a border. Every compositor honours
    /// this, so it needs no rules and behaves the same on Hyprland, KWin or a plain X session.
    ///
    /// The attribute is read when a window is mapped, so a window that is already on screen has to be taken
    /// down and put back up for it to count.
    /// </summary>
    public static bool MakeUnmanaged(X11Display display, IntPtr xid)
    {
        if (xid == IntPtr.Zero) return false;
        byte[] attributes = new byte[X11.SetWindowAttributesSize];
        BitConverter.GetBytes(1).CopyTo(attributes, X11.OverrideRedirectOffset);
        lock (display.Sync)
        {
            X11Errors.Clear();
            X11.XChangeWindowAttributes(display.Handle, xid, X11.CWOverrideRedirect, attributes);
            X11.XSync(display.Handle, false);
            if (X11Errors.Last != null)
            {
                Log.Write("the overlay could not be taken out of the window manager's hands: " + X11Errors.Last);
                return false;
            }
        }
        return true;
    }
}
