using System;
using Avalonia;
using Avalonia.Controls;
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
    private static GlobalShortcutsPortal? _shortcuts;
    private static readonly System.Collections.Generic.List<System.Runtime.InteropServices.PosixSignalRegistration> _signals
        = new System.Collections.Generic.List<System.Runtime.InteropServices.PosixSignalRegistration>();

    private static async System.Threading.Tasks.Task RegisterShortcutsAsync(AppSettings settings)
    {
        bool registered = await _shortcuts!.RegisterAsync(new[]
        {
            ("scan", Platform.Hyprland.HyprlandRules.ScanDescription, settings.ScanKey.ToString()),
            ("overlay", Platform.Hyprland.HyprlandRules.OverlayDescription, settings.OverlayKey.ToString()),
        });
        if (settings.ApplyCompositorRules)
            Platform.Hyprland.HyprlandRules.Bind(settings.ScanKey.ToString(), settings.OverlayKey.ToString(), registered);
    }

    /// <summary>Set from --hover: price what the mouse rests on instead of scanning the whole tab.</summary>
    public static bool HoverForThisRun { get; set; }

    /// <summary>Set from --overlay: the prices over the game, with no window of our own.</summary>
    public static bool OverlayOnly { get; set; }

    /// <summary>
    /// Brings the window forward, for when a second copy starts and bows out. Each step is tried on its
    /// own: a compositor may refuse to raise or focus a window, and being refused is not worth dying over.
    /// </summary>
    private static void Raise(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Window? window = desktop.MainWindow;
        if (window == null) return;
        try { window.Show(); } catch (Exception ex) { Log.Write("the window would not show: " + ex.Message); }
        try { window.WindowState = WindowState.Normal; } catch (Exception ex) { Log.Write("the window would not restore: " + ex.Message); }
        try { window.Activate(); } catch (Exception ex) { Log.Write("the window would not take focus: " + ex.Message); }
    }

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

        // Closing the window ends the app. There is no tray icon, so a copy left running without one
        // would be invisible, would keep the hotkeys to itself and would answer the launcher with a
        // window it can no longer show. --overlay has no window of its own and runs until interrupted.
        desktop.ShutdownMode = OverlayOnly
            ? Avalonia.Controls.ShutdownMode.OnExplicitShutdown
            : Avalonia.Controls.ShutdownMode.OnMainWindowClose;
        // A key bound in the desktop runs "--scan", which arrives here.
        _control = new ControlSocket();
        _control.Command += command => Dispatcher.UIThread.Post(() =>
        {
            // Whatever a command asks for, it must not be able to bring the app down: this runs on the
            // interface thread, where an exception nobody catches ends the process.
            try
            {
                if (command == "scan") _session?.Scan();
                else if (command == "overlay") _session?.ToggleOverlay();
                else if (command == "show") Raise(desktop);
            }
            catch (Exception ex) { Log.Write("the command '" + command + "' failed: " + ex); }
        });
        _control.Listen();

        // The desktop's own shortcuts come first: they show up in its settings, can be rebound there and
        // work the same everywhere. Hyprland is then asked to point the keys at them, so nothing has to be
        // set up by hand; on a desktop that cannot be asked, the user binds the shortcut themselves.
        _shortcuts = new GlobalShortcutsPortal();
        _shortcuts.Pressed += id => Dispatcher.UIThread.Post(() =>
        {
            if (id == "scan") _session?.Scan();
            else if (id == "overlay") _session?.ToggleOverlay();
        });
        _ = RegisterShortcutsAsync(settings);

        // Ctrl+C, a logout or a "systemctl stop" should end the app the ordinary way rather than kill it
        // where it stands: the keys have to go back to the desktop and the virtual input device be closed.
        foreach (System.Runtime.InteropServices.PosixSignal signal in new[]
                 {
                     System.Runtime.InteropServices.PosixSignal.SIGINT,
                     System.Runtime.InteropServices.PosixSignal.SIGTERM,
                     System.Runtime.InteropServices.PosixSignal.SIGHUP,
                 })
            _signals.Add(System.Runtime.InteropServices.PosixSignalRegistration.Create(signal, context =>
            {
                Log.Write(context.Signal + " received: shutting down");
                context.Cancel = true;
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
            }));

        desktop.Exit += (s, e) =>
        {
            if (settings.ApplyCompositorRules)
                Platform.Hyprland.HyprlandRules.Unbind(settings.ScanKey.ToString(), settings.OverlayKey.ToString());
            _shortcuts?.DisposeAsync().AsTask().Wait(500);
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
