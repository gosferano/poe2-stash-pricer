using System;
using Poe2StashPricer.Pricing;

namespace Poe2StashPricer.Session;

/// <summary>How much an item is worth, said the way the user asked for it.</summary>
public enum ValueTier
{
    /// <summary>Small change.</summary>
    Low,
    /// <summary>Worth noticing.</summary>
    Mid,
    /// <summary>A divine or more.</summary>
    High,
}

/// <summary>
/// Turns divine values into the text shown everywhere. Upstream kept this on the form; it is the same
/// arithmetic, with the colour replaced by a tier so the theme decides what that looks like.
/// </summary>
public static class Money
{
    public static string Num(double v)
    {
        if (v >= 1000) return v.ToString("#,0");
        if (v >= 100) return v.ToString("0");
        if (v >= 10) return v.ToString("0.#");
        if (v >= 1) return v.ToString("0.##");
        return v.ToString("0.###");
    }

    /// <param name="mode">auto | divine | exalted | chaos, as the setting spells it.</param>
    public static string Format(double div, PriceTable table, string mode)
    {
        double ex = table != null ? table.ExPerDiv : 0, ch = table != null ? table.ChaosPerDiv : 0;
        if (mode == null) mode = "auto";
        if (mode == "auto") mode = div >= 1 || ex <= 0 ? "divine" : "exalted";
        if (mode == "exalted" && ex > 0) return Num(div * ex) + " ex";
        if (mode == "chaos" && ch > 0) return Num(div * ch) + " c";
        return Num(div) + " div";
    }

    public static ValueTier Tier(double div, PriceTable table)
    {
        double ex = table != null && table.ExPerDiv > 0 ? div * table.ExPerDiv : div * 500;
        if (div >= 1) return ValueTier.High;
        return ex >= 10 ? ValueTier.Mid : ValueTier.Low;
    }
}
