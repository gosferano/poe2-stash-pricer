using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Poe2StashPricer.App.Platform.Linux;
using Poe2StashPricer.Detection;
using Poe2StashPricer.Pricing;
using Poe2StashPricer.Scanning;
using Poe2StashPricer.Storage;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.App;

/// <summary>
/// The platform layer on the command line, before there is any UI to drive it: --debug-scan prices the open
/// stash tab and prints what it read. It is also how the platform layer is checked against the real game.
/// </summary>
internal static class DebugScan
{
    public static int Run(bool withPrices)
    {
        AppSettings settings = AppSettings.Load();
        using LinuxPlatform platform = LinuxPlatform.Create(settings);

        if (!platform.Game.Find())
        {
            Console.Error.WriteLine("Path of Exile 2 was not found (looking for WM_CLASS \"" + settings.GameWindowClass
                                    + "\" or a title containing \"" + settings.GameWindowTitle + "\").");
            return 2;
        }
        Console.WriteLine("game window at " + platform.Game.ClientRectOnScreen());

        if (!platform.Game.IsForeground())
        {
            Console.Error.WriteLine("the game is not in front (" + platform.Game.ForegroundDescription()
                                    + "). Bring it up first: the scan reads what the game shows and types into it.");
            return 3;
        }
        if (platform.Input == null)
        {
            Console.Error.WriteLine("the mouse cannot be moved: " + platform.InputProblem);
            return 4;
        }

        PriceTable? prices = null;
        if (withPrices)
        {
            string league = settings.League ?? FirstLeague();
            Console.WriteLine("loading poe.ninja prices for " + league + "...");
            prices = PriceService.Load(league, null);
            Console.WriteLine("  " + prices.Count + " prices, 1 div = " + prices.ExPerDiv.ToString("0.#") + " ex"
                              + (prices.Failed.Count > 0 ? ", " + prices.Failed.Count + " categories failed" : ""));
        }

        ScanConfig cfg = new ScanConfig
        {
            Window = platform.Game.ClientRectOnScreen(),
            TintSensitivity = settings.TintSensitivity,
            Threshold = settings.Threshold,
            HoverDelay = settings.HoverDelay,
            CopyTimeout = settings.CopyTimeout,
        };
        Dictionary<string, TabProfile> known = TabLibrary.LoadAll();
        Scanner scanner = new Scanner(cfg, platform.Capture, platform.Input, platform.Keys, platform.Clipboard, platform.Game);

        Stopwatch sw = Stopwatch.StartNew();
        ScanResult res = scanner.Run(known.Values, it => prices?.Lookup(it), null);
        sw.Stop();

        if (res.BlackScreen)
        {
            Console.Error.WriteLine("the game window came back black, so nothing could be read.");
            return 5;
        }
        if (res.StashNotFound)
        {
            Console.Error.WriteLine("no stash panel on screen. Open a stash tab and try again.");
            return 6;
        }

        Console.WriteLine();
        Console.WriteLine("tab      " + (res.Tab == null ? "not recognised" : res.Tab.Name + " (" + res.Tab.Key + ")")
                          + ", difference " + res.TabDifference.ToString("0.000"));
        Console.WriteLine("cells    " + res.CellsTried + " hovered, " + res.CellsCopied + " copied, "
                          + res.CellsRetried + " retried, " + res.CellsRecovered + " recovered");
        Console.WriteLine("timing   " + res.Timing);
        Console.WriteLine();

        double total = 0;
        foreach (ScanItem si in res.Items)
        {
            total += si.TotalDiv;
            Console.WriteLine(string.Format("  {0,-44} x{1,-7} {2}", si.Item.DisplayName, si.Qty,
                si.Price == null ? (withPrices ? "no price" : "")
                                 : si.TotalDiv.ToString("0.###") + " div"
                                   + (si.CountUnread ? "  (count unread)" : "")));
        }
        Console.WriteLine();
        Console.WriteLine(res.Items.Count + " items" + (withPrices ? ", " + total.ToString("0.##") + " div in total" : ""));
        if (res.Aborted) Console.WriteLine("(the scan was stopped early)");
        return 0;
    }

    private static string FirstLeague()
    {
        try
        {
            List<string> leagues = PriceService.GetLeagues();
            if (leagues.Count > 0) return leagues[0];
        }
        catch (Exception ex) { Log.Write("leagues could not be loaded: " + ex.Message); }
        return "Standard";
    }
}
