using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Poe2StashPricer.App.Views;

namespace Poe2StashPricer.App;

public partial class App : Application
{
    /// <summary>Started with --overlay: no main window yet, just the prices over the game.</summary>
    public static bool OverlayOnly { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OverlayOnly) OverlayApp.Attach(desktop);
            else desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
