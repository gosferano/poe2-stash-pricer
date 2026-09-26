using System.Drawing;
using Poe2StashPricer.Platform;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>Moves the mouse with the virtual device and reads where it is with X11.</summary>
internal sealed class UinputInput : IInput
{
    private readonly X11Display display;
    private readonly UinputDevice device;

    public UinputInput(X11Display display, UinputDevice device)
    {
        this.display = display;
        this.device = device;
    }

    public void MoveMouse(int x, int y) { device.MoveMouse(x, y); }

    public Point GetCursorPos()
    {
        lock (display.Sync)
        {
            if (!X11.XQueryPointer(display.Handle, display.Root, out _, out _, out int x, out int y, out _, out _, out _))
                return Point.Empty;
            return new Point(x, y);
        }
    }

    public void SendCopy() { device.SendCopy(); }
}
