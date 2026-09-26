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

/// <summary>The tab that is ready for hover pricing: where its slots are and how it looked when made ready.</summary>
public class HoverFrame
{
    public string Key;             // result key: the tab's key, or UnknownTab (not recognised, or paged)
    public string TabKey;          // the recognised tab, or null
    public Rectangle Region;       // the stash area on screen
    public List<Rectangle> Slots;  // on screen; null = not a known tab, every cell counts
    public bool Exact;             // Slots are a built-in layout: every slot is in the list
    public bool Fixed;             // one slot per item type
    public double CellSize;
    public double[] FrameColor;
    public PixelBuffer Snapshot;   // the stash as it looked when made ready (or last checked)
}

/// <summary>
/// Prices the item the user's own mouse rests on, instead of the app hovering every slot. When the mouse
/// stops on a slot that one item is copied (Ctrl+C, as a scan does) and priced. The tab must be made ready
/// first so its slots are known: a tab the app knows is made ready when it is opened, any other one with
/// the scan key.
/// </summary>
public class HoverPricer
{
    private const int RestMs = 150;        // the mouse stays this long on a slot before its item is read
    private const int HoverCopyWait = 300; // the game's answer to Ctrl+C; nothing by then = an empty slot

    private readonly IScreenCapture _capture;
    private readonly IGameWindow _game;
    private readonly IInput _input;
    private readonly IKeyState _keys;
    private readonly IClipboard _clipboard;
    private readonly StashModel _model;
    private readonly Func<PriceTable> _prices;
    private readonly AppSettings _settings;

    private Point _restPos;
    private DateTime _restSince = DateTime.MaxValue;
    private Rectangle _lastSlot;           // slot copied last; copied again only after the mouse was elsewhere
    private bool _clickHeld;
    private int _recheckTries;
    private DateTime _recheckAt = DateTime.MaxValue;

    public HoverPricer(IScreenCapture capture, IGameWindow game, IInput input, IKeyState keys, IClipboard clipboard,
                       StashModel model, Func<PriceTable> prices, AppSettings settings)
    {
        _capture = capture;
        _game = game;
        _input = input;
        _keys = keys;
        _clipboard = clipboard;
        _model = model;
        _prices = prices;
        _settings = settings;
    }

    public HoverFrame Frame { get; private set; }

    /// <summary>A tab was made ready: where it is, how it looked and which result it belongs to.</summary>
    public event Action<Rectangle, PixelBuffer, string> Armed;

    /// <summary>Something was priced or dropped, so the lists and overlay are out of date.</summary>
    public event Action Changed;

    public event Action<string> Status;

    public void Clear()
    {
        Frame = null;
        _lastSlot = Rectangle.Empty;
        _recheckTries = 0;
        _recheckAt = DateTime.MaxValue;
    }

    private void Say(string text)
    {
        Action<string> handler = Status;
        if (handler != null) handler(text);
    }

    private void Touched()
    {
        Action handler = Changed;
        if (handler != null) handler();
    }

    /// <summary>The ready tab is still the one on screen (same place, same tab or at least the same frame colour).</summary>
    public bool FrameOnScreen(TabProfile tab, StashLocator.Result loc, Rectangle stashRegion, bool stashVisible)
    {
        if (!_settings.HoverPrices || Frame == null || !stashVisible || Frame.Region != stashRegion) return false;
        if (tab != null) return Frame.TabKey == tab.Key;
        return Frame.TabKey == null && Frame.FrameColor != null && loc.FrameColor != null
               && StashLocator.ColorDistance(Frame.FrameColor, loc.FrameColor) <= 0.15;
    }

    /// <summary>True when the tab on screen still has to be made ready for hover pricing.</summary>
    public bool NeedsArming(TabProfile tab, Rectangle stashRegion)
    {
        return _settings.HoverPrices && tab != null
               && (Frame == null || Frame.TabKey != tab.Key || Frame.Region != stashRegion);
    }

    /// <summary>Makes the tab in <paramref name="full"/> (a capture of <paramref name="win"/>) ready.</summary>
    public void Arm(Rectangle win, PixelBuffer full, StashLocator.Result loc, TabProfile tab)
    {
        Rectangle region = new Rectangle(win.X + loc.Region.X, win.Y + loc.Region.Y, loc.Region.Width, loc.Region.Height);
        PixelBuffer pb = full.Crop(loc.Region);
        string key = tab != null && !tab.Paged ? tab.Key : StashModel.UnknownTab;
        bool sameUnknown = Frame != null && Frame.Key == StashModel.UnknownTab && key == StashModel.UnknownTab
                           && Frame.Region == region && Frame.TabKey == (tab != null ? tab.Key : null);
        HoverFrame f = new HoverFrame
        {
            Key = key,
            TabKey = tab != null ? tab.Key : null,
            Region = region,
            CellSize = Grid.CellSizeFor(region.Width),
            FrameColor = loc.FrameColor,
            Snapshot = pb,
            Exact = tab != null && tab.BuiltIn,
            Fixed = tab != null && !tab.Paged,
        };
        if (tab != null) f.Slots = tab.SlotsIn(region.Size).Select(s => { s.Offset(region.Location); return s; }).ToList();
        Frame = f;
        _lastSlot = Rectangle.Empty;
        if (key == StashModel.UnknownTab && !sameUnknown) _model.UnknownResult = null;   // another unsaved tab or page
        Log.Write(string.Format("hover prices: tab ready: {0} ({1})", tab != null ? tab.Key : "not recognised",
                                f.Slots != null ? f.Slots.Count + " slots" : "every cell"));
        Action<Rectangle, PixelBuffer, string> armed = Armed;
        if (armed != null) armed(region, pb, key);
        Touched();
    }

