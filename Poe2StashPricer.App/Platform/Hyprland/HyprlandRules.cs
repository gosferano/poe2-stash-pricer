using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App.Platform.Hyprland;

/// <summary>
/// Asks Hyprland to leave the overlay alone.
///
/// A compositor blurs whatever is behind a window that has transparency, and behind the overlay is the
/// game: with blur on, the stash ends up looking washed out. There is no way to say "do not blur me" in any
/// Wayland protocol, so on Hyprland the app asks over its own IPC socket instead of making the user edit a
/// config file. The rules are scoped to this app's own window class and last only until the compositor
/// reloads its config; nothing of the user's is written to.
///
/// Everything here is optional. On any other compositor none of it runs, and the overlay still works - it
/// just looks however that compositor draws a transparent window.
/// </summary>
internal static class HyprlandRules
{
    /// <summary>
    /// The window class Avalonia gives our windows - all of them, which is why the title matters too: these
    /// rules are for the overlay alone. Applied to the main window they would stop it ever taking focus,
    /// and the desktop's own "close the focused window" would act on something else entirely.
    /// </summary>
    private const string WindowClass = "Poe2StashPricer.App";

    /// <summary>Must match the overlay window's Title exactly.</summary>
    public const string OverlayTitle = "PoE2 Stash Pricer overlay";

    private static readonly string[] Rules =
    {
        "float = true",
        // A config of one's own may centre or resize every floating window (CachyOS's default does). The
        // overlay has to sit exactly over the stash, so it says plainly that it places itself.
        "center = false",
        "persistent_size = false",
        "pin = true",
        "no_focus = true",         // never takes the keyboard from the game
        "no_follow_mouse = true",
        "no_blur = true",          // the one that matters: the game behind it stays sharp
        "no_shadow = true",
        "no_anim = true",
        "no_dim = true",
        "opacity = 1.0",           // inactive_opacity would otherwise fade the prices
        "border_size = 0",
        "rounding = 0",
    };

    public static bool IsHyprland
    {
        get { return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")); }
    }

    /// <summary>
    /// Applies the rules, if this is Hyprland. Must run before the overlay window is shown: a window rule
    /// is decided when the window appears. Never throws; the overlay is worth showing either way.
    /// </summary>
    public static void Apply()
    {
        if (!IsHyprland) return;
        string? path = SocketPath();
        if (path == null || !File.Exists(path))
        {
            Log.Write("Hyprland's IPC socket was not found, so the overlay rules were not applied");
            return;
        }
        int applied = 0;
        foreach (string rule in Rules)
        {
            string match = "{ class = \"" + WindowClass + "\", title = \"" + OverlayTitle + "\" }";
            string? answer = Send(path, "eval hl.window_rule({ match = " + match + ", " + rule + " })");
            if (answer == "ok") applied++;
            else Log.Write("Hyprland refused the overlay rule '" + rule + "': " + (answer ?? "no answer"));
        }
        Log.Write("Hyprland overlay rules applied: " + applied + " of " + Rules.Length);
    }

    private static string? SocketPath()
    {
        string? runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        string? signature = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE");
        if (string.IsNullOrEmpty(runtime) || string.IsNullOrEmpty(signature)) return null;
        return Path.Combine(runtime, "hypr", signature, ".socket.sock");
    }

    private static string? Send(string path, string command)
    {
        try
        {
            using (Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
            {
                socket.Connect(new UnixDomainSocketEndPoint(path));
                socket.Send(Encoding.UTF8.GetBytes(command));
                byte[] buffer = new byte[4096];
                int n = socket.Receive(buffer);
                return Encoding.UTF8.GetString(buffer, 0, n).Trim();
            }
        }
        catch (Exception ex)
        {
            Log.Write("Hyprland IPC failed: " + ex.Message);
            return null;
        }
    }
}
