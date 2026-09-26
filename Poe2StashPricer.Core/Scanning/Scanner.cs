using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Poe2StashPricer.Detection;
using Poe2StashPricer.Platform;
using Poe2StashPricer.Pricing;
using Poe2StashPricer.Storage;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.Scanning;

/// <summary>Hovers each detected item, presses Ctrl+C and reads what the game copied.</summary>
public class Scanner
{
    private readonly ScanConfig cfg;
    private readonly IScreenCapture capture;
    private readonly IInput input;
    private readonly IKeyState keys;
    private readonly IClipboard clipboard;
    private readonly IGameWindow game;
    public volatile bool CancelRequested;

    public Scanner(ScanConfig cfg, IScreenCapture capture, IInput input, IKeyState keys, IClipboard clipboard, IGameWindow game)
    {
        this.cfg = cfg;
        this.capture = capture;
        this.input = input;
        this.keys = keys;
        this.clipboard = clipboard;
        this.game = game;
    }

    /// <summary>
    /// Moves the cursor onto the tab header row (so no item tooltip covers the stash), screenshots the game
    /// window, finds the stash panel and decides where the items are. Fills cfg.Region and cfg.CellSize.
    /// </summary>
    public static ScanPlan Prepare(ScanConfig cfg, ICollection<TabProfile> profiles, IScreenCapture capture, IInput input)
    {
        ScanPlan plan = new ScanPlan();
        Rectangle win = cfg.Window;
        Rectangle guess = StashLocator.Predict(win.Size);
        input.MoveMouse(win.X + guess.X + guess.Width / 2, win.Y + Math.Max(0, guess.Y - (int)(win.Height * 0.03)));
        Thread.Sleep(150);

        PixelBuffer full = CaptureStable(win, guess, capture);
        if (IsBlack(full, guess))
        {
            // Nothing usable came through: the capture was denied, or it came back black.
            Log.Write("capture of the game window " + win + " was denied or came back black");
            plan.BlackScreen = true;
            return plan;
        }
        StashLocator.Result loc = StashLocator.Locate(full);
        Log.Write(string.Format("stash search in window {0}: {1}/4 frame edges, region {2}, frame colour {3}",
            win, loc.EdgesFound, loc.Region, loc.FrameColor == null ? "none" : string.Join(",", Array.ConvertAll(loc.FrameColor, c => ((int)c).ToString()))));
        if (!loc.StashVisible && profiles != null && profiles.Count > 0)
        {
            // Faint frames (grey ones, at low resolution) can escape the frame search; if the predicted
            // area looks exactly like a saved tab, the stash is open all the same.
            double d;
            if (TabLibrary.Identify(full.Crop(loc.Region), null, profiles, out d) != null && d <= 0.15) loc.EdgesFound = 4;
        }
        plan.StashVisible = loc.StashVisible;
        if (!plan.StashVisible) return plan;

        plan.Snapshot = full.Crop(loc.Region);
        cfg.Region = new Rectangle(win.X + loc.Region.X, win.Y + loc.Region.Y, loc.Region.Width, loc.Region.Height);
        double difference = 1;
        plan.FrameColor = loc.FrameColor;
        plan.Tab = profiles == null ? null : TabLibrary.Identify(plan.Snapshot, loc.FrameColor, profiles, out difference);
        plan.Difference = difference;
        if (plan.Tab == null && profiles != null) plan.Candidate = TabLibrary.Candidate(plan.Snapshot, loc.FrameColor, profiles, out difference);
        if (profiles != null) Log.Write("tab recognised: " + (plan.Tab != null ? plan.Tab.Key : "none") + " (difference " + difference.ToString("0.00") + ")"
                                       + (plan.Candidate != null ? ", looks like " + plan.Candidate.Key + ": checked by its items after the scan" : ""));
        Size area = new Size(plan.Snapshot.Width, plan.Snapshot.Height);
        cfg.CellSize = Grid.CellSizeFor(area.Width);
        // One slot per item type, except in a paged tab (Tablets...): its cells hold many stacks of one item.
        cfg.FixedLayout = plan.Tab != null && !plan.Tab.Paged;
        plan.Groups = Grid.Plan(plan.Snapshot, cfg, plan.Tab != null ? plan.Tab.SlotsIn(area) : null, plan.Tab != null && plan.Tab.BuiltIn);
        return plan;
    }

    /// <summary>
    /// The game fades a tab in when it is opened; a picture taken during the fade is darker or washed
    /// out. Take pictures until the stash area (<paramref name="watch"/>, window coordinates) stops
    /// changing, at most about a second.
    /// </summary>
    private static PixelBuffer CaptureStable(Rectangle win, Rectangle watch, IScreenCapture capture)
    {
        PixelBuffer prev = capture.Capture(win);
        for (int i = 0; i < 10; i++)
        {
            Thread.Sleep(100);
            PixelBuffer cur = capture.Capture(win);
            if (MeanDifference(prev, cur, watch) < 1.0) return cur;
            prev = cur;
        }
        return prev;
    }

