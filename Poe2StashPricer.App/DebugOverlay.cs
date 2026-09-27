using System;
using System.Drawing;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Poe2StashPricer.App.Platform.Linux;
using Poe2StashPricer.App.Views;
using Poe2StashPricer.Session;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App;

/// <summary>
/// --overlay-test: puts the overlay on screen with made-up prices, over whatever happens to be there. It
/// needs no game and no scan, which is what makes it useful for checking how the compositor treats it -
/// whether it blurs what is behind, draws a border, or takes focus.
/// </summary>
internal static class DebugOverlay
{
    private static LinuxPlatform? _platform;
    private static OverlayWindow? _overlay;

    /// <summary>Where the test patch goes, and whether to draw anything in it.</summary>
    public static Rectangle Where = new Rectangle(400, 300, 844, 846);
    public static bool Bare;

    public static void Attach(IClassicDesktopStyleApplicationLifetime desktop, bool applyRules)
    {
        AppSettings settings = AppSettings.Load();
        _platform = LinuxPlatform.Create(settings);
        Console.WriteLine("compositor rules: " + (applyRules ? "asked for" : "NOT asked for (for comparison)"));
        if (applyRules) Platform.Hyprland.HyprlandRules.Apply();

        _overlay = new OverlayWindow(_platform.Display);
        desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
        desktop.Exit += (s, e) => _platform?.Dispose();

        // A patch of the screen the size a stash would be, with prices spread over it.
        Rectangle region = Where;
        OverlayContent content = new OverlayContent
        {
            Kind = OverlayKind.Prices,
            Region = region,
            Header = Bare ? null : "Overlay test · this is not a real scan · nothing here is priced",
        };
        int slot = 70;
        for (int row = 0; row < 6 && !Bare; row++)
            for (int col = 0; col < 6; col++)
                content.Labels.Add(new OverlayLabel
                {
                    Bounds = new Rectangle(region.X + 20 + col * slot * 2, region.Y + 20 + row * slot * 2, slot, slot),
                    Text = (row * 6 + col + 1) + " ex",
                    Tier = row == 0 ? ValueTier.High : row == 1 ? ValueTier.Mid : ValueTier.Low,
                });

        Dispatcher.UIThread.Post(() => _overlay.Update(content));
        Console.WriteLine("overlay shown at " + region + "; Ctrl+C to stop.");
    }
}
