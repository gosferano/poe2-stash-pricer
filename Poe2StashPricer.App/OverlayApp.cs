using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Poe2StashPricer.App.Platform.Linux;
using Poe2StashPricer.App.Views;
using Poe2StashPricer.Session;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App;

/// <summary>
/// The overlay on its own, with no main window: the session follows the game and the prices appear over the
/// stash. This is the app as far as it goes until the rest of the interface exists.
/// </summary>
internal static class OverlayApp
{
    private static LinuxPlatform? _platform;
    private static PricerSession? _session;
    private static OverlayWindow? _overlay;

    public static void Attach(IClassicDesktopStyleApplicationLifetime desktop)
    {
        AppSettings settings = AppSettings.Load();
        _platform = LinuxPlatform.Create(settings);
        if (_platform.Input == null)
        {
            Console.Error.WriteLine("the mouse cannot be moved: " + _platform.InputProblem);
            Console.Error.WriteLine("the overlay will still show saved prices.");
        }

        // Before the window appears: a compositor decides a window's rules when it is mapped.
        if (settings.ApplyCompositorRules) Platform.Hyprland.HyprlandRules.Apply();

        _overlay = new OverlayWindow(_platform.Display);
        _session = new PricerSession(settings, _platform.Capture, _platform.Game, _platform.Input!,
                                     _platform.Keys, _platform.Clipboard);
        _session.ScanKeyName = settings.ScanKey.ToString();
        _session.OverlayKeyName = settings.OverlayKey.ToString();

        // The session runs on its own thread; only this thread may touch a window.
        _session.OverlayChanged += content => Dispatcher.UIThread.Post(() => _overlay.Update(content));
        _session.StatusChanged += text => Console.WriteLine("status: " + text);

        desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
        desktop.Exit += (s, e) =>
        {
            _session?.Dispose();
            _platform?.Dispose();
        };

        Console.WriteLine("Overlay running. Open a stash tab in the game; prices of scanned tabs appear over it.");
        Console.WriteLine("Ctrl+C here to stop.");
        _session.Start();
    }

    /// <summary>Asks for a scan, as a hotkey eventually will.</summary>
    public static void Scan()
    {
        _session?.Scan();
    }
}
