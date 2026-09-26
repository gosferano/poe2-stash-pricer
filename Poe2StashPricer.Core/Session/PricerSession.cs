using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Poe2StashPricer.Detection;
using Poe2StashPricer.Platform;
using Poe2StashPricer.Pricing;
using Poe2StashPricer.Storage;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.Session;

/// <summary>
/// Everything the app does, with no window anywhere in sight: it follows the game, keeps prices current,
/// scans when asked and says what the overlay and the lists should show.
///
/// It runs its own thread. A capture takes about 25 ms, which is far too long to do on a thread that is
/// also drawing, and it means the session works with no user interface at all - which is how it is tested.
/// Everything raised from here arrives on that thread, so a caller with a user interface has to hand the
/// events over to whichever thread may touch its windows.
/// </summary>
public class PricerSession : IDisposable
{
    private const string NoStash = "Stash not visible. Open the stash, keep the mouse off it and try again.";
    private const string NotInFront = "Bring Path of Exile 2 to the front first: the app reads what the game shows.";
    private const string NoGame = "Path of Exile 2 was not found. Start the game, then try again.";

    private readonly AppSettings _settings;
    private readonly IScreenCapture _capture;
    private readonly IGameWindow _game;
    private readonly IInput _input;
    private readonly IKeyState _keys;
    private readonly IClipboard _clipboard;

    private readonly StashWatcher _watcher;
    private readonly HoverPricer _hover;
    private readonly ScanRunner _scans;

    private Thread _loop;
    private volatile bool _stop;
    private volatile bool _busy;
    private bool _overlayWanted = true;
    private string _overlayState = "";
    private OverlayKind _overlayKind = OverlayKind.Hidden;

    public PricerSession(AppSettings settings, IScreenCapture capture, IGameWindow game, IInput input,
                         IKeyState keys, IClipboard clipboard)
    {
        _settings = settings;
        _capture = capture;
        _game = game;
        _input = input;
        _keys = keys;
        _clipboard = clipboard;

        Model = new StashModel();
        Model.MoveLearnedTabsToLayouts();
        Prices = new PriceFeed(settings);
        _watcher = new StashWatcher(capture, game, input);
        _hover = new HoverPricer(capture, game, input, keys, clipboard, Model, () => Prices.Table, settings);
        _scans = new ScanRunner(capture, input, keys, clipboard, game, Model, settings);

        Prices.Status += Say;
        Prices.Changed += ViewChangedNow;
        _hover.Status += Say;
        _hover.Changed += ViewChangedNow;
        _hover.Armed += (region, snapshot, key) => NoteStash(region, snapshot, key);
    }

    public StashModel Model { get; }

    public PriceFeed Prices { get; }

    /// <summary>A scan is running; most things wait for it.</summary>
    public bool Busy { get { return _busy; } }

    /// <summary>What to put in the status line.</summary>
    public event Action<string> StatusChanged;

    /// <summary>The tabs or their items changed: whatever shows them is out of date.</summary>
    public event Action ViewChanged;

    /// <summary>What the overlay should show now.</summary>
    public event Action<OverlayContent> OverlayChanged;

    public event Action<int, int> ScanProgress;

    public event Action<bool> BusyChanged;

    // The names the messages use for the keys. The app binds them outside, so it says what they are.
    public string ScanKeyName { get; set; } = "the scan key";
    public string OverlayKeyName { get; set; } = "the overlay key";

    private void Say(string text)
    {
        Action<string> handler = StatusChanged;
        if (handler != null) handler(text);
    }

    private void ViewChangedNow()
    {
        Action handler = ViewChanged;
        if (handler != null) handler();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Action<bool> handler = BusyChanged;
        if (handler != null) handler(busy);
    }

    public void Start()
    {
        if (_loop != null) return;
        _loop = new Thread(Loop) { IsBackground = true, Name = "pricer" };
        _loop.Start();
        Task.Run(() => Prices.LoadLeaguesAsync());
    }

