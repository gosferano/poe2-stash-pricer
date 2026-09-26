using System;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>Makes a window invisible to the mouse, so the game gets every click.</summary>
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
}
