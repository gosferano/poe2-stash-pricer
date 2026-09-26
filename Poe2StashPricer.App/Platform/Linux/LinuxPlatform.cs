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
    private readonly X11Display display;
    private readonly UinputDevice? device;

    public X11GameWindow Game { get; }
    public IScreenCapture Capture { get; }
    public IInput? Input { get; }
    public X11KeyState Keys { get; }
    public X11Clipboard Clipboard { get; }

    /// <summary>Why the mouse cannot be driven, when it cannot; null when all is well.</summary>
    public string? InputProblem { get; }

    private LinuxPlatform(AppSettings settings)
    {
        display = X11Display.Open();
        Game = new X11GameWindow(display, settings.GameWindowClass, settings.GameWindowTitle);
        Capture = new X11ScreenCapture(display, Game);
        Keys = new X11KeyState(display);
        Clipboard = new X11Clipboard(display);

        // Without /dev/uinput the app can still watch and price on hover; only scanning needs to move the mouse.
        try
        {
            device = new UinputDevice(() => display.ScreenSize());
            Input = new UinputInput(display, device);
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
        if (!p.display.HasExtension("XFIXES"))
            Log.Write("the X server has no XFIXES; clipboard changes cannot be detected");
        return p;
    }

    public void Dispose()
    {
        Clipboard.Dispose();
        device?.Dispose();
        display.Dispose();
    }
}
