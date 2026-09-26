using System;
using Poe2StashPricer.Platform;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// Everything the app needs from the system, put together. Nothing here asks which compositor is running:
/// the game is an XWayland client wherever it runs, and the mouse goes through the kernel.
/// </summary>
internal sealed class LinuxPlatform : IDisposable
{
    private readonly X11Display _display;
    private readonly UinputDevice? _device;
    private readonly WaylandClipboard? _wayland;
    private readonly X11Clipboard? _x11Clipboard;

    public X11GameWindow Game { get; }
    public IScreenCapture Capture { get; }
    public IInput? Input { get; }
    public X11KeyState Keys { get; }
    public IClipboard Clipboard { get; }

    /// <summary>How the clipboard is reached, for the log and the debug commands.</summary>
    public string ClipboardKind { get; }

    /// <summary>Why the mouse cannot be driven, when it cannot; null when all is well.</summary>
    public string? InputProblem { get; }

    private LinuxPlatform(AppSettings settings)
    {
        _display = X11Display.Open();
        Game = new X11GameWindow(_display, settings.GameWindowClass, settings.GameWindowTitle);
        Capture = new X11ScreenCapture(_display, Game);
        Keys = new X11KeyState(_display);

        // The data-control protocol is where the clipboard lives on a Wayland desktop, and the compositor
        // mirrors what the XWayland game copies into it. Falling back to X11 covers a plain X session and
        // any compositor without the protocol; there the user's own clipboard cannot be put back.
        _wayland = WaylandClipboard.TryCreate();
        if (_wayland != null)
        {
            Clipboard = _wayland;
            ClipboardKind = _wayland.ProtocolName;
        }
        else
        {
            _x11Clipboard = new X11Clipboard(_display);
            Clipboard = _x11Clipboard;
            ClipboardKind = "X11 selections (the user's clipboard cannot be put back)";
        }

        // Without /dev/uinput the app can still watch and price on hover; only scanning needs to move the mouse.
        try
        {
            _device = new UinputDevice(() => _display.ScreenSize());
            Input = new UinputInput(_display, _device);
        }
        catch (Exception ex)
        {
            InputProblem = ex.Message;
            Log.Write("no virtual input device: " + ex.Message);
        }
    }

    public static LinuxPlatform Create(AppSettings settings)
    {
        LinuxPlatform p = new LinuxPlatform(settings);
        if (!p._display.HasExtension("XFIXES"))
            Log.Write("the X server has no XFIXES; clipboard changes cannot be detected");
        return p;
    }

    public void Dispose()
    {
        _wayland?.Dispose();
        _x11Clipboard?.Dispose();
        _device?.Dispose();
        _display.Dispose();
    }
}