    // ---------------------------------------------------------------- the loop

    private void Loop()
    {
        DateTime nextWatch = DateTime.MinValue;
        while (!_stop)
        {
            try
            {
                Thread.Sleep(40);
                if (DateTime.Now >= nextWatch)
                {
                    nextWatch = DateTime.Now.AddMilliseconds(500);
                    WatchOnce();
                }
                if (!_busy) _hover.Tick(_watcher.StashVisible);
            }
            catch (Exception ex) { Log.Write("session loop: " + ex.Message); }
        }
    }

    private void WatchOnce()
    {
        Prices.TickAsync(_busy).GetAwaiter().GetResult();
        if (_busy) return;
        UpdateOverlay(false);   // e.g. the game lost or regained focus

        if (!_game.Find() || !_game.IsForeground()) return;
        if (Model.Profiles.Count == 0 && Model.Results.Count == 0) return;

        bool cursorOver;
        if (!_watcher.DueForLook(out cursorOver)) return;
        Apply(_watcher.Look(cursorOver, Model));
    }

    /// <summary>What upstream's Detect did once it had looked: decide which tab is current.</summary>
    private void Apply(StashSighting seen)
    {
        if (seen == null) return;
        if (seen.Ignore) { UpdateOverlay(false); return; }

        // Price on hover: a tab the app knows is made ready as soon as it is on screen.
        if (_watcher.StashVisible && _hover.NeedsArming(seen.Tab, _watcher.Region))
            _hover.Arm(seen.WindowRect, seen.Full, seen.Loc, seen.Tab);

        // A paged tab (Tablets...) shows one of several pages: like an unknown tab, its last scan is not kept.
        string key = _watcher.StashVisible && seen.Tab != null && !seen.Tab.Paged ? seen.Tab.Key : null;
        if (_hover.FrameOnScreen(seen.Tab, seen.Loc, _watcher.Region, _watcher.StashVisible))
            key = _hover.Frame.Key;   // hover prices of an unsaved or paged tab stay shown

        bool changed = key != Model.CurrentTab;
        if (changed)
        {
            Model.CurrentTab = key;
            Model.ViewKey = null;   // the item list follows the game again
            ViewChangedNow();
        }
        UpdateOverlay(changed);   // another tab replaces a preview or message
    }

    // ---------------------------------------------------------------- the overlay

    /// <summary>Shows the saved prices of the tab on screen, or hides the overlay when there are none.</summary>
    private void UpdateOverlay(bool force)
    {
        // Messages and previews stay until the tab changes or the overlay key.
        if (!force && (_overlayKind == OverlayKind.Message || _overlayKind == OverlayKind.Preview)) return;

        bool gameFront = _game.IsForeground();
        TabResult tr = Model.ResultFor(Model.CurrentTab);
        bool hoverHere = _settings.HoverPrices && _hover.Frame != null && _hover.Frame.Key == Model.CurrentTab;
        bool show = _overlayWanted && gameFront && _watcher.StashVisible && (tr != null || hoverHere) && !_watcher.Region.IsEmpty;
        PriceTable table = Prices.Table;
        string state = show
            ? string.Format("tab:{0}:{1:O}:{2}:{3}:{4}:{5}", Model.CurrentTab, tr == null ? DateTime.MinValue : tr.ScannedAt,
                            _watcher.Region, table == null ? 0 : table.LoadedAt.Ticks, _settings.DisplayCurrency, hoverHere)
            : "hidden";
        if (!force && state == _overlayState) return;
        _overlayState = state;
        if (!show) { Show(OverlayContent.Hidden); return; }

        OverlayContent content = OverlayContent.ForTab(Model, table, _watcher.Region, _settings.DisplayCurrency,
                                                       hoverHere, ScanKeyName, OverlayKeyName);
        Show(content ?? OverlayContent.Hidden);
    }

    private void Show(OverlayContent content)
    {
        _overlayKind = content.Kind;
        Action<OverlayContent> handler = OverlayChanged;
        if (handler != null) handler(content);
    }

