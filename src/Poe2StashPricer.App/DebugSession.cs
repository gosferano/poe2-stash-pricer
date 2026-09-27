using System;
using System.Threading;
using Poe2StashPricer.App.Platform.Linux;
using Poe2StashPricer.Session;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App;

/// <summary>
/// --debug-session: runs the whole session and prints what it would show. This is the app without a window:
/// the same loop, the same scan, the same overlay decisions, so all of it can be checked against the real
/// game before any of it is drawn.
/// </summary>
internal static class DebugSession
{
    public static int Run(int seconds, bool scanAfterStart)
    {
        AppSettings settings = AppSettings.Load();
        using LinuxPlatform platform = LinuxPlatform.Create(settings);
        if (platform.Input == null)
        {
            Console.Error.WriteLine("the mouse cannot be moved: " + platform.InputProblem);
            return 4;
        }

        using PricerSession session = new PricerSession(settings, platform.Capture, platform.Game,
                                                        platform.Input, platform.Keys, platform.Clipboard);
        session.ScanKeyName = settings.ScanKey.ToString();
        session.OverlayKeyName = settings.OverlayKey.ToString();

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        string lastOverlay = "";

        session.StatusChanged += text => Console.WriteLine(Stamp(clock) + "status: " + text);
        session.BusyChanged += busy => Console.WriteLine(Stamp(clock) + (busy ? "scanning..." : "idle"));
        session.ScanProgress += (done, total) =>
        {
            if (done == total || done % 10 == 0) Console.WriteLine(Stamp(clock) + "  hovered " + done + "/" + total);
        };
        session.ViewChanged += () =>
        {
            string tab = session.Model.CurrentTab == null ? "none" : StashModel.NameOf(session.Model.CurrentTab);
            Console.WriteLine(Stamp(clock) + "tab on screen: " + tab
                              + "  ·  stash total " + Money.Format(session.Model.GrandTotal(session.Prices.Table),
                                                                  session.Prices.Table, settings.DisplayCurrency));
        };
        session.OverlayChanged += content =>
        {
            string line = content.Kind == OverlayKind.Hidden
                ? "overlay: hidden"
                : string.Format("overlay: {0}, {1} labels, at {2}{3}", content.Kind, content.Labels.Count, content.Region,
                                content.Note != null ? "  note: " + content.Note : "");
            if (line == lastOverlay) return;
            lastOverlay = line;
            Console.WriteLine(Stamp(clock) + line);
            if (content.Header != null) Console.WriteLine(Stamp(clock) + "  header: " + content.Header);
        };

        Console.WriteLine("session running for " + seconds + " s. Open the stash, switch tabs, hover items.");
        Console.WriteLine("known tabs: " + session.Model.Profiles.Count + " (" + session.Model.ScannedTabs + " scanned)");
        Console.WriteLine("hover pricing: " + (settings.HoverPrices ? "on" : "off") + "; clipboard: " + platform.ClipboardKind);
        Console.WriteLine();
        session.Start();

        bool scanned = false;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            Thread.Sleep(200);
            if (scanAfterStart && !scanned && clock.Elapsed.TotalSeconds > 4)
            {
                scanned = true;
                Console.WriteLine(Stamp(clock) + "(pressing the scan key)");
                session.Scan();
            }
        }
        return 0;
    }

    private static string Stamp(System.Diagnostics.Stopwatch clock)
    {
        return string.Format("[{0,6:0.0}s] ", clock.Elapsed.TotalSeconds);
    }
}
