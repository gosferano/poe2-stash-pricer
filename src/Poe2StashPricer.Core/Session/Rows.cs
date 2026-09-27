using System;
using System.Collections.Generic;
using System.Drawing;
using Poe2StashPricer.Pricing;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.Session;

/// <summary>One line of the tab list.</summary>
public class TabRow
{
    public string Key;
    public string Name;
    public double TotalDiv;
    public bool Scanned;
    public DateTime ScannedAt;
    public bool OnScreen;      // the tab the game is showing right now
    public bool Unsaved;       // scanned but not one of the saved tabs
}

/// <summary>
/// One line of the item list: every read of the same item in a tab added up, which is what the user wants
/// to see (a scan can read one stack from several of its cells).
/// </summary>
public class ItemRow
{
    public string Name;
    public string Category;
    public int Qty;
    public double UnitDiv;
    public double TotalDiv;
    public bool Priced;
    public bool CountUnread;   // the count had to be read off the icon and could not be
}

/// <summary>
/// Shapes what the lists show. This is the part of the old RefreshTabList / RefreshItems that decides
/// *what* is in a row; putting it in a control is the caller's business.
/// </summary>
public static class Rows
{
    /// <summary>Items of one tab, grouped by name and ordered the way the list shows them.</summary>
    public static List<ItemRow> Items(TabResult result, PriceTable table)
    {
        List<PricedItem> priced = ResultStore.Price(result, table, Rectangle.Empty);
        Dictionary<string, ItemRow> byName = new Dictionary<string, ItemRow>();
        List<ItemRow> order = new List<ItemRow>();
        foreach (PricedItem pi in priced)
        {
            string name = pi.Price != null ? pi.Price.Name : pi.Item.DisplayName;
            ItemRow row;
            if (!byName.TryGetValue(name, out row))
            {
                row = new ItemRow
                {
                    Name = name,
                    Priced = pi.Price != null,
                    UnitDiv = pi.Price != null ? pi.Price.Div : 0,
                    Category = pi.Price != null ? pi.Price.Category : (pi.Item.Rarity ?? pi.Item.ItemClass ?? ""),
                };
                byName[name] = row;
                order.Add(row);
            }
            row.Qty += pi.Qty;
            row.TotalDiv += pi.TotalDiv;
            row.CountUnread |= pi.CountUnread;
        }
        // Priced items first, then by what they are worth: the valuable things belong at the top.
        order.Sort((a, b) =>
        {
            if (a.Priced != b.Priced) return b.Priced.CompareTo(a.Priced);
            int byTotal = b.TotalDiv.CompareTo(a.TotalDiv);
            return byTotal != 0 ? byTotal : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
        });
        return order;
    }

    /// <summary>What one tab's items add up to.</summary>
    public static double Total(List<ItemRow> rows)
    {
        double sum = 0;
        foreach (ItemRow r in rows) sum += r.TotalDiv;
        return sum;
    }
}