    /// <summary>Something the user asked for from the game didn't work: tell them where they are looking.</summary>
    private void Problem(string text, Rectangle window)
    {
        Log.Write("problem: " + text);
        Say(text);
        if (!window.IsEmpty)
        {
            Rectangle where = StashLocator.Predict(window.Size);
            where.Offset(window.Location);
            _overlayState = "message";
            Show(OverlayContent.ForMessage(text, where));
        }
    }

    public void ToggleOverlay()
    {
        // A message or preview on screen: the overlay key just dismisses it.
        if (_overlayKind == OverlayKind.Message || _overlayKind == OverlayKind.Preview)
        {
            _overlayState = "hidden";
            Show(OverlayContent.Hidden);
            return;
        }
        _overlayWanted = !_overlayWanted;
        UpdateOverlay(true);
        Say(_overlayWanted ? "Price overlay on." : "Price overlay off (" + OverlayKeyName + " to turn on).");
    }

    /// <summary>Remembers where the stash is after a scan, preview or arming, so the watching can follow it.</summary>
    private void NoteStash(Rectangle region, PixelBuffer snapshot, string key)
    {
        _watcher.Note(region, snapshot);
        if (key != Model.CurrentTab)
        {
            Model.CurrentTab = key;
            Model.ViewKey = null;
        }
        ViewChangedNow();
    }

    // ---------------------------------------------------------------- what the user asks for

    public void SetHoverPricing(bool on)
    {
        _settings.HoverPrices = on;
        _settings.Save();
        _hover.Clear();
        Say(on
            ? "Price on hover: open a stash tab (press " + ScanKeyName + " on a tab the app doesn't know), then rest the mouse on an item."
            : "Price on hover off: " + ScanKeyName + " scans the whole tab.");
        _watcher.LookSoon();
        UpdateOverlay(true);
    }

    public void SetDisplayCurrency(string mode)
    {
        _settings.DisplayCurrency = mode;
        _settings.Save();
        ViewChangedNow();
        UpdateOverlay(true);
    }

    /// <summary>The scan key. During a scan it stops it; with hover pricing on it gets the tab ready instead.</summary>
    public void Scan()
    {
        if (_busy) { _scans.Cancel(); return; }
        if (!Ready()) return;
        if (_settings.HoverPrices) { Task.Run(ArmFromGame); return; }

        PriceTable table = Prices.Table;
        if (table == null) { Say("Prices are not loaded yet, please wait a moment."); return; }
        if (Prices.IsStale) Task.Run(() => Prices.LoadPricesAsync(false));

        SetBusy(true);
        Task.Run(() =>
        {
            try
            {
                _overlayState = "hidden";
                Show(OverlayContent.Hidden);
                Log.Write("scan asked for | foreground: " + Description());
                ScanOutcome outcome = _scans.Run(table, (done, total) =>
                {
                    Action<int, int> handler = ScanProgress;
                    if (handler != null) handler(done, total);
                });
                if (outcome.BlackScreen)
                {
                    Problem("The game picture came back black, so nothing could be read from it.", outcome.Config.Window);
                    return;
                }
                if (outcome.StashNotFound) { Problem(NoStash, outcome.Config.Window); return; }
                NoteStash(outcome.Config.Region, outcome.Result.Snapshot, outcome.Key);
                ViewChangedNow();
                UpdateOverlay(true);
                if (outcome.Status != null) Say(outcome.Status);
            }
            catch (Exception ex)
            {
                Log.Write("scan error: " + ex);
                Problem("Scan error: " + ex.Message, Rectangle.Empty);
            }
            finally { SetBusy(false); }
        });
    }

