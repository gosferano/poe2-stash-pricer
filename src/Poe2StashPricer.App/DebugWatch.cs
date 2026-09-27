using System;
using System.Drawing;
using System.Threading;
using Poe2StashPricer.App.Platform.Linux;
using Poe2StashPricer.Session;
using Poe2StashPricer.Storage;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.App;

/// <summary>
/// --debug-watch: follows the game and prints which tab it thinks is open, every time that changes. This is
/// the loop PricerSession will run, with printing where the overlay and the lists will be, so the watching
/// can be checked against the real game before there is any window to show it in.
/// </summary>
internal static class DebugWatch
{
    public static int Run(int seconds, bool withPrices)
    {
        AppSettings settings = AppSettings.Load();
        using LinuxPlatform platform = LinuxPlatform.Create(settings);
        if (!platform.Game.Find())
        {
            Console.Error.WriteLine("Path of Exile 2 was not found.");
            return 2;
        }

        StashModel model = new StashModel();
        model.MoveLearnedTabsToLayouts();
        PriceFeed prices = new PriceFeed(settings);
        prices.Status += text => Console.WriteLine("  prices: " + text);
        if (withPrices) prices.LoadPricesAsync(false).GetAwaiter().GetResult();

        StashWatcher watcher = new StashWatcher(platform.Capture, platform.Game, platform.Input!);

        Console.WriteLine("watching for " + seconds + " s. Open the stash and switch tabs; every change is printed.");
        Console.WriteLine("known tabs: " + model.Profiles.Count + " (" + model.ScannedTabs + " scanned)");
        Console.WriteLine();

        string lastLine = "";
        bool wasFocused = true;
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            Thread.Sleep(120);
            if (!platform.Game.IsForeground())
            {
                if (wasFocused) Console.WriteLine(Stamp(clock) + "the game is not in front (" + platform.Game.ForegroundDescription() + ")");
                wasFocused = false;
                continue;
            }
            if (!wasFocused)
            {
                Console.WriteLine(Stamp(clock) + "the game is in front again");
                wasFocused = true;
            }

            bool cursorOver;
            if (!watcher.DueForLook(out cursorOver)) continue;

            StashSighting seen;
            try { seen = watcher.Look(cursorOver, model); }
            catch (Exception ex) { Console.WriteLine(Stamp(clock) + "look failed: " + ex.Message); continue; }
            if (seen == null) continue;
            if (seen.Ignore)
            {
                Report(clock, ref lastLine, "(a tooltip, probably: keeping " + Show(model.CurrentTab) + ")");
                continue;
            }

            // What PricerSession will do with a sighting: a paged tab shows one of several pages, so like an
            // unrecognised tab its last scan is not kept under its own key.
            string? key = seen.StashVisible && seen.Tab != null && !seen.Tab.Paged ? seen.Tab.Key : null;
            model.CurrentTab = key;

            string line = !seen.StashVisible
                ? "stash closed"
                : string.Format("{0}  region {1}  cursor {2}", Show(key), seen.Region, cursorOver ? "over the stash" : "elsewhere");
            if (seen.StashVisible && seen.Tab == null) line += "  (frame colour "
                + (seen.Loc.FrameColor == null ? "none" : string.Join(",", Array.ConvertAll(seen.Loc.FrameColor, c => ((int)c).ToString()))) + ")";
            Report(clock, ref lastLine, line);

            // Not recognised: say how close each layout came, so a near miss is told from a different tab.
            if (seen.StashVisible && seen.Tab == null && seen.Full != null)
            {
                Poe2StashPricer.Detection.PixelBuffer area = seen.Full.Crop(seen.Loc.Region);
                System.Collections.Generic.List<double> sig = TabLibrary.Signature(area), mask = TabLibrary.ItemMask(area);
                System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>> ranked =
                    new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>>();
                foreach (TabProfile tp in model.KnownTabs)
                    ranked.Add(new System.Collections.Generic.KeyValuePair<string, double>(tp.Key,
                        TabLibrary.Distance(sig, mask, tp.Signature, tp.ItemMask)));
                ranked.Sort((x, y) => x.Value.CompareTo(y.Value));
                string top = "";
                for (int i = 0; i < 3 && i < ranked.Count; i++)
                    top += (i > 0 ? ", " : "") + ranked[i].Key + " " + ranked[i].Value.ToString("0.000");
                Console.WriteLine(Stamp(clock) + "    closest layouts: " + top
                                  + "   (a built-in needs <= 0.20 with a 0.05 lead, or <= 0.50 at twice the next one's distance)");
            }
        }
        return 0;
    }

    private static void Report(System.Diagnostics.Stopwatch clock, ref string last, string line)
    {
        if (line == last) return;
        last = line;
        Console.WriteLine(Stamp(clock) + line);
    }

    private static string Stamp(System.Diagnostics.Stopwatch clock)
    {
        return string.Format("[{0,6:0.0}s] ", clock.Elapsed.TotalSeconds);
    }

    private static string Show(string? key)
    {
        return key == null ? "tab not recognised" : StashModel.NameOf(key);
    }
}
