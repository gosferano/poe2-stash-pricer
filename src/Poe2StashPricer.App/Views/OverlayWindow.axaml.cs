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
    private bool _unmanaged;
    private bool _remapping;

    public OverlayWindow() : this(null) { }

    public OverlayWindow(X11Display? display)
    {
        _display = display;
        InitializeComponent();
        Content = _surface;
        // Before the window is ever mapped: override-redirect is read at map time, and doing it here means
        // the compositor never gets to place, centre or decorate it even once.
        TakeOutOfTheWindowManagersHands();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        MakeClickThrough();
        TakeOutOfTheWindowManagersHands();
    }

    /// <summary>
    /// The window manager places, sizes and decorates ordinary windows, and a user's own rules can move
    /// ours somewhere else entirely. An overlay wants none of that, so it is marked override-redirect: the
    /// compositor then leaves it exactly where it is put. The mark only counts when a window is mapped, so
    /// the first time round it is taken down and put back up.
    /// </summary>
    private void TakeOutOfTheWindowManagersHands()
    {
        if (_unmanaged || _display == null || _remapping) return;
        IPlatformHandle? handle = TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero) return;
        if (!ClickThrough.MakeUnmanaged(_display, handle.Handle)) return;
        _unmanaged = true;

        _remapping = true;
        Hide();
        Show();
        _remapping = false;
        MakeClickThrough();
        Log.Write("overlay window: transparency " + ActualTransparencyLevel + ", click-through " + _shaped
                  + ", unmanaged " + _unmanaged);
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
        // An unmanaged window is placed by us and nobody else, so the position is set again after showing:
        // showing it is what puts it on screen, and only then is there anything to place.
        Position = new PixelPoint(content.Region.X, top);
    }
}
