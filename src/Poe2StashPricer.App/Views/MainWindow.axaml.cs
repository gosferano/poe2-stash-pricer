using Avalonia.Controls;
using Avalonia.Interactivity;
using Poe2StashPricer.App.ViewModels;

namespace Poe2StashPricer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private MainWindowViewModel? Model => DataContext as MainWindowViewModel;

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? model = Model;
        if (model?.SelectedTab == null) return;
        string? name = await Ask.ForText(this, "New name for this tab:", model.SelectedTab.Name);
        if (name != null) model.Rename(name);
    }

    private async void OnDeleteAll(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? model = Model;
        if (model == null) return;
        bool sure = await Ask.ToConfirm(this,
            "Delete all saved tabs, scan results and learned digits?\n\n"
            + "The app starts over as if freshly installed; tabs are learned again on their first scan. "
            + "Your league and currency choices are kept.",
            "Delete everything");
        if (sure) model.DeleteEverythingCommand.Execute(null);
    }
}
