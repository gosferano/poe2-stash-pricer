using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Poe2StashPricer.Detection;
using Poe2StashPricer.Platform;
using Poe2StashPricer.Pricing;
using Poe2StashPricer.Scanning;
using Poe2StashPricer.Storage;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.Session;

/// <summary>What a scan came to, ready for the status line and the lists.</summary>
public class ScanOutcome
{
    public bool BlackScreen;
    public bool StashNotFound;
    public bool Aborted;
    public ScanConfig Config;
    public ScanResult Result;
    public string Key;            // which result the scan was filed under
    public string LearnedName;    // set when this scan taught the app a new tab
    public string Status;         // the sentence to show
}

/// <summary>Where a preview says the scan would look.</summary>
public class PreviewOutcome
{
    public bool BlackScreen;
    public bool StashNotFound;
    public ScanConfig Config;
    public ScanPlan Plan;
    public List<Rectangle> Probes = new List<Rectangle>();
    public string TabName;
    public string Status;
}

/// <summary>
/// Runs a scan of the open tab and files the result, learning the tab if it is one the app has not seen.
/// Everything here blocks: it is meant to be called from the session's own thread, never a UI one.
/// </summary>
public class ScanRunner
{
    private readonly IScreenCapture _capture;
    private readonly IInput _input;
    private readonly IKeyState _keys;
    private readonly IClipboard _clipboard;
    private readonly IGameWindow _game;
    private readonly StashModel _model;
    private readonly AppSettings _settings;
    private Scanner _running;

    public ScanRunner(IScreenCapture capture, IInput input, IKeyState keys, IClipboard clipboard, IGameWindow game,
                      StashModel model, AppSettings settings)
    {
        _capture = capture;
        _input = input;
        _keys = keys;
        _clipboard = clipboard;
        _game = game;
        _model = model;
        _settings = settings;
    }

    /// <summary>Stops a scan that is running; harmless when none is.</summary>
    public void Cancel()
    {
        Scanner sc = _running;
        if (sc != null) sc.CancelRequested = true;
    }

    public ScanConfig BuildConfig()
    {
        return new ScanConfig
        {
            Window = _game.ClientRectOnScreen(),
            TintSensitivity = Math.Max(1, _settings.TintSensitivity),
            Threshold = _settings.Threshold > 0 ? _settings.Threshold : 12,
            HoverDelay = _settings.HoverDelay,
            CopyTimeout = Math.Max(100, _settings.CopyTimeout),
        };
    }

    // ---- preview ----

    public PreviewOutcome Preview()
    {
        ScanConfig cfg = BuildConfig();
        PreviewOutcome outcome = new PreviewOutcome { Config = cfg };
        ScanPlan plan = Scanner.Prepare(cfg, _model.KnownTabs, _capture, _input);
        outcome.Plan = plan;
        if (plan.BlackScreen) { outcome.BlackScreen = true; return outcome; }
        if (!plan.StashVisible) { outcome.StashNotFound = true; return outcome; }

        foreach (ProbeGroup g in plan.Groups)
            for (int r = 0; r < g.Rows; r++)
                for (int c = 0; c < g.Cols; c++)
                    if (g.Active[r, c]) outcome.Probes.Add(g.Rects[r, c]);

        outcome.TabName = plan.Tab != null ? "Tab: " + TabLibrary.NameOf(plan.Tab.Key) : "New tab (learned on its first scan)";
        outcome.Status = string.Format("Preview: {0}, {1} positions. If items are missed, save the tab again.",
                                       outcome.TabName, outcome.Probes.Count);
        return outcome;
    }

    // ---- scanning ----

    public ScanOutcome Run(PriceTable table, Action<int, int> progress)
    {
        ScanConfig cfg = BuildConfig();
        ScanOutcome outcome = new ScanOutcome { Config = cfg };
        List<TabProfile> known = _model.KnownTabs.ToList();
        Scanner sc = new Scanner(cfg, _capture, _input, _keys, _clipboard, _game);
        _running = sc;
        try
        {
            ScanResult res = sc.Run(known, table.Lookup, progress);
            outcome.Result = res;
            outcome.Aborted = res.Aborted;
            if (res.BlackScreen) { outcome.BlackScreen = true; return outcome; }
            if (res.StashNotFound) { outcome.StashNotFound = true; return outcome; }

            outcome.LearnedName = LearnIfNew(res, cfg);
            string key = res.Tab != null && !res.Tab.Paged ? res.Tab.Key : StashModel.UnknownTab;
            outcome.Key = key;
            Log.Write(string.Format("scan done: tab {0}, {1} positions tried, {2} items read ({3} on a second try of {4}), {5} items, aborted={6}",
                                    key, res.CellsTried, res.CellsCopied, res.CellsRecovered, res.CellsRetried, res.Items.Count, res.Aborted));
            if (res.Timing != null) Log.Write("  timing: " + res.Timing);

            // Only when the tab was recognised for sure: slots of a look-alike tab must not get mixed in.
            if (outcome.LearnedName == null && res.Tab != null && !res.Tab.BuiltIn && !res.Aborted
                && res.TabDifference <= TabLibrary.SureMatch)
            {
                // Items in slots that were empty when the tab was learned: remember those slots too.
                Rectangle region = cfg.Region;
                List<Rectangle> found = res.Items
                    .Where(i => i.Bounds.Width <= cfg.CellSize * 2.3 && i.Bounds.Height <= cfg.CellSize * 2.3)   // at most a 2x2 slot
                    .Select(i => new Rectangle(i.Bounds.X - region.X, i.Bounds.Y - region.Y, i.Bounds.Width, i.Bounds.Height))
                    .ToList();
                int added = TabLibrary.AddSlots(res.Tab, found, region.Size);
                if (added > 0)
                {
                    TabLibrary.Save(res.Tab);
                    Log.Write("tab " + key + ": " + added + " new slots learned, " + res.Tab.Slots.Count + " in all");
                }
            }

            TabResult tr = ResultStore.FromScan(key, res, cfg.Region);
            tr.ValueAtScan = ResultStore.Total(tr, table);
            tr.PricesAtScan = table.LoadedAt;
            bool kept;
            if (res.Aborted && _model.ResultFor(key) != null)
            {
                outcome.Status = "Scan stopped; the previous result of this tab was kept.";
                kept = false;
            }
            else
            {
                _model.Remember(key, tr);
                kept = true;
            }
            if (kept) outcome.Status = Describe(res, key, outcome.LearnedName);
            return outcome;
        }
        finally { _running = null; }
    }

