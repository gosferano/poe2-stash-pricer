using System.Drawing;
using Poe2StashPricer.Detection;

namespace Poe2StashPricer.Platform;

/// <summary>
/// Takes a picture of a piece of the screen. Upstream did this with GDI's CopyFromScreen; on Wayland
/// the compositor decides, so this is the app's job (XComposite on the XWayland game window first,
/// with compositor-specific capture protocols as fallbacks).
/// </summary>
public interface IScreenCapture
{
    /// <param name="screenRect">The area to capture, in screen coordinates (physical pixels).</param>
    PixelBuffer Capture(Rectangle screenRect);
}
