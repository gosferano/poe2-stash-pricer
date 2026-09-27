using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Poe2StashPricer.App.Views;

/// <summary>
/// The two small dialogs the app needs. Upstream used the message boxes Windows provides and Visual Basic's
/// InputBox; Avalonia has neither, so they are built here rather than taking on a dialog library.
/// </summary>
internal static class Ask
{
    public static async Task<string?> ForText(Window owner, string question, string initial)
    {
        TextBox box = new TextBox { Text = initial, Margin = new Avalonia.Thickness(0, 10, 0, 12), MinWidth = 320 };
        Window dialog = Shell(owner, question, box, out Button ok, out Button cancel, "Rename", "Cancel");
        string? answer = null;
        ok.Click += (_, _) => { answer = box.Text; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        box.SelectAll();
        box.Focus();
        await dialog.ShowDialog(owner);
        return string.IsNullOrWhiteSpace(answer) ? null : answer!.Trim();
    }

    public static async Task<bool> ToConfirm(Window owner, string question, string confirmText)
    {
        Window dialog = Shell(owner, question, null, out Button ok, out Button cancel, confirmText, "Cancel");
        ok.Foreground = new SolidColorBrush(Color.Parse("#D9584A"));
        bool answer = false;
        ok.Click += (_, _) => { answer = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
        return answer;
    }

    private static Window Shell(Window owner, string question, Control? content,
                                out Button ok, out Button cancel, string okText, string cancelText)
    {
        ok = new Button { Content = okText, Margin = new Avalonia.Thickness(0, 0, 8, 0), Padding = new Avalonia.Thickness(14, 6) };
        cancel = new Button { Content = cancelText, Padding = new Avalonia.Thickness(14, 6) };
        StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        StackPanel panel = new StackPanel { Margin = new Avalonia.Thickness(18) };
        panel.Children.Add(new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        if (content != null) panel.Children.Add(content);
        else panel.Children.Add(new Control { Height = 12 });
        panel.Children.Add(buttons);

        return new Window
        {
            Title = owner.Title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.Parse("#1C1B15")),
        };
    }
}
