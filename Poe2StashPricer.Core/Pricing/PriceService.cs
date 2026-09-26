using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.Pricing;

public static class PriceService
{
    const string Base = "https://poe.ninja/poe2/api/economy/";
    const string UserAgent = "Poe2StashPricer/0.1.0 (Linux desktop stash pricing tool)";

    static readonly string[] ExchangeTypes =
    {
        "Currency", "Fragments", "Abyss", "UncutGems", "LineageSupportGems", "Essences", "SoulCores",
        "Idols", "Runes", "Ritual", "Expedition", "Delirium", "Breach", "Verisium"
    };

    static readonly string[] StashTypes =
    {
        "UniqueWeapons", "UniqueArmours", "UniqueAccessories", "UniqueFlasks", "UniqueCharms",
        "UniqueJewels", "UniqueSanctumRelics", "UniqueTablets", "PrecursorTablets"
    };

    static readonly HttpClient http = NewClient();

    static HttpClient NewClient()
    {
        HttpClientHandler handler = new HttpClientHandler();
        handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        HttpClient c = new HttpClient(handler);
        c.Timeout = TimeSpan.FromSeconds(20);
        c.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        c.DefaultRequestHeaders.Add("Accept", "application/json");
        return c;
    }

    /// <param name="t">The table being filled, so a "slow down" answer can be remembered; may be null.</param>
    static string Get(string url, PriceTable t)
    {
        using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url))
        using (HttpResponseMessage resp = http.Send(req, HttpCompletionOption.ResponseHeadersRead))
        {
            if (!resp.IsSuccessStatusCode)
            {
                if (t != null) Limited(resp, t);
                throw new HttpRequestException("poe.ninja answered " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
            }
            using (Stream s = resp.Content.ReadAsStream())
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
            {
                // The biggest poe.ninja answer is a few MB; refuse anything absurd instead of filling the memory.
                char[] buf = new char[64 * 1024];
                StringBuilder sb = new StringBuilder();
                int n;
                while ((n = r.Read(buf, 0, buf.Length)) > 0)
                {
                    sb.Append(buf, 0, n);
                    if (sb.Length > MaxResponseChars) throw new InvalidDataException("poe.ninja answer too large");
                }
                return sb.ToString();
            }
        }
    }

    const int MaxResponseChars = 64 * 1024 * 1024;

    static string Q(string s) { return Uri.EscapeDataString(s); }

    static double Num(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Number) return e.GetDouble();
        if (e.ValueKind == JsonValueKind.String)
        {
            double d;
            return double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }
        return 0;
    }

    static string Str(JsonElement d, string k)
    {
        JsonElement v;
        if (d.ValueKind != JsonValueKind.Object || !d.TryGetProperty(k, out v)) return null;
        if (v.ValueKind == JsonValueKind.Null || v.ValueKind == JsonValueKind.Undefined) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
    }

    static IEnumerable<JsonElement> List(JsonElement d, string k)
    {
        JsonElement v;
        if (d.ValueKind != JsonValueKind.Object || !d.TryGetProperty(k, out v) || v.ValueKind != JsonValueKind.Array) yield break;
        foreach (JsonElement x in v.EnumerateArray())
            if (x.ValueKind == JsonValueKind.Object) yield return x;
    }

    public static List<string> GetLeagues()
    {
        List<string> res = new List<string>();
        using (JsonDocument doc = JsonDocument.Parse(Get(Base + "leagues", null)))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return res;
            foreach (JsonElement d in doc.RootElement.EnumerateArray())
            {
                if (d.ValueKind != JsonValueKind.Object) continue;
                string id = Str(d, "id") ?? Str(d, "name");
                if (id != null) res.Add(id);
            }
        }
        return res;
    }

    public static PriceTable Load(string league, Action<string> progress)
    {
        PriceTable t = new PriceTable();
        t.League = league;
        int step = 0, total = ExchangeTypes.Length + StashTypes.Length;

        foreach (string type in ExchangeTypes)
        {
            step++;
            if (t.RateLimited) { t.Failed.Add(type); continue; }   // don't keep asking a server that said "slow down"
            if (progress != null) progress("Loading prices (" + step + "/" + total + "): " + type);
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(Get(Base + "exchange/current/overview?league=" + Q(league) + "&type=" + type, t)))
                {
                    JsonElement root = doc.RootElement;
                    ReadRates(t, root);
                    Dictionary<string, string> names = new Dictionary<string, string>();
                    foreach (JsonElement item in List(root, "items"))
                    {
                        string id = Str(item, "id"), name = Str(item, "name");
                        if (id != null && name != null) names[id] = name;
                    }
                    foreach (JsonElement line in List(root, "lines"))
                    {
                        string id = Str(line, "id"), name;
                        JsonElement pv;
                        if (id == null || !names.TryGetValue(id, out name) || !line.TryGetProperty("primaryValue", out pv)) continue;
                        t.AddExchange(name, type, Num(pv));
                    }
                }
            }
            catch (Exception ex) { t.Failed.Add(type); Log.Write("poe.ninja " + type + " not loaded: " + ex.Message); }
        }

        foreach (string type in StashTypes)
        {
            step++;
            if (t.RateLimited) { t.Failed.Add(type); continue; }   // don't keep asking a server that said "slow down"
            if (progress != null) progress("Loading prices (" + step + "/" + total + "): " + type);
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(Get(Base + "stash/current/item/overview?league=" + Q(league) + "&type=" + type, t)))
                {
                    JsonElement root = doc.RootElement;
                    ReadRates(t, root);
                    foreach (JsonElement line in List(root, "lines"))
                    {
                        string name = Str(line, "name"), baseType = Str(line, "baseType");
                        JsonElement pv, lc;
                        if (name == null || !line.TryGetProperty("primaryValue", out pv)) continue;
                        line.TryGetProperty("listingCount", out lc);
                        t.AddUnique(name, baseType ?? "", type, Num(pv), (int)Num(lc));
                    }
                }
            }
            catch (Exception ex) { t.Failed.Add(type); Log.Write("poe.ninja " + type + " not loaded: " + ex.Message); }
        }

        t.LoadedAt = DateTime.Now;
        return t;
    }

    /// <summary>True (and remembered in the table) when the server asked us to slow down.</summary>
    static bool Limited(HttpResponseMessage resp, PriceTable t)
    {
        int code = (int)resp.StatusCode;
        if (code != 429 && code != 503) return false;
        t.RateLimited = true;
        RetryConditionHeaderValue ra = resp.Headers.RetryAfter;
        if (ra != null)
        {
            if (ra.Delta.HasValue) t.RetryAfter = ra.Delta.Value;
            else if (ra.Date.HasValue) t.RetryAfter = ra.Date.Value - DateTimeOffset.UtcNow;
        }
        Log.Write("poe.ninja rate limit (" + code + "), retry after: " + (ra != null ? ra.ToString() : "-"));
        return true;
    }

    static void ReadRates(PriceTable t, JsonElement root)
    {
        if (t.ExPerDiv > 0) return;
        JsonElement core, rates, v;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("core", out core)) return;
        if (core.ValueKind != JsonValueKind.Object || !core.TryGetProperty("rates", out rates)) return;
        if (rates.ValueKind != JsonValueKind.Object) return;
        if (rates.TryGetProperty("exalted", out v)) t.ExPerDiv = Num(v);
        if (rates.TryGetProperty("chaos", out v)) t.ChaosPerDiv = Num(v);
    }
}