    /// <summary>
    /// Called often while the game is in front. Watches where the mouse rests and prices what is under it.
    /// </summary>
    public void Tick(bool stashVisible)
    {
        if (!_settings.HoverPrices || Frame == null) return;
        if (!stashVisible || !_game.IsForeground()) { _lastSlot = Rectangle.Empty; return; }
        try
        {
            // A click can move or take items, or show another page: look at the slots again once it's done.
            if (_keys.MouseButtonHeld) { _clickHeld = true; _restSince = DateTime.MaxValue; return; }
            if (_clickHeld) { _clickHeld = false; _recheckAt = DateTime.Now.AddMilliseconds(350); }
            if (DateTime.Now >= _recheckAt) { _recheckAt = DateTime.MaxValue; Recheck(); }
            if (_recheckAt != DateTime.MaxValue || Frame == null) return;   // until then, which tab is on screen is not sure

            Point p = _input.GetCursorPos();
            if (!Frame.Region.Contains(p)) { _lastSlot = Rectangle.Empty; return; }
            if (_restSince == DateTime.MaxValue || Math.Abs(p.X - _restPos.X) > 3 || Math.Abs(p.Y - _restPos.Y) > 3)
            {
                _restPos = p;
                _restSince = DateTime.Now;
                return;
            }
            if ((DateTime.Now - _restSince).TotalMilliseconds < RestMs) return;
            Rectangle slot = SlotAt(p);
            if (slot.IsEmpty || slot == _lastSlot) return;
            // Keys the user holds would turn our Ctrl+C into something else.
            if (_keys.ModifiersHeld) return;

            _lastSlot = slot;
            HoverFrame f = Frame;
            string txt = Scanner.CopyItemUnderCursor(HoverCopyWait, _input, _clipboard);
            if (f == Frame) Apply(f, slot, txt);
        }
        catch (Exception ex) { Log.Write("hover prices: " + ex.Message); }
    }

    /// <summary>The slot under a screen point: a slot of the tab, or for a tab without a layout the stash cell there.</summary>
    private Rectangle SlotAt(Point p)
    {
        HoverFrame f = Frame;
        if (f.Slots != null)
        {
            foreach (Rectangle s in f.Slots) if (s.Contains(p)) return s;
            if (f.Exact) return Rectangle.Empty;   // a built-in layout has every slot: this is the panel between them
        }
        double cs = f.CellSize;
        int c = (int)((p.X - f.Region.X) / cs), r = (int)((p.Y - f.Region.Y) / cs);
        if (c < 0 || r < 0 || c >= 12 || r >= 12) return Rectangle.Empty;
        return new Rectangle(f.Region.X + (int)Math.Round(c * cs), f.Region.Y + (int)Math.Round(r * cs),
                             (int)Math.Round(cs), (int)Math.Round(cs));
    }

    private static Rectangle OnScreen(SavedItem s, Rectangle region)
    {
        return new Rectangle(region.X + (int)Math.Round(s.X * region.Width), region.Y + (int)Math.Round(s.Y * region.Height),
                             (int)Math.Round(s.W * region.Width), (int)Math.Round(s.H * region.Height));
    }