    /// <summary>True when the area is (nearly) uniformly black: what a capture the compositor denied gives.</summary>
    private static bool IsBlack(PixelBuffer pb, Rectangle area)
    {
        area.Intersect(new Rectangle(0, 0, pb.Width, pb.Height));
        long bright = 0; int n = 0;
        for (int y = area.Top; y < area.Bottom; y += 5)
            for (int x = area.Left; x < area.Right; x += 5)
            {
                int o = y * pb.Stride + x * 4;
                if (Math.Max(pb.Px[o], Math.Max(pb.Px[o + 1], pb.Px[o + 2])) > 8) bright++;
                n++;
            }
        return n > 0 && bright < n / 100;
    }

    public static double MeanDifference(PixelBuffer a, PixelBuffer b, Rectangle area)
    {
        area.Intersect(new Rectangle(0, 0, Math.Min(a.Width, b.Width), Math.Min(a.Height, b.Height)));
        long sum = 0; int n = 0;
        for (int y = area.Top; y < area.Bottom; y += 7)
            for (int x = area.Left; x < area.Right; x += 7)
            {
                int oa = y * a.Stride + x * 4, ob = y * b.Stride + x * 4;
                sum += Math.Abs(a.Px[oa] - b.Px[ob]) + Math.Abs(a.Px[oa + 1] - b.Px[ob + 1]) + Math.Abs(a.Px[oa + 2] - b.Px[ob + 2]);
                n++;
            }
        return n == 0 ? 0 : sum / (3.0 * n);
    }

    private bool ShouldAbort()
    {
        if (CancelRequested || keys.AbortPressed) return true;
        return game != null && !game.IsForeground();
    }

    /// <summary>
    /// Puts the user's clipboard text back. It may be a password copied from a password manager, which marks
    /// it as private; put back as plain text it would land in the clipboard manager's history. So the
    /// restored copy is offered in a way that keeps it out of there.
    /// </summary>
    private static void RestoreClipboard(string text, IClipboard clipboard)
    {
        try { clipboard.SetText(text, true); }
        catch { }
    }

    private static string ReadClipboard(IClipboard clipboard)
    {
        try { return clipboard.GetText(); }
        catch { return null; }
    }

    // How long the game took to answer Ctrl+C on recent items (milliseconds). Kept across scans, so a tab
    // with one item still knows how long an empty slot is worth waiting for.
    private static readonly List<double> copyLatency = new List<double>();

    /// <summary>
    /// Waiting for an empty slot is most of a scan's time. Once a few items showed how fast the game
    /// answers, wait a few times that long instead of the full configured timeout.
    /// </summary>
    private int EmptySlotTimeout()
    {
        if (copyLatency.Count < 4) return cfg.CopyTimeout;
        List<double> l = new List<double>(copyLatency);
        l.Sort();
        double p90 = l[Math.Min(l.Count - 1, (int)(l.Count * 0.9))];
        return (int)Math.Max(60, Math.Min(cfg.CopyTimeout, p90 * 3 + 30));
    }

    private string CopyHovered() { return CopyHovered(EmptySlotTimeout()); }

    private string CopyHovered(int timeout) { return CopyHovered(timeout, input, clipboard); }

    /// <summary>
    /// Copies the item the user's own mouse rests on (hover pricing): one Ctrl+C, the user's clipboard put back
    /// afterwards. Null when nothing was copied (an empty slot).
    /// </summary>
    public static string CopyItemUnderCursor(int timeout, IInput input, IClipboard clipboard)
    {
        string saved = ReadClipboard(clipboard);
        try { return CopyHovered(timeout, input, clipboard); }
        finally { if (saved != null) RestoreClipboard(saved, clipboard); }
    }

    private static string CopyHovered(int timeout, IInput input, IClipboard clipboard)
    {
        ulong seq = clipboard.ChangeCount;
        input.SendCopy();
        // Measure real time: Sleep(1) lasts up to a timer tick (about 15 ms), not 1 ms.
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        while (clipboard.ChangeCount == seq)
        {
            if (sw.ElapsedMilliseconds >= timeout) return null;   // nothing copied: empty slot
            Thread.Sleep(1);
        }
        lock (copyLatency)
        {
            copyLatency.Add(sw.Elapsed.TotalMilliseconds);
            if (copyLatency.Count > 60) copyLatency.RemoveAt(0);
        }
        Thread.Sleep(5);
        string txt = ReadClipboard(clipboard);
        return ItemParser.LooksLikeItem(txt) ? txt : null;
    }

