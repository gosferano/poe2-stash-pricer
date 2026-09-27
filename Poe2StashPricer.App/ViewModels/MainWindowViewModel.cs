using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poe2StashPricer.Pricing;
using Poe2StashPricer.Session;
using Poe2StashPricer.Storage;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.App.ViewModels;

/// <summary>
/// What the main window shows, kept in step with the session. The session raises everything on its own
/// thread, so each handler hands the work to the interface thread before touching any of this.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private static readonly string[] CurrencyKeys = { "auto", "divine", "exalted", "chaos" };
    private static readonly string[] CurrencyNames = { "Auto", "Divine", "Exalted", "Chaos" };

    private readonly PricerSession _session;
    private readonly AppSettings _settings;
    private bool _loading;

    public MainWindowViewModel(PricerSession session, AppSettings settings)
    {
        _session = session;
        _settings = settings;

        foreach (string name in CurrencyNames) Currencies.Add(name);
        int currency = Array.IndexOf(CurrencyKeys, settings.DisplayCurrency ?? "auto");
        _selectedCurrency = CurrencyNames[currency < 0 ? 0 : currency];
        _hoverPrices = settings.HoverPrices;

        session.StatusChanged += text => OnUi(() => Status = text);
        session.ViewChanged += () => OnUi(Refresh);
        session.BusyChanged += busy => OnUi(() => Busy = busy);
        session.ScanProgress += (done, total) => OnUi(() =>
        {
            ProgressMax = Math.Max(1, total);
            Progress = Math.Min(done, ProgressMax);
        });
        Refresh();
    }

    private static void OnUi(Action action) { Dispatcher.UIThread.Post(action); }

    // ---- what is on screen ----

    public ObservableCollection<TabRowVm> Tabs { get; } = new ObservableCollection<TabRowVm>();
    public ObservableCollection<ItemRowVm> Items { get; } = new ObservableCollection<ItemRowVm>();
    public ObservableCollection<string> Leagues { get; } = new ObservableCollection<string>();
    public ObservableCollection<string> Currencies { get; } = new ObservableCollection<string>();

    [ObservableProperty] private string _grandTotal = "—";
    [ObservableProperty] private string _grandSubtitle = "";
    [ObservableProperty] private string _itemsHeader = "";
    [ObservableProperty] private string? _itemsNote;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private int _progress;
    [ObservableProperty] private int _progressMax = 1;
    [ObservableProperty] private string? _selectedLeague;
    [ObservableProperty] private string _selectedCurrency;
    [ObservableProperty] private bool _hoverPrices;
    [ObservableProperty] private TabRowVm? _selectedTab;

    [ObservableProperty] private bool _busy;

    partial void OnBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ScanButtonText));
        OnPropertyChanged(nameof(CanEdit));
    }

    public bool CanEdit => !Busy;

    public string ScanButtonText => Busy ? "Stop" : HoverPrices ? "Get tab ready" : "Scan tab";

    partial void OnHoverPricesChanged(bool value)
    {
        if (_loading) return;
        _session.SetHoverPricing(value);
        OnPropertyChanged(nameof(ScanButtonText));
    }

    partial void OnSelectedCurrencyChanged(string value)
    {
        if (_loading) return;
        int i = Array.IndexOf(CurrencyNames, value);
        _session.SetDisplayCurrency(CurrencyKeys[i < 0 ? 0 : i]);
        Refresh();
    }

    partial void OnSelectedLeagueChanged(string? value)
    {
        if (_loading || value == null || value == _settings.League) return;
        _ = _session.Prices.SetLeagueAsync(value);
    }

    partial void OnSelectedTabChanged(TabRowVm? value)
    {
        if (_loading) return;
        // Picking a tab by hand stops the list following the game, until the game changes tab.
        _session.Model.ViewKey = value?.Key;
        RefreshItems();
    }

    // ---- filling it in ----

    public void Refresh()
    {
        _loading = true;
        try
        {
            PriceTable table = _session.Prices.Table;
            string mode = _settings.DisplayCurrency ?? "auto";

            bool leaguesChanged = !Leagues.SequenceEqual(_session.Prices.Leagues);
            if (leaguesChanged)
            {
                Leagues.Clear();
                foreach (string league in _session.Prices.Leagues) Leagues.Add(league);
            }
            // Emptying the list drops the box's selection, and re-assigning the same value would raise no
            // change for it to notice, so the selection is cleared first and put back.
            if (leaguesChanged) SelectedLeague = null;
            if (_settings.League != null && SelectedLeague != _settings.League) SelectedLeague = _settings.League;

            if (_session.Model.ScannedTabs == 0)
            {
                GrandTotal = "—";
                GrandSubtitle = "No tab scanned yet. Open a tab in the game and press the scan key.";
            }
            else
            {
                double sum = _session.Model.GrandTotal(table);
                GrandTotal = table == null ? "loading prices..." : Money.Format(sum, table, mode);
                string alt = table != null && table.ExPerDiv > 0
                    ? string.Format("≈ {0} div / {1} ex · ", Money.Num(sum), Money.Num(sum * table.ExPerDiv)) : "";
                GrandSubtitle = string.Format("{0}{1} tabs scanned · oldest scan {2:dd.MM HH:mm}",
                                              alt, _session.Model.ScannedTabs, _session.Model.OldestScan);
            }

            string? keep = SelectedTab?.Key;
            Tabs.Clear();
            foreach (TabRow row in _session.Model.TabRows(table))
                Tabs.Add(new TabRowVm
                {
                    Key = row.Key,
                    Name = row.Name,
                    Value = row.Scanned && table != null ? Money.Format(row.TotalDiv, table, mode) : "",
                    Status = row.Unsaved ? "not saved"
                           : row.Scanned ? (row.OnScreen ? "▶ " : "") + string.Format("scanned {0:dd.MM HH:mm}", row.ScannedAt)
                           : "not scanned",
                    OnScreen = row.OnScreen,
                    Unsaved = row.Unsaved,
                });
            foreach (TabRowVm row in Tabs)
                if (row.Key == (_session.Model.ViewKey ?? keep ?? _session.Model.CurrentTab)) { SelectedTab = row; break; }

            RefreshItems();
        }
        finally { _loading = false; }
    }

    private void RefreshItems()
    {
        PriceTable table = _session.Prices.Table;
        string mode = _settings.DisplayCurrency ?? "auto";
        string? key = _session.Model.ViewKey ?? _session.Model.CurrentTab;
        TabResult result = _session.Model.ResultFor(key);
        List<ItemRow> rows = Rows.Items(result, table);

        Items.Clear();
        foreach (ItemRow row in rows)
            Items.Add(new ItemRowVm
            {
                Name = row.Name,
                Qty = row.Qty.ToString("#,0") + (row.CountUnread ? "?" : ""),
                Unit = row.Priced ? Money.Format(row.UnitDiv, table, mode) : "—",
                Total = row.Priced ? Money.Format(row.TotalDiv, table, mode) : "no price",
                Category = row.Category,
                Priced = row.Priced,
                CountUnread = row.CountUnread,
            });

        if (key == null)
        {
            ItemsHeader = "Open a scanned tab in the game, or pick a tab on the left.";
            ItemsNote = null;
        }
        else if (result == null)
        {
            ItemsHeader = StashModel.NameOf(key) + " · not scanned yet";
            ItemsNote = null;
        }
        else
        {
            double sum = Rows.Total(rows);
            ItemsHeader = string.Format("{0} · {1} · scanned {2:dd.MM HH:mm}",
                                        StashModel.NameOf(key), Money.Format(sum, table, mode), result.ScannedAt);
            ItemsNote = OverlayContent.PriceChangeNote(result, sum, table, mode, _session.ScanKeyName);
        }
    }

    // ---- what the buttons do ----

    [RelayCommand] private void Scan() { _session.Scan(); }

    [RelayCommand] private void Preview() { _session.Preview(); }

    [RelayCommand] private void ToggleOverlay() { _session.ToggleOverlay(); }

    [RelayCommand] private void RefreshPrices() { _ = _session.Prices.LoadPricesAsync(false); }

    [RelayCommand]
    private void DeleteTab()
    {
        TabRowVm? row = SelectedTab;
        if (row == null) { Status = "Pick a tab in the list first."; return; }
        _session.DeleteTab(row.Key);
        Refresh();
    }

    [RelayCommand]
    private void DeleteEverything()
    {
        _session.DeleteEverything();
        Refresh();
    }

    public void Rename(string name)
    {
        TabRowVm? row = SelectedTab;
        if (row == null) { Status = "Pick a tab in the list first."; return; }
        if (!_session.Model.CanRename(row.Key)) { Status = "'" + row.Name + "' is a built-in tab: its name comes with the app."; return; }
        _session.RenameTab(row.Key, name);
        Refresh();
    }

    public bool CanRenameSelected => SelectedTab != null && _session.Model.CanRename(SelectedTab.Key);
}
