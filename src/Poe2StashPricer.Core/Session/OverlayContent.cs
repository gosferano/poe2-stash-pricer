using System;
using System.Collections.Generic;
using System.Drawing;
using Poe2StashPricer.Pricing;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.Session;

/// <summary>One thing drawn over the game: a price on an item, or an outline in a preview.</summary>
public class OverlayLabel
{
    public Rectangle Bounds;      // screen coordinates
    public string Text;
    public ValueTier Tier;
    public bool Outline;          // draw the rectangle rather than write in it (preview)
}

/// <summary>Why the overlay is showing what it is; a message and a preview stay until something replaces them.</summary>
public enum OverlayKind
{
    Hidden,
    Prices,
    Preview,
    Message,
}

/// <summary>
/// What the overlay should show. Deciding this is Core's business; putting pixels on the screen is not.
/// </summary>
public class OverlayContent
{
    public OverlayKind Kind = OverlayKind.Hidden;
    public List<OverlayLabel> Labels = new List<OverlayLabel>();
    public string Header;
    public string Note;           // the "prices updated" line, or null
    public Rectangle Region;      // the stash area on screen

    public static readonly OverlayContent Hidden = new OverlayContent();

    /// <summary>
    /// The saved prices of the tab on screen. Null when there is nothing to show, which the caller turns
    /// into hiding the overlay.
    /// </summary>
    public static OverlayContent ForTab(StashModel model, PriceTable table, Rectangle region, string currencyMode,
                                        bool hoverReady, string scanKeyName, string overlayKeyName)
    {
        TabResult tr = model.ResultFor(model.CurrentTab);
        if (tr == null && !hoverReady) return null;

        List<PricedItem> items = ResultStore.Price(tr, table, region);
        OverlayContent content = new OverlayContent { Kind = OverlayKind.Prices, Region = region };
        double sum = 0;
        foreach (PricedItem pi in items)
        {
            sum += pi.TotalDiv;
            if (pi.Price == null) continue;
            content.Labels.Add(new OverlayLabel
            {
                Bounds = pi.Bounds,
                Text = Money.Format(pi.TotalDiv, table, currencyMode),
                Tier = Money.Tier(pi.TotalDiv, table),
            });
        }
        double grand = model.GrandTotal(table);
        string name = StashModel.NameOf(model.CurrentTab);
        content.Header = hoverReady
            ? string.Format("{0}: {1}  ·  Stash total: {2}  ·  rest the mouse on an item to price it  ·  {3}: hide",
                            name, Money.Format(sum, table, currencyMode), Money.Format(grand, table, currencyMode), overlayKeyName)
            : string.Format("{0}: {1}  ·  scanned {2:HH:mm}  ·  Stash total: {3}  ·  {4}: rescan · {5}: hide",
                            name, Money.Format(sum, table, currencyMode), tr.ScannedAt,
                            Money.Format(grand, table, currencyMode), scanKeyName, overlayKeyName);
        if (!hoverReady) content.Note = PriceChangeNote(tr, sum, table, currencyMode, scanKeyName);
        return content;
    }

    /// <summary>A line of text over the stash, for something that went wrong.</summary>
    public static OverlayContent ForMessage(string text, Rectangle region)
    {
        return new OverlayContent { Kind = OverlayKind.Message, Header = text, Region = region };
    }

    /// <summary>
    /// "Prices updated" when poe.ninja prices changed the tab value since it was scanned. The values shown
    /// already use the new prices; a rescan also picks up items that were moved or used.
    /// </summary>
    public static string PriceChangeNote(TabResult tr, double now, PriceTable table, string currencyMode, string scanKeyName)
    {
        if (table == null || tr == null) return null;
        if (tr.PricesAtScan == DateTime.MinValue)   // scanned by an older version: no value to compare
            return table.LoadedAt > tr.ScannedAt
                ? string.Format("↻ Prices updated at {0:HH:mm} since this scan  ·  {1}: rescan", table.LoadedAt, scanKeyName)
                : null;
        if (table.LoadedAt <= tr.PricesAtScan || tr.ValueAtScan <= 0) return null;
        double change = (now - tr.ValueAtScan) / tr.ValueAtScan;
        if (Math.Abs(change) < 0.01) return null;
        return string.Format("↻ Prices updated at {0:HH:mm}: tab value {1} → {2} ({3}{4:0.#}%)  ·  {5}: rescan",
                             table.LoadedAt, Money.Format(tr.ValueAtScan, table, currencyMode),
                             Money.Format(now, table, currencyMode), change > 0 ? "+" : "−", Math.Abs(change) * 100, scanKeyName);
    }
}