    /// <summary>Puts what was copied from a slot into the tab's result, replacing what was there before.</summary>
    private void Apply(HoverFrame f, Rectangle slot, string txt)
    {
        ParsedItem it = txt != null ? ItemParser.Parse(txt) : null;
        TabResult tr = _model.ResultFor(f.Key);
        if (tr == null)
        {
            if (it == null) return;
            tr = new TabResult { Key = f.Key };
            if (f.Key == StashModel.UnknownTab) _model.UnknownResult = tr;
            else _model.Remember(f.Key, tr);
        }
        Rectangle region = f.Region;
        Point mid = new Point(slot.X + slot.Width / 2, slot.Y + slot.Height / 2);
        // Whatever was read here before goes: the item may have been moved, used or replaced.
        int removed = tr.Items.RemoveAll(s =>
        {
            Rectangle b = OnScreen(s, region);
            return b.Contains(mid) || slot.Contains(new Point(b.X + b.Width / 2, b.Y + b.Height / 2));
        });
        if (it == null && removed == 0) return;

        if (it != null)
        {
            SavedItem si = new SavedItem
            {
                Text = txt,
                X = (slot.X - region.X) / (double)region.Width,
                Y = (slot.Y - region.Y) / (double)region.Height,
                W = slot.Width / (double)region.Width,
                H = slot.Height / (double)region.Height,
            };
            // A big item (armour, a unique) answers on every cell it covers: hovering another of its cells
            // grows it instead of adding it twice.
            SavedItem same = !f.Fixed && it.IsMultiCellCandidate
                ? tr.Items.FirstOrDefault(s => s.Text == txt
                    && OnScreen(s, region).IntersectsWith(Rectangle.Inflate(slot, (int)(f.CellSize * 2.5), (int)(f.CellSize * 2.5))))
                : null;
            if (same != null)
            {
                Rectangle u = Rectangle.Union(OnScreen(same, region), slot);
                same.X = (u.X - region.X) / (double)region.Width;
                same.Y = (u.Y - region.Y) / (double)region.Height;
                same.W = u.Width / (double)region.Width;
                same.H = u.Height / (double)region.Height;
            }
            else
            {
                if (it.NeedsCount)
                {
                    // No stack size in the text: read the number on the icon, as a scan does.
                    Rectangle local = slot;
                    local.Offset(-region.X, -region.Y);
                    int n = DigitReader.Read(f.Snapshot, local, f.CellSize);
                    if (n > 0) si.Count = n; else si.CountUnread = true;
                }
                tr.Items.Add(si);
            }
        }

        PriceTable t = _prices();
        tr.ScannedAt = DateTime.Now;
        tr.ValueAtScan = ResultStore.Total(tr, t);
        tr.PricesAtScan = t != null ? t.LoadedAt : DateTime.MinValue;
        if (f.Key != StashModel.UnknownTab) _model.Save();
        PriceInfo price = it != null && t != null ? t.Lookup(it) : null;
        Log.Write(it == null
            ? "hover prices: " + StashModel.NameOf(f.Key) + ": slot now empty"
            : string.Format("hover prices: {0}: {1} x{2} = {3}", StashModel.NameOf(f.Key), it.DisplayName, it.Stack,
                            price != null ? Money.Format(price.Div * it.Stack, t, _settings.DisplayCurrency) : "no price"));
        Touched();
    }

    /// <summary>
    /// After a click. Another tab opened: that one is made ready (the prices of the one before stay, its items
    /// are still in the stash). The same tab: items whose slot looks different now were moved, taken or are on
    /// another page; their prices go until they are hovered again.
    /// </summary>
    private void Recheck()
    {
        HoverFrame f = Frame;
        if (f == null) return;
        Rectangle win = _game.ClientRectOnScreen();
        if (win.Width <= 0 || win.Height <= 0) return;
        PixelBuffer full = _capture.Capture(win);
        StashLocator.Result loc = StashLocator.Locate(full);
        double d;
        TabProfile tab = TabLibrary.Identify(full.Crop(loc.Region), loc.StashVisible ? loc.FrameColor : null, _model.KnownTabs, out d);
        if (!loc.StashVisible && tab != null && d <= 0.15) loc.EdgesFound = 4;
        if (!loc.StashVisible) { _recheckTries = 0; return; }   // stash closed: the watcher takes it from here
        if (tab != null && tab.Key != f.TabKey)
        {
            _recheckTries = 0;
            Arm(win, full, loc, tab);
            return;
        }
        if (tab == null && f.TabKey != null)
        {
            // Not recognised: an item tooltip may hide part of the tab (or it is still fading in). Only another
            // frame colour says for sure that another tab is open; otherwise it's still this one, and its
            // slots can't be compared with a tooltip over them, so the prices stay.
            bool otherColour = loc.FrameColor != null && f.FrameColor != null
                               && StashLocator.ColorDistance(loc.FrameColor, f.FrameColor) > 0.15;
            if (!otherColour)
            {
                if (++_recheckTries < 4) _recheckAt = DateTime.Now.AddMilliseconds(300);
                else _recheckTries = 0;
                return;
            }
            _recheckTries = 0;
            Frame = null;
            _model.CurrentTab = null;
            Log.Write("hover prices: the tab on screen is not one the app knows");
            Say("This tab isn't one the app knows: press the scan key to get it ready for hover prices.");
            Touched();
            return;
        }
        _recheckTries = 0;
        Rectangle local = f.Region;
        local.Offset(-win.X, -win.Y);
        PixelBuffer now = full.Crop(local);
        TabResult tr = _model.ResultFor(f.Key);
        int dropped = 0;
        if (tr != null)
            dropped = tr.Items.RemoveAll(s =>
            {
                Rectangle b = OnScreen(s, f.Region);
                b.Offset(-f.Region.X, -f.Region.Y);
                return Scanner.MeanDifference(f.Snapshot, now, b) > 20;
            });
        f.Snapshot = now;
        _lastSlot = Rectangle.Empty;   // the slot under the mouse may have changed too
        if (dropped == 0) return;
        Log.Write("hover prices: " + dropped + " items changed after a click, their prices were removed");
        tr.ScannedAt = DateTime.Now;
        tr.ValueAtScan = ResultStore.Total(tr, _prices());
        if (f.Key != StashModel.UnknownTab) _model.Save();
        Touched();
    }
}
