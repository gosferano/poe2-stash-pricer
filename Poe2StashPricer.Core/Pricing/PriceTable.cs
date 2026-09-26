using System;
using System.Collections.Generic;

namespace Poe2StashPricer.Pricing;

public class PriceInfo
{
    public string Name;
    public string Category;
    public double Div;          // value of one unit in Divine Orbs
    public int Listings;
}

/// <summary>Immutable snapshot of poe.ninja prices for one league.</summary>
public class PriceTable
{
    public string League;
    public DateTime LoadedAt;
    public double ExPerDiv;
    public double ChaosPerDiv;
    public List<string> Failed = new List<string>();
    public bool RateLimited;            // poe.ninja answered 429/503: the rest was not asked
    public TimeSpan RetryAfter;         // from the Retry-After header, zero if none

    internal readonly Dictionary<string, PriceInfo> ByName = new Dictionary<string, PriceInfo>();
    internal readonly Dictionary<string, PriceInfo> UniqueByNameBase = new Dictionary<string, PriceInfo>();
    internal readonly Dictionary<string, PriceInfo> UniqueByName = new Dictionary<string, PriceInfo>();

    public int Count { get { return ByName.Count + UniqueByNameBase.Count; } }

    static string Key(string s) { return s.Trim().ToLowerInvariant(); }

    public PriceInfo Lookup(ParsedItem it)
    {
        if (it == null || it.Name == null) return null;
        string rarity = (it.Rarity ?? "").ToLowerInvariant();
        PriceInfo p;

        if (rarity == "unique")
        {
            if (it.Unidentified || it.BaseType == null) return null;
            if (UniqueByNameBase.TryGetValue(Key(it.Name + "|" + it.BaseType), out p)) return p;
            if (UniqueByName.TryGetValue(Key(it.Name), out p)) return p;
            return null;
        }
        if (rarity == "rare" || rarity == "magic") return null;

        string n = it.Name;
        if (n.StartsWith("Superior ")) n = n.Substring(9);
        if (it.Level > 0 && ByName.TryGetValue(Key(n + " (Level " + it.Level + ")"), out p)) return p;
        if (ByName.TryGetValue(Key(n), out p)) return p;
        return null;
    }

    /// <summary>
    /// For the categories that failed to load, keep the prices of the previous table, so a partial update
    /// doesn't make the stash value drop.
    /// </summary>
    public void FillFailedFrom(PriceTable old)
    {
        if (old == null || old.League != League || Failed.Count == 0) return;
        HashSet<string> failed = new HashSet<string>(Failed);
        foreach (KeyValuePair<string, PriceInfo> kv in old.ByName)
            if (failed.Contains(kv.Value.Category) && !ByName.ContainsKey(kv.Key)) ByName[kv.Key] = kv.Value;
        foreach (KeyValuePair<string, PriceInfo> kv in old.UniqueByNameBase)
            if (failed.Contains(kv.Value.Category) && !UniqueByNameBase.ContainsKey(kv.Key)) UniqueByNameBase[kv.Key] = kv.Value;
        foreach (KeyValuePair<string, PriceInfo> kv in old.UniqueByName)
            if (failed.Contains(kv.Value.Category) && !UniqueByName.ContainsKey(kv.Key)) UniqueByName[kv.Key] = kv.Value;
        if (ExPerDiv <= 0) ExPerDiv = old.ExPerDiv;
        if (ChaosPerDiv <= 0) ChaosPerDiv = old.ChaosPerDiv;
    }

    internal void AddExchange(string name, string category, double div)
    {
        string k = Key(name);
        if (!ByName.ContainsKey(k))
            ByName[k] = new PriceInfo { Name = name, Category = category, Div = div };
    }

    internal void AddUnique(string name, string baseType, string category, double div, int listings)
    {
        PriceInfo info = new PriceInfo { Name = name + " (" + baseType + ")", Category = category, Div = div, Listings = listings };
        Keep(UniqueByNameBase, Key(name + "|" + baseType), info);
        Keep(UniqueByName, Key(name), info);
    }

    // Variants (corrupted, different rolls...) share names; keep the most-listed one as the "typical" price.
    static void Keep(Dictionary<string, PriceInfo> d, string k, PriceInfo info)
    {
        PriceInfo old;
        if (!d.TryGetValue(k, out old) || info.Listings > old.Listings) d[k] = info;
    }
}
