using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Poe2StashPricer.App.Platform.Linux;
using Poe2StashPricer.App.ViewModels;
using Poe2StashPricer.App.Views;
using Poe2StashPricer.Session;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App;

/// <summary>
/// The app: the platform, the session, the window that lists what was found and the prices drawn over the
/// game. With --overlay only the prices are shown, which is useful when the window is in the way.
/// </summary>
internal static class AppHost
{
    private static LinuxPlatform? _platform;
    private static PricerSession? _session;
    private static OverlayWindow? _overlay;
    private static ControlSocket? _control;

    /// <summary>Set from --hover: price what the mouse rests on instead of scanning the whole tab.</summary>
    public static bool HoverForThisRun { get; set; }

    /// <summary>Set from --overlay: the prices over the game, with no window of our own.</summary>
    public static bool OverlayOnly { get; set; }

    public static void Attach(IClassicDesktopStyleApplicationLifetime desktop)
    {
        AppSettings settings = AppSettings.Load();
        // --hover turns price-on-hover on for this run only; the settings file is left alone.
        if (HoverForThisRun) settings.HoverPrices = true;
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

        if (OverlayOnly)
        {
            _session.StatusChanged += text => Console.WriteLine("status: " + text);
        }
        else
        {
            MainWindowViewModel model = new MainWindowViewModel(_session, settings);
            desktop.MainWindow = new MainWindow { DataContext = model };
            desktop.MainWindow.Show();
        }

        desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
        // A key bound in the desktop runs "--scan", which arrives here.
        _control = new ControlSocket();
        _control.Command += command => Dispatcher.UIThread.Post(() =>
        {
            if (command == "scan") _session?.Scan();
            else if (command == "overlay") _session?.ToggleOverlay();
        });
        _control.Listen();

        desktop.Exit += (s, e) =>
        {
            _control?.Dispose();
            _session?.Dispose();
            _platform?.Dispose();
        };

        if (OverlayOnly)
        {
            Console.WriteLine(settings.HoverPrices
                ? "Price on hover. Open a stash tab; rest the mouse on an item and its price appears."
                : "Overlay running. Open a stash tab in the game; prices of scanned tabs appear over it.");
            Console.WriteLine("Bind a key to \"poe2-stash-pricer --scan\" to scan the open tab; Ctrl+C here to stop.");
        }
        _session.Start();
    }

    /// <summary>Asks for a scan, as a hotkey eventually will.</summary>
    public static void Scan()
    {
        _session?.Scan();
    }
}
