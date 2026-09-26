using System;
using System.Collections.Generic;
using System.Linq;
using Poe2StashPricer.Detection;
using Poe2StashPricer.Pricing;
using Poe2StashPricer.Storage;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.Session;

/// <summary>
/// What the app knows: the tabs it has learned, the last scan of each, and which one the game is showing.
/// Nothing here looks at the screen or draws anything.
/// </summary>
public class StashModel
{
    /// <summary>A scanned tab that isn't one of the saved ones.</summary>
    public const string UnknownTab = "_unknown";

    private Dictionary<string, TabProfile> _profiles = TabLibrary.LoadAll();
    private Dictionary<string, TabResult> _results = ResultStore.Load();
    private TabResult _unknownResult;

    /// <summary>The tab on screen: a key, <see cref="UnknownTab"/>, or null when there is none.</summary>
    public string CurrentTab { get; set; }

    /// <summary>A tab the user picked in the list; null means the list follows the game.</summary>
    public string ViewKey { get; set; }

    public IReadOnlyDictionary<string, TabProfile> Profiles { get { return _profiles; } }

    public IReadOnlyDictionary<string, TabResult> Results { get { return _results; } }

    public TabResult UnknownResult
    {
        get { return _unknownResult; }
        set { _unknownResult = value; }
    }

    public ICollection<TabProfile> KnownTabs { get { return _profiles.Values; } }

    public static string NameOf(string key)
    {
        return key == UnknownTab ? "Unsaved tab" : TabLibrary.NameOf(key);
    }

    public TabResult ResultFor(string key)
    {
        if (key == null) return null;
        if (key == UnknownTab) return _unknownResult;
        TabResult tr;
        return _results.TryGetValue(key, out tr) ? tr : null;
    }

    public void Remember(string key, TabResult result)
    {
        if (key == UnknownTab) { _unknownResult = result; return; }
        _results[key] = result;
        Save();
    }

    public void Save()
    {
        ResultStore.Save(_results);
    }

    public void Add(TabProfile profile)
    {
        _profiles[profile.Key] = profile;
    }

    // ---- what the lists show ----

    /// <summary>
    /// Every tab worth listing: the ones learned here (each is learned on its first scan), any tab with a
    /// saved result, and the unsaved tab just scanned. A built-in layout is listed once it has been scanned.
    /// </summary>
    public List<TabRow> TabRows(PriceTable table)
    {
        List<string> keys = _profiles.Values.Where(p => !p.BuiltIn).Select(p => p.Key)
                                     .Union(_results.Keys)
                                     .OrderBy(k => NameOf(k))
                                     .ToList();
        if (_unknownResult != null) keys.Add(UnknownTab);

        List<TabRow> rows = new List<TabRow>();
        foreach (string key in keys)
        {
            TabResult r = ResultFor(key);
            rows.Add(new TabRow
            {
                Key = key,
                Name = NameOf(key),
                TotalDiv = r != null && table != null ? ResultStore.Total(r, table) : 0,
                Scanned = r != null,
                ScannedAt = r != null ? r.ScannedAt : DateTime.MinValue,
                OnScreen = key == CurrentTab,
                Unsaved = key == UnknownTab,
            });
        }
        return rows;
    }

    /// <summary>What the whole stash is worth. An unsaved tab is left out, as it is not really a tab.</summary>
    public double GrandTotal(PriceTable table)
    {
        double sum = 0;
        foreach (TabResult r in _results.Values) sum += ResultStore.Total(r, table);
        return sum;
    }

    public int ScannedTabs { get { return _results.Count; } }

    public DateTime OldestScan
    {
        get { return _results.Count == 0 ? DateTime.MinValue : _results.Values.Min(r => r.ScannedAt); }
    }

    // ---- tab management ----

    public bool CanRename(string key)
    {
        TabProfile p;
        return key != null && _profiles.TryGetValue(key, out p) && !p.BuiltIn;
    }

    public void Rename(string key, string name)
    {
        TabProfile p;
        if (key == null || !_profiles.TryGetValue(key, out p) || p.BuiltIn) return;
        TabLibrary.Rename(p, name.Trim());
    }

    /// <summary>Forgets one tab. A built-in layout stays; only its last scan goes.</summary>
    public void Delete(string key)
    {
        if (key == null || (!_profiles.ContainsKey(key) && !_results.ContainsKey(key))) return;
        bool builtIn = _profiles.ContainsKey(key) && _profiles[key].BuiltIn;
        Log.Write("tab deleted: " + key);
        if (!builtIn)
        {
            TabLibrary.Delete(key);
            _profiles.Remove(key);
        }
        if (_results.Remove(key)) Save();
        if (CurrentTab == key) CurrentTab = null;
    }

    /// <summary>Back to a fresh install: saved tabs, scan results and learned digits go.</summary>
    public void DeleteEverything()
    {
        TabLibrary.DeleteAll();
        Log.Write("everything deleted (tabs, results, digits)");
        ResultStore.DeleteAll();
        DigitReader.DeleteAll();
        _profiles = TabLibrary.LoadAll();   // the built-in layouts stay
        _results.Clear();
        _unknownResult = null;
        CurrentTab = null;
        ViewKey = null;
    }

    /// <summary>
    /// Tabs learned here that a built-in layout covers now: their last scan moves over to the built-in tab
    /// (the same panel, so item positions still fit) and the learned copy goes, so no tab is listed or
    /// counted twice. The last scan of a paged tab is dropped: it only ever showed one page.
    /// </summary>
    public void MoveLearnedTabsToLayouts()
    {
        List<TabProfile> layouts = _profiles.Values.Where(p => p.BuiltIn).ToList();
        bool moved = false;
        foreach (TabProfile p in _profiles.Values.Where(p => !p.BuiltIn).ToList())
        {
            var ranked = layouts.Select(l => new { l, d = TabLibrary.Distance(p.Signature, p.ItemMask, l.Signature, l.ItemMask) })
                                .OrderBy(x => x.d).ToList();
            if (ranked.Count == 0 || ranked[0].d > 0.15 || (ranked.Count > 1 && ranked[1].d - ranked[0].d < 0.05)) continue;
            TabProfile layout = ranked[0].l;
            TabResult tr;
            if (_results.TryGetValue(p.Key, out tr))
            {
                _results.Remove(p.Key);
                if (!layout.Paged && !_results.ContainsKey(layout.Key)) { tr.Key = layout.Key; _results[layout.Key] = tr; }
            }
            TabLibrary.Delete(p.Key);
            _profiles.Remove(p.Key);
            Log.Write(string.Format("learned tab '{0}' ({1}) is now the built-in '{2}' (difference {3:0.00})", p.Name, p.Key, layout.Name, ranked[0].d));
            moved = true;
        }
        if (moved) Save();
    }
}
