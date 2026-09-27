using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Poe2StashPricer.App.Views;

namespace Poe2StashPricer.App;

public partial class App : Application
{
    /// <summary>Started with --overlay: no main window yet, just the prices over the game.</summary>
    public static bool OverlayOnly { get; set; }

    /// <summary>Started with --overlay-test: the overlay with made-up content, to see how it is drawn.</summary>
    public static bool OverlayTest { get; set; }

    public static bool OverlayTestRules { get; set; } = true;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OverlayTest) DebugOverlay.Attach(desktop, OverlayTestRules);
            else if (OverlayOnly) OverlayApp.Attach(desktop);
            else desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
