using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Poe2StashPricer.Pricing;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.Session;

/// <summary>
/// Keeps poe.ninja prices current: fetched at start and then every 15 minutes. A failed or rate-limited
/// fetch is retried sooner (1, 2, 5, 10, 15 minutes), or when poe.ninja's Retry-After says so.
/// </summary>
public class PriceFeed
{
    private static readonly TimeSpan PriceInterval = TimeSpan.FromMinutes(15);
    private static readonly int[] RetryMinutes = { 1, 2, 5, 10, 15 };

    private readonly AppSettings _settings;
    private DateTime _nextPriceLoad = DateTime.MaxValue;
    private DateTime _nextLeagueLoad = DateTime.MaxValue;
    private int _priceFailures, _leagueFailures;
    private volatile bool _loading;

    public PriceFeed(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>The prices in use, or null before the first successful load.</summary>
    public PriceTable Table { get; private set; }

    public List<string> Leagues { get; private set; } = new List<string>();

    public bool IsLoading { get { return _loading; } }

    /// <summary>Something worth putting in the status line happened.</summary>
    public event Action<string> Status;

    /// <summary>The prices changed, so everything showing a value is now out of date.</summary>
    public event Action Changed;

    private void Say(string text)
    {
        Action<string> handler = Status;
        if (handler != null) handler(text);
    }

    /// <summary>Call now and then; starts whichever fetch is due. Nothing happens during a scan.</summary>
    public async Task TickAsync(bool busy)
    {
        if (_loading || busy) return;
        DateTime now = DateTime.Now;
        if (now >= _nextLeagueLoad) await LoadLeaguesAsync();
        else if (now >= _nextPriceLoad) await LoadPricesAsync(true);
    }

    private DateTime RetryAt(int failures, TimeSpan retryAfter)
    {
        TimeSpan wait = TimeSpan.FromMinutes(RetryMinutes[Math.Min(failures, RetryMinutes.Length) - 1]);
        if (retryAfter > wait) wait = retryAfter;
        return DateTime.Now + wait;
    }

    public async Task LoadLeaguesAsync()
    {
        _nextLeagueLoad = DateTime.MaxValue;
        Say("Loading leagues...");
        List<string> leagues;
        try
        {
            leagues = await Task.Run(() => PriceService.GetLeagues());
            _leagueFailures = 0;
        }
        catch (Exception ex)
        {
            _leagueFailures++;
            _nextLeagueLoad = RetryAt(_leagueFailures, TimeSpan.Zero);
            Say(string.Format("Could not reach poe.ninja: {0} Retrying at {1:HH:mm}.", ex.Message, _nextLeagueLoad));
            Log.Write("leagues failed: " + ex.Message);
            leagues = new List<string>();
            if (!string.IsNullOrEmpty(_settings.League)) leagues.Add(_settings.League);
        }

        if (leagues.Count > 0) Leagues = leagues;
        if (_settings.League == null || leagues.IndexOf(_settings.League) < 0)
        {
            if (leagues.Count > 0)
            {
                _settings.League = leagues[0];
                _settings.Save();
            }
        }
        if (Table == null && !string.IsNullOrEmpty(_settings.League)) await LoadPricesAsync(false);
    }

    /// <summary>Switches league and loads its prices.</summary>
    public async Task SetLeagueAsync(string league)
    {
        if (string.IsNullOrEmpty(league) || (league == _settings.League && Table != null)) return;
        _settings.League = league;
        _settings.Save();
        await LoadPricesAsync(false);
    }

    /// <param name="auto">An automatic update runs quietly; only its result is shown.</param>
    public async Task LoadPricesAsync(bool auto)
    {
        string league = _settings.League;
        if (_loading || string.IsNullOrEmpty(league)) return;
        _loading = true;
        _nextPriceLoad = DateTime.MaxValue;
        if (!auto) _priceFailures = 0;
        try
        {
            Action<string> report = auto ? null : (Action<string>)Say;
            PriceTable old = Table;
            PriceTable t = await Task.Run(() => PriceService.Load(league, report));
            t.FillFailedFrom(old);
            string when;
            if (t.Failed.Count == 0)
            {
                _priceFailures = 0;
                _nextPriceLoad = DateTime.Now + PriceInterval;
                when = string.Format("next update {0:HH:mm}", _nextPriceLoad);
            }
            else
            {
                _priceFailures++;
                _nextPriceLoad = RetryAt(_priceFailures, t.RetryAfter);
                when = string.Format("{0} {1:HH:mm}", t.RateLimited ? "poe.ninja asked to slow down, retrying at" : "retrying at", _nextPriceLoad);
                Log.Write("prices: failed " + string.Join(", ", t.Failed.ToArray()) + (t.RateLimited ? " (rate limited)" : "")
                          + ", retry " + _nextPriceLoad.ToString("HH:mm:ss"));
            }
            if (t.Count == 0)
            {
                Say(old != null
                    ? "Could not update prices, keeping the ones from " + old.LoadedAt.ToString("HH:mm") + " · " + when
                    : "Could not load prices. Check your internet connection · " + when);
                return;
            }
            Table = t;
            string msg = string.Format("{0} prices: {1} items · 1 div = {2:0} ex · updated {3:HH:mm} · {4}",
                                       league, t.Count, t.ExPerDiv, t.LoadedAt, when);
            if (t.Failed.Count > 0 && !t.RateLimited) msg += " · failed: " + string.Join(", ", t.Failed.ToArray());
            Say(msg);
            Action changed = Changed;
            if (changed != null) changed();
        }
        catch (Exception ex)
        {
            _priceFailures++;
            _nextPriceLoad = RetryAt(_priceFailures, TimeSpan.Zero);
            Say(string.Format("Price loading error: {0} · retrying at {1:HH:mm}", ex.Message, _nextPriceLoad));
        }
        finally
        {
            _loading = false;
            // The league was changed while loading: load that one now.
            if (_settings.League != league) _nextPriceLoad = DateTime.Now;
        }
    }

    /// <summary>Prices older than an hour are worth refreshing before a scan is recorded against them.</summary>
    public bool IsStale
    {
        get { return Table != null && (DateTime.Now - Table.LoadedAt).TotalMinutes > 60; }
    }
}
