using CommunityToolkit.Mvvm.ComponentModel;

namespace Poe2StashPricer.App.ViewModels;

/// <summary>A tab as the list shows it: everything already turned into text.</summary>
public partial class TabRowVm : ObservableObject
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public string Status { get; init; } = "";
    public bool OnScreen { get; init; }
    public bool Unsaved { get; init; }
}

/// <summary>One line of the item list.</summary>
public partial class ItemRowVm : ObservableObject
{
    public string Name { get; init; } = "";
    public string Qty { get; init; } = "";
    public string Unit { get; init; } = "";
    public string Total { get; init; } = "";
    public string Category { get; init; } = "";
    public bool Priced { get; init; }
    public bool CountUnread { get; init; }
}