    /// <summary>Median and slowest answer to Ctrl+C in this scan, for the log.</summary>
    public string LatencySummary()
    {
        if (copyLatency.Count == 0) return "no copies";
        List<double> l = new List<double>(copyLatency);
        l.Sort();
        return string.Format("copy answer median {0:0} ms, max {1:0} ms, empty-slot wait {2} ms", l[l.Count / 2], l[l.Count - 1], EmptySlotTimeout());
    }

    public ScanResult Run(ICollection<TabProfile> profiles, Func<ParsedItem, PriceInfo> lookup, Action<int, int> progress)
    {
        ScanResult res = new ScanResult();

        // Don't start while the user still holds the hotkey / modifiers.
        for (int i = 0; i < 100 && keys.ScanTriggerHeld; i++) Thread.Sleep(10);

        Point orig = input.GetCursorPos();
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        ScanPlan plan = Prepare(cfg, profiles, capture, input);
        long planMs = clock.ElapsedMilliseconds, firstMs = 0;
        res.Tab = plan.Tab;
        res.TabDifference = plan.Difference;
        res.Candidate = plan.Candidate;
        res.FrameColor = plan.FrameColor;
        if (!plan.StashVisible)
        {
            input.MoveMouse(orig.X, orig.Y);
            res.StashNotFound = true;
            res.BlackScreen = plan.BlackScreen;
            return res;
        }
        res.Snapshot = plan.Snapshot;
        List<ProbeGroup> groups = plan.Groups;

        int total = 0;
        foreach (ProbeGroup g in groups)
            foreach (bool a in g.Active) if (a) total++;

        string savedClipboard = ReadClipboard(clipboard);
        string last = null;
        int done = 0;
        try
        {
            foreach (ProbeGroup g in groups)
            {
                for (int r = 0; r < g.Rows && !res.Aborted; r++)
                    for (int c = 0; c < g.Cols; c++)
                    {
                        if (!g.Active[r, c]) continue;
                        if (ShouldAbort()) { res.Aborted = true; break; }
                        Rectangle cell = g.Rects[r, c];
                        int cx = cell.X + cell.Width / 2, cy = cell.Y + cell.Height / 2;
                        input.MoveMouse(cx + 1, cy + 1);
                        input.MoveMouse(cx, cy);
                        Thread.Sleep(cfg.HoverDelay);
                        string txt = CopyHovered();
                        if (txt != null && txt == last)
                        {
                            // Same text as the previous cell: either a genuine duplicate or the game hadn't
                            // updated the hover yet. Wait and read again to be sure.
                            Thread.Sleep(cfg.HoverDelay * 2);
                            txt = CopyHovered();
                        }
                        g.Texts[r, c] = txt;
                        last = txt;
                        res.CellsTried++;
                        if (txt != null) res.CellsCopied++;
                        if (progress != null) progress(++done, total);
                    }
                if (res.Aborted) break;
            }

            firstMs = clock.ElapsedMilliseconds - planMs;
            // Second chance for spots that copied nothing: the game can be late to update the tooltip (a busy
            // frame, the mouse arriving from far away). Spots that looked occupied get a longer look; saved
            // slots that look empty are tried again only while the game's answer time is still unknown.
            foreach (ProbeGroup g in groups)
                for (int r = 0; r < g.Rows && !res.Aborted; r++)
                    for (int c = 0; c < g.Cols; c++)
                    {
                        if (!g.Active[r, c] || g.Texts[r, c] != null) continue;
                        bool likely = g.Likely[r, c], measured = copyLatency.Count >= 4;
                        // Once the game's answer time is known, the first wait was already several times
                        // the slowest answer: only spots that looked occupied are worth another look.
                        if (!likely && measured) continue;
                        if (ShouldAbort()) { res.Aborted = true; break; }
                        Rectangle cell = g.Rects[r, c];
                        int cx = cell.X + cell.Width / 2, cy = cell.Y + cell.Height / 2;
                        input.MoveMouse(cx + 2, cy + 2);
                        Thread.Sleep(30);
                        input.MoveMouse(cx, cy);
                        Thread.Sleep(likely ? Math.Max(90, cfg.HoverDelay * 2) : cfg.HoverDelay);
                        string txt = CopyHovered(measured ? Math.Max(120, EmptySlotTimeout() * 2) : likely ? Math.Max(250, cfg.CopyTimeout) : cfg.CopyTimeout);
                        res.CellsRetried++;
                        if (txt == null) continue;
                        g.Texts[r, c] = txt;
                        res.CellsCopied++;
                        res.CellsRecovered++;
                    }
        }
        finally
        {
            input.MoveMouse(orig.X, orig.Y);
            if (savedClipboard != null) RestoreClipboard(savedClipboard, clipboard);
        }

        foreach (ProbeGroup g in groups) BuildItems(res, g, lookup);
        // In a saved fixed-slot tab every item type has exactly one slot, so the same text read at several
        // points is one item (a big slot, or two probes on one slot). Elsewhere two identical stacks can
        // sit side by side, so only reads closer than a cell are merged there.
        MergeNearDuplicates(res, cfg.FixedLayout ? double.MaxValue : cfg.CellSize * 0.85);
        long hoverMs = clock.ElapsedMilliseconds - planMs;
        ReadCountsFromScreen(res);
        res.Timing = string.Format("screenshot and plan {0} ms, hovering {1} ms, second tries {2} ms, counts {3} ms; {4}",
                                   planMs, firstMs, Math.Max(0, hoverMs - firstMs), clock.ElapsedMilliseconds - planMs - hoverMs, LatencySummary());
        return res;
    }

