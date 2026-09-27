using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Poe2StashPricer.Detection;
using Poe2StashPricer.Platform;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// Takes the picture with XGetImage on the game window itself.
///
/// Reading the root window is not an option: Xwayland is rootless and answers BadMatch, so under Wayland
/// there is no screen-wide drawable at all. That suits us — only the game is ever in the picture, so the
/// overlay can never end up in it. XComposite's NameWindowPixmap was measured against this and returned
/// byte-identical pixels at the same speed, so it buys nothing and is not used.
/// </summary>
internal sealed class X11ScreenCapture : IScreenCapture
{
    private readonly X11Display _display;
    private readonly X11GameWindow _game;

    public X11ScreenCapture(X11Display display, X11GameWindow game)
    {
        _display = display;
        _game = game;
    }

    /// <param name="screenRect">Wanted area in screen coordinates; it is read out of the game window.</param>
    public PixelBuffer Capture(Rectangle screenRect)
    {
        Rectangle win = _game.ClientRectOnScreen();
        if (win.IsEmpty) throw new InvalidOperationException("the game window is gone");

        Rectangle local = new Rectangle(screenRect.X - win.X, screenRect.Y - win.Y,
                                        Math.Max(1, screenRect.Width), Math.Max(1, screenRect.Height));
        lock (_display.Sync)
        {
            X11Errors.Clear();
            IntPtr img = X11.XGetImage(_display.Handle, _game.Handle, local.X, local.Y,
                                       (uint)local.Width, (uint)local.Height, X11.AllPlanes, X11.ZPixmap);
            if (img == IntPtr.Zero)
                throw new InvalidOperationException("the game window could not be captured: "
                                                    + (X11Errors.Last ?? "XGetImage gave nothing"));
            try
            {
                X11.XImage x = Marshal.PtrToStructure<X11.XImage>(img);
                // Depth 24 or 32 at 32 bits per pixel on a little-endian server is BGRA in memory, which is
                // exactly what PixelBuffer reads. Anything else we do not know how to interpret.
                if (x.bits_per_pixel != 32)
                    throw new InvalidOperationException("the X server gave " + x.bits_per_pixel
                                                        + " bits per pixel; only 32 is supported");
                byte[] px = new byte[x.bytes_per_line * x.height];
                Marshal.Copy(x.data, px, 0, px.Length);
                return new PixelBuffer(px, x.width, x.height, x.bytes_per_line);
            }
            finally { X11.DestroyImage(img); }
        }
    }
}