    /// <summary>The sentence shown after a scan.</summary>
    private static string Describe(ScanResult res, string key, string learnedName)
    {
        string status = string.Format("Scan done ({0}): {1} positions tried, {2} items read.",
                                      StashModel.NameOf(key), res.CellsTried, res.CellsCopied);
        if (res.Aborted) status = "Scan stopped (partial result). " + status;
        if (res.CellsTried > 0 && res.CellsCopied == 0)
            status = "No item text could be copied from the game! Try increasing the hover delay.";
        else if (res.CellsTried == 0)
            status = "No items found in this tab.";
        if (learnedName != null)
            status += string.Format(" New tab learned as '{0}' (named after its items; use Rename to change it).", learnedName);
        if (key == StashModel.UnknownTab)
            status += " This looks like a normal tab, so it is not saved or added to the total stash value.";
        int unread = res.Items.Count(i => i.CountUnread);
        if (unread > 0)
            status += string.Format(" The count of {0} items could not be read from the screen (shown with \"?\", counted as 1). "
                                    + "Digits are learned while scanning (known: {1}); scan other tabs, then rescan this one.",
                                    unread, DigitReader.Known == "" ? "none" : DigitReader.Known);
        return status;
    }

    // ---- learning a tab ----

    /// <summary>Share of item types in common between a scan and a saved result (of the smaller of the two).</summary>
    private static double SharedItems(ScanResult res, TabResult saved)
    {
        if (saved == null) return 0;
        HashSet<string> now = new HashSet<string>(res.Items.Where(i => i.Item != null && i.Item.Name != null).Select(i => i.Item.Name));
        HashSet<string> before = new HashSet<string>();
        foreach (SavedItem s in saved.Items)
        {
            ParsedItem it = ItemParser.Parse(s.Text);
            if (it != null && it.Name != null) before.Add(it.Name);
        }
        int smaller = Math.Min(now.Count, before.Count);
        if (smaller == 0) return 0;
        return (double)now.Count(n => before.Contains(n)) / smaller;
    }

    /// <summary>
    /// The first scan of a tab teaches it: if the scanned tab isn't one of the saved ones and looks like a
    /// special (fixed-slot) tab, it is saved under a name guessed from its items. Returns that name, or null.
    /// </summary>
    private string LearnIfNew(ScanResult res, ScanConfig cfg)
    {
        if (res.Tab != null || res.Aborted || res.Snapshot == null) return null;
        string why;
        if (res.Candidate != null)
        {
            // The picture is similar to a saved tab but not the same. The items decide: the same tab still
            // holds mostly the same items; a look-alike sub-tab (Kalguuran Runes next to Runes) holds others.
            double shared = SharedItems(res, _model.ResultFor(res.Candidate.Key));
            if (shared >= 0.5)
            {
                Scanner.MergeNearDuplicates(res, double.MaxValue);   // a special tab: one slot per item type
                res.Tab = res.Candidate;
                TabLibrary.Refresh(res.Tab, res.Snapshot, res.FrameColor);
                TabLibrary.AddSlots(res.Tab,
                    res.Items.Select(i => new Rectangle(i.Bounds.X - cfg.Region.X, i.Bounds.Y - cfg.Region.Y, i.Bounds.Width, i.Bounds.Height)),
                    cfg.Region.Size);
                TabLibrary.Save(res.Tab);
                Log.Write(string.Format("tab '{0}' recognised by its items ({1:0%} the same), its picture was updated", res.Tab.Name, shared));
                return null;
            }
            Log.Write(string.Format("looked like '{0}' but holds other items ({1:0%} the same): a different tab", res.Candidate.Name, shared));
        }
        string guess = TabLibrary.GuessSpecialTab(res, cfg.Region, out why);
        if (guess == null)
        {
            Log.Write("tab not learned: " + why);
            return null;
        }

        // It's a special tab: every item type has one slot, so repeated reads of one item (the cells of a
        // big slot) are one item. The first scan didn't know that yet.
        Scanner.MergeNearDuplicates(res, double.MaxValue);

        string key, name;
        TabLibrary.NewKey(guess, _model.Profiles.Keys.ToList(), out key, out name);
        List<Rectangle> itemSlots = res.Items.Select(i =>
        {
            Rectangle b = i.Bounds;
            b.Offset(-cfg.Region.X, -cfg.Region.Y);
            return b;
        }).ToList();
        TabProfile learned = TabLibrary.Learn(key, name, res.Snapshot, res.FrameColor, cfg, itemSlots);
        learned.Kind = guess;
        TabLibrary.Save(learned);
        _model.Add(learned);
        res.Tab = learned;
        Log.Write("tab learned: " + key + " '" + name + "', " + learned.Slots.Count + " slots");
        return name;
    }
}