    /// <summary>
    /// Items whose text has a stack size teach the digit reader what the numbers look like; items that are
    /// stackable but have no stack size in their text get their count read from the screen.
    /// </summary>
    private void ReadCountsFromScreen(ScanResult res)
    {
        if (res.Snapshot == null) return;
        Point origin = cfg.Region.Location;
        foreach (ScanItem si in res.Items)
            if (si.Item.IsStackable && si.Qty > 0)
                DigitReader.Learn(res.Snapshot, Local(si.Bounds, origin), cfg.CellSize, si.Qty);
        foreach (ScanItem si in res.Items)
        {
            if (!si.Item.NeedsCount) continue;
            int n = DigitReader.Read(res.Snapshot, Local(si.Bounds, origin), cfg.CellSize);
            if (n > 0)
            {
                si.Qty = n;
                si.TotalDiv = si.Price != null ? si.Price.Div * n : 0;
            }
            else si.CountUnread = true;
        }
        DigitReader.Save();
    }

    private static Rectangle Local(Rectangle screen, Point origin)
    {
        screen.Offset(-origin.X, -origin.Y);
        return screen;
    }

    /// <summary>
    /// Two probes can land on the same slot (e.g. one of them only on its edge). Identical text from
    /// points closer than one cell is one item; genuine twin stacks sit a full cell apart.
    /// </summary>
    public static void MergeNearDuplicates(ScanResult res, double maxDist)
    {
        List<ScanItem> kept = new List<ScanItem>();
        foreach (ScanItem si in res.Items)
        {
            bool twin = false;
            foreach (ScanItem k in kept)
            {
                if (k.Text != si.Text) continue;
                double dx = (k.Bounds.X + k.Bounds.Width / 2.0) - (si.Bounds.X + si.Bounds.Width / 2.0);
                double dy = (k.Bounds.Y + k.Bounds.Height / 2.0) - (si.Bounds.Y + si.Bounds.Height / 2.0);
                if (Math.Sqrt(dx * dx + dy * dy) < maxDist)
                {
                    k.Bounds = Rectangle.Union(k.Bounds, si.Bounds);   // e.g. all cells of a 2x2 slot
                    twin = true;
                    break;
                }
            }
            if (!twin) kept.Add(si);
        }
        res.Items = kept;
    }

    private static void BuildItems(ScanResult res, ProbeGroup g, Func<ParsedItem, PriceInfo> lookup)
    {
        int rows = g.Rows, cols = g.Cols;
        string[,] texts = g.Texts;
        bool[,] seen = new bool[rows, cols];

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                string txt = texts[r, c];
                if (txt == null || seen[r, c]) continue;
                ParsedItem it = ItemParser.Parse(txt);
                if (it == null) continue;
                seen[r, c] = true;
                Rectangle bounds = g.Rects[r, c];

                if (it.IsMultiCellCandidate)
                {
                    // Big items (e.g. 2x3 armour) answer on every cell they cover: flood-fill identical neighbours.
                    Queue<Point> q = new Queue<Point>();
                    q.Enqueue(new Point(c, r));
                    while (q.Count > 0)
                    {
                        Point p = q.Dequeue();
                        Point[] nb = { new Point(p.X + 1, p.Y), new Point(p.X - 1, p.Y), new Point(p.X, p.Y + 1), new Point(p.X, p.Y - 1) };
                        foreach (Point n in nb)
                        {
                            if (n.X < 0 || n.Y < 0 || n.X >= cols || n.Y >= rows || seen[n.Y, n.X]) continue;
                            if (texts[n.Y, n.X] != txt) continue;
                            seen[n.Y, n.X] = true;
                            bounds = Rectangle.Union(bounds, g.Rects[n.Y, n.X]);
                            q.Enqueue(n);
                        }
                    }
                }

                ScanItem si = new ScanItem();
                si.Text = txt;
                si.Item = it;
                si.Qty = it.Stack;
                si.Price = lookup(it);
                si.TotalDiv = si.Price != null ? si.Price.Div * si.Qty : 0;
                si.Bounds = bounds;
                res.Items.Add(si);
            }
    }
}
