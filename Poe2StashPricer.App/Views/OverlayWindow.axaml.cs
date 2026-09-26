using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Poe2StashPricer.App.Platform.Linux;
using Poe2StashPricer.Session;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App.Views;

/// <summary>
/// The prices drawn over the game. It is a plain X11 window kept above the others, with an empty input
/// region so every click goes to the game underneath.
///
/// Screen coordinates are physical pixels and Avalonia sizes windows in device-independent ones, so the
/// size is divided by the scaling while the position (a PixelPoint) is not.
/// </summary>
internal partial class OverlayWindow : Window
{
    private readonly OverlaySurface _surface = new OverlaySurface();
    private readonly X11Display? _display;
    private bool _shaped;

    public OverlayWindow() : this(null) { }

    public OverlayWindow(X11Display? display)
    {
        _display = display;
        InitializeComponent();
        Content = _surface;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        MakeClickThrough();
        Log.Write("overlay window: transparency asked " + string.Join(",", TransparencyLevelHint)
                  + ", got " + ActualTransparencyLevel + ", click-through " + _shaped);
    }

    private void MakeClickThrough()
    {
        if (_shaped || _display == null) return;
        IPlatformHandle? handle = TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero) return;
        _shaped = ClickThrough.Apply(_display, handle.Handle);
        if (!_shaped) Log.Write("the overlay takes clicks: the game will not see them");
    }

    /// <summary>Shows what the session decided, or hides the window when there is nothing to show.</summary>
    public void Update(OverlayContent content)
    {
        if (content.Kind == OverlayKind.Hidden || content.Region.Width <= 0)
        {
            if (IsVisible) Hide();
            return;
        }

        double scale = RenderScaling <= 0 ? 1 : RenderScaling;
        // Room above the stash for the header line.
        int headerHeight = 28;
        int top = Math.Max(0, content.Region.Y - headerHeight);
        Position = new PixelPoint(content.Region.X, top);
        Width = content.Region.Width / scale;
        Height = (content.Region.Height + headerHeight) / scale;

        _surface.Show(content, new PixelPoint(content.Region.X, top), scale, headerHeight);
        if (!IsVisible) Show();
        MakeClickThrough();
        Topmost = true;   // a fullscreen game can push itself above; ask again every time
    }
}
