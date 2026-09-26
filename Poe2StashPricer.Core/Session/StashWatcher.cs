using System;
using System.Collections.Generic;
using System.Drawing;
using Poe2StashPricer.Detection;
using Poe2StashPricer.Platform;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.Session;

/// <summary>What one look at the game found.</summary>
public class StashSighting
{
    /// <summary>Nothing certain enough to act on: probably just an item tooltip over the stash.</summary>
    public bool Ignore;
    public bool StashVisible;
    public Rectangle WindowRect;     // the game's client area, screen coordinates
    public Rectangle Region;         // the stash area, screen coordinates
    public PixelBuffer Full;         // the whole window as captured
    public StashLocator.Result Loc;  // in window coordinates
    public TabProfile Tab;           // the tab recognised, or null
}

/// <summary>
/// Keeps track of which tab is open. When the stash picture changes (and the cursor isn't over it, where
/// item tooltips come and go) the stash is located and recognised again. Results are never thrown away;
/// this only says what the game is showing.
/// </summary>
public class StashWatcher
{
    private readonly IScreenCapture _capture;
    private readonly IGameWindow _game;
    private readonly IInput _input;
    private readonly TabWatcher _watcher;

    private DateTime _nextDetect = DateTime.MinValue;
    private DateTime _settleUntil = DateTime.MinValue;   // after a change, keep looking every tick until then

    public StashWatcher(IScreenCapture capture, IGameWindow game, IInput input)
    {
        _capture = capture;
        _game = game;
        _input = input;
        _watcher = new TabWatcher(capture);
    }

    /// <summary>The stash area on screen, empty until it has been found once.</summary>
    public Rectangle Region { get; private set; }

    public bool StashVisible { get; private set; }

    /// <summary>Look again on the next tick, whatever the timers say.</summary>
    public void LookSoon(int settleMs = 1500)
    {
        _nextDetect = DateTime.MinValue;
        _settleUntil = DateTime.Now.AddMilliseconds(settleMs);
    }

    public void Forget()
    {
        _watcher.Clear();
        Region = Rectangle.Empty;
        StashVisible = false;
    }

    /// <summary>
    /// Whether this tick is worth a look, and whether the cursor is over the stash (where item tooltips
    /// change the picture constantly).
    /// </summary>
    public bool DueForLook(out bool cursorOver)
    {
        cursorOver = false;
        bool due = DateTime.Now >= _nextDetect;
        if (Region.IsEmpty) return due;

        Rectangle guard = Region;
        guard.Inflate(8, 8);
        cursorOver = guard.Contains(_input.GetCursorPos());
        if (!StashVisible)
        {
            // Stash closed: the game world moves all the time, so only look now and then.
            return due;
        }
        if (DateTime.Now > _settleUntil)
        {
            // Look again when the picture changed. With the cursor over the stash, item tooltips change it
            // constantly, so then only at the slower pace.
            if (!_watcher.Active || !_watcher.HasChanged()) return false;
            if (cursorOver && !due) return false;
            _settleUntil = DateTime.Now.AddMilliseconds(1500);
        }
        // Within settleUntil: keep looking every tick, so a tab that is still fading in (or was captured
        // before the game redrew it) is caught once it is fully shown.
        return true;
    }

    /// <param name="cursorOver">
    /// The cursor is over the stash, so an item tooltip may hide part of it: only clear evidence counts
    /// (another saved tab recognised, a different frame colour, or the stash gone).
    /// </param>
    public StashSighting Look(bool cursorOver, StashModel model)
    {
        Rectangle win = _game.ClientRectOnScreen();
        if (win.Width <= 0 || win.Height <= 0) return null;

        PixelBuffer full = _capture.Capture(win);
        StashLocator.Result loc = StashLocator.Locate(full);
        TabProfile tab = null;
        if (loc.StashVisible || model.Profiles.Count > 0)
        {
            double diff;
            tab = TabLibrary.Identify(full.Crop(loc.Region), loc.StashVisible ? loc.FrameColor : null, model.KnownTabs, out diff);
            if (!loc.StashVisible && tab != null && diff > 0.15) tab = null;   // without a frame, only a sure match counts
            if (tab != null) loc.EdgesFound = 4;   // a saved tab is on screen even if its frame was faint
        }

        StashSighting seen = new StashSighting { WindowRect = win, Full = full, Loc = loc, Tab = tab, StashVisible = loc.StashVisible };

        if (cursorOver && StashVisible)
        {
            TabProfile shown = model.CurrentTab != null && model.Profiles.ContainsKey(model.CurrentTab)
                ? model.Profiles[model.CurrentTab] : null;
            bool otherTab = tab != null && tab.Key != model.CurrentTab;
            bool otherColour = tab == null && loc.FrameColor != null && shown != null && shown.FrameColor != null
                               && StashLocator.ColorDistance(loc.FrameColor, shown.FrameColor) > 0.15;
            bool closed = loc.EdgesFound <= 1;   // a tooltip hides one or two edges at most
            if (!otherTab && !otherColour && !closed)
            {
                _nextDetect = DateTime.Now.AddMilliseconds(1000);
                seen.Ignore = true;
                return seen;
            }
        }

        StashVisible = loc.StashVisible;
        // While the stash is closed the game world moves all the time: look again only now and then.
        _nextDetect = DateTime.Now.AddMilliseconds(StashVisible ? 500 : 1500);
        if (StashVisible)
        {
            Region = new Rectangle(win.X + loc.Region.X, win.Y + loc.Region.Y, loc.Region.Width, loc.Region.Height);
            _watcher.SetBaseline(full.Crop(loc.Region), Region, 12, 12);
        }
        else if (!Region.IsEmpty)
        {
            Rectangle local = Region;
            local.Offset(-win.X, -win.Y);
            _watcher.SetBaseline(full.Crop(local), Region, 12, 12);
        }
        seen.Region = Region;
        return seen;
    }

    /// <summary>Remembers where the stash is after a scan, preview or save, so the watching can follow it.</summary>
    public void Note(Rectangle region, PixelBuffer snapshot)
    {
        Region = region;
        StashVisible = true;
        if (snapshot != null) _watcher.SetBaseline(snapshot, region, 12, 12);
    }
}