    public void Preview()
    {
        if (_busy || !Ready()) return;
        SetBusy(true);
        Task.Run(() =>
        {
            try
            {
                _overlayState = "hidden";
                Show(OverlayContent.Hidden);
                PreviewOutcome outcome = _scans.Preview();
                if (outcome.BlackScreen)
                {
                    Problem("The game picture came back black, so nothing could be read from it.", outcome.Config.Window);
                    return;
                }
                if (outcome.StashNotFound) { Problem(NoStash, outcome.Config.Window); return; }

                NoteStash(outcome.Config.Region, outcome.Plan.Snapshot,
                          outcome.Plan.Tab != null ? outcome.Plan.Tab.Key : null);
                OverlayContent content = new OverlayContent { Kind = OverlayKind.Preview, Region = outcome.Config.Region };
                foreach (Rectangle probe in outcome.Probes)
                    content.Labels.Add(new OverlayLabel { Bounds = probe, Outline = true });
                content.Labels.Add(new OverlayLabel { Bounds = outcome.Config.Region, Outline = true });
                content.Header = string.Format("Preview · {0} · {1} item positions will be scanned.  {2}: hide",
                                               outcome.TabName, outcome.Probes.Count, OverlayKeyName);
                _overlayState = "preview";
                Show(content);
                Say(outcome.Status);
            }
            catch (Exception ex)
            {
                Log.Write("preview error: " + ex);
                Problem("Preview error: " + ex.Message, Rectangle.Empty);
            }
            finally { SetBusy(false); }
        });
    }

    /// <summary>The scan key with price on hover on: make the tab on screen ready, whatever it is.</summary>
    private void ArmFromGame()
    {
        try
        {
            Log.Write("scan key pressed (price on hover) | foreground: " + Description());
            Rectangle win = _game.ClientRectOnScreen();
            if (win.Width <= 0 || win.Height <= 0) { Say(NoGame); return; }
            PixelBuffer full = _capture.Capture(win);
            StashLocator.Result loc = StashLocator.Locate(full);
            double d;
            TabProfile tab = TabLibrary.Identify(full.Crop(loc.Region), loc.StashVisible ? loc.FrameColor : null, Model.KnownTabs, out d);
            if (!loc.StashVisible && tab != null && d <= 0.15) loc.EdgesFound = 4;   // a known tab whose frame was faint
            if (!loc.StashVisible) { Problem(NoStash, win); return; }
            _hover.Arm(win, full, loc, tab);
            Say(string.Format("{0} is ready: rest the mouse on an item to price it.", StashModel.NameOf(_hover.Frame.Key)));
            UpdateOverlay(true);
        }
        catch (Exception ex)
        {
            Log.Write("getting the tab ready failed: " + ex.Message);
            Say("Could not read the game: " + ex.Message);
        }
    }

    /// <summary>
    /// The game has to be in front: unlike on Windows there is no way to bring it there ourselves, and a
    /// scan reads what the game shows and types into it.
    /// </summary>
    private bool Ready()
    {
        if (!_game.Find()) { Say(NoGame); return false; }
        if (!_game.IsForeground()) { Say(NotInFront + " (" + Description() + " has it)"); return false; }
        return true;
    }

    private string Description()
    {
        return _game.IsForeground() ? "the game" : "another window";
    }

    // ---- tabs ----

    public void RenameTab(string key, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Model.Rename(key, name);
        ViewChangedNow();
    }

    public void DeleteTab(string key)
    {
        Model.Delete(key);
        ViewChangedNow();
        UpdateOverlay(true);
    }

    public void DeleteEverything()
    {
        if (_busy) return;
        try { Model.DeleteEverything(); }
        catch (Exception ex) { Say("Could not delete everything: " + ex.Message); return; }
        _hover.Clear();
        _watcher.Forget();
        _overlayState = "hidden";
        Show(OverlayContent.Hidden);
        ViewChangedNow();
        Say("Everything deleted. Tabs are learned again on their first scan (" + ScanKeyName + ").");
    }

    public void Dispose()
    {
        _stop = true;
        _scans.Cancel();
        if (_loop != null) _loop.Join(1000);
        _settings.Save();
    }
}
