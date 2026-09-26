using System;
using Avalonia;

namespace Poe2StashPricer.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Before there is a UI, the platform layer is driven from the command line.
        if (Array.IndexOf(args, "--debug-scan") >= 0)
            return DebugScan.Run(Array.IndexOf(args, "--prices") >= 0,
                                 Array.IndexOf(args, "--hold") >= 0 ? 15 : 0);
        if (Array.IndexOf(args, "--debug-watch") >= 0)
            return DebugWatch.Run(60, Array.IndexOf(args, "--prices") >= 0);
        if (Array.IndexOf(args, "--debug-wayland") >= 0)
            return DebugWayland.Run();
        if (Array.IndexOf(args, "--debug-watch-clipboard") >= 0)
            return DebugWayland.Watch(30);
        if (Array.IndexOf(args, "--debug-clipboard") >= 0)
            return DebugClipboard.Run(12, Array.IndexOf(args, "--plain") < 0);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
