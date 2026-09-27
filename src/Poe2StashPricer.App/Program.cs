using System;
using System.Linq;
using Avalonia;
using Avalonia.X11;

namespace Poe2StashPricer.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Before there is a UI, the platform layer is driven from the command line.
        // One copy at a time. Two would both take the hotkeys, both drive the mouse and both borrow the
        // clipboard, which is a good way to make a scan fight itself. A second start brings the first one
        // forward instead.
        if (Array.IndexOf(args, "--overlay") < 0 && Array.IndexOf(args, "--scan") < 0
            && Array.IndexOf(args, "--toggle-overlay") < 0 && Array.IndexOf(args, "--quit") < 0
            && !args.Any(a => a.StartsWith("--debug"))
            && Platform.Linux.ControlSocket.Send("show"))
        {
            Console.WriteLine("PoE2 Stash Pricer is already running; its window has been brought to the front.");
            return 0;
        }

        // Talking to a copy that is already running: what a key binding invokes.
        if (Array.IndexOf(args, "--scan") >= 0 && Array.IndexOf(args, "--debug-session") < 0)
            return Platform.Linux.ControlSocket.Send("scan") ? 0 : Nobody("--scan");
        if (Array.IndexOf(args, "--toggle-overlay") >= 0)
            return Platform.Linux.ControlSocket.Send("overlay") ? 0 : Nobody("--toggle-overlay");
        if (Array.IndexOf(args, "--quit") >= 0)
            return Platform.Linux.ControlSocket.Send("quit") ? 0 : Nobody("--quit");

        if (Array.IndexOf(args, "--debug-scan") >= 0)
            return DebugScan.Run(Array.IndexOf(args, "--prices") >= 0,
                                 Array.IndexOf(args, "--hold") >= 0 ? 15 : 0);
        if (Array.IndexOf(args, "--debug-session") >= 0)
            return DebugSession.Run(60, Array.IndexOf(args, "--scan") >= 0);
        if (Array.IndexOf(args, "--debug-watch") >= 0)
            return DebugWatch.Run(60, Array.IndexOf(args, "--prices") >= 0);
        if (Array.IndexOf(args, "--debug-wayland") >= 0)
            return DebugWayland.Run();
        if (Array.IndexOf(args, "--debug-watch-clipboard") >= 0)
            return DebugWayland.Watch(30);
        if (Array.IndexOf(args, "--debug-clipboard") >= 0)
            return DebugClipboard.Run(12, Array.IndexOf(args, "--plain") < 0);

        AppHost.OverlayOnly = Array.IndexOf(args, "--overlay") >= 0;
        AppHost.HoverForThisRun = Array.IndexOf(args, "--hover") >= 0;
        App.OverlayTest = Array.IndexOf(args, "--overlay-test") >= 0;
        App.OverlayTestRules = Array.IndexOf(args, "--no-rules") < 0;
        DebugOverlay.Bare = Array.IndexOf(args, "--bare") >= 0;
        int at = Array.IndexOf(args, "--at");
        if (at >= 0 && at + 1 < args.Length)
        {
            string[] parts = args[at + 1].Split(',');
            if (parts.Length == 4)
                DebugOverlay.Where = new System.Drawing.Rectangle(int.Parse(parts[0]), int.Parse(parts[1]),
                                                                  int.Parse(parts[2]), int.Parse(parts[3]));
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static int Nobody(string what)
    {
        Console.Error.WriteLine(what + ": no running PoE2 Stash Pricer to tell. Start the app first.");
        return 1;
    }

    // Avalonia configuration; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // A dropdown is normally a window of its own, which leaves the compositor free to place it -
            // and a rule like "centre every floating window" then puts it in the middle of the screen.
            // Drawing popups inside their parent window instead keeps them where they belong, whatever
            // the desktop's rules say.
            .With(new X11PlatformOptions { OverlayPopups = true })
            .WithInterFont()
            .LogToTrace();
}
