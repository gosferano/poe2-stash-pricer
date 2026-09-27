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

    /// <summary>
    /// Binds the app's own keys. A compositor will not give a key to a window that is not focused, so the
    /// app cannot listen for one while the game is in front; what it can do is ask the compositor to run a
    /// command, which then tells the running copy what to do through its socket.
    ///
    /// The binding lasts until the compositor reloads its config, and is given back when the app closes.
    /// </summary>
    public static void Bind(string scanKey, string overlayKey, bool usePortal)
    {
        if (!IsHyprland) return;
        string? path = SocketPath();
        if (path == null || !File.Exists(path)) return;

        string? portalAppId = usePortal ? FindPortalAppId(path) : null;
        if (portalAppId != null)
        {
            // The desktop knows these shortcuts, so the key is pointed at the shortcut rather than at a
            // command: the app is told directly, the same way it would be on any other desktop.
            BindOne(path, scanKey, "hl.dsp.global(\"" + portalAppId + ":scan\")");
            BindOne(path, overlayKey, "hl.dsp.global(\"" + portalAppId + ":overlay\")");
            return;
        }
        string? exe = Environment.ProcessPath;
        if (exe == null) return;
        BindOne(path, scanKey, "hl.dsp.exec_cmd(\"" + exe + " --scan\")");
        BindOne(path, overlayKey, "hl.dsp.exec_cmd(\"" + exe + " --toggle-overlay\")");
    }

    private static void BindOne(string socket, string key, string dispatcher)
    {
        if (string.IsNullOrEmpty(key)) return;
        string answer = Send(socket, "eval hl.bind(\"" + key + "\", " + dispatcher + ")") ?? "no answer";
        if (answer == "ok") Log.Write("bound " + key + " to " + dispatcher);
        else Log.Write("Hyprland would not bind " + key + ": " + answer);
    }

    /// <summary>Gives the keys back, so they mean what they did before the app started.</summary>
    public static void Unbind(string scanKey, string overlayKey)
    {
        if (!IsHyprland) return;
        string? path = SocketPath();
        if (path == null || !File.Exists(path)) return;
        foreach (string key in new[] { scanKey, overlayKey })
            if (!string.IsNullOrEmpty(key)) Send(path, "eval hl.unbind(\"" + key + "\")");
    }

    /// <summary>
    /// Which name the portal filed our shortcuts under. It is not ours to choose: the portal works it out
    /// from the process that registered them, so an app launched from a terminal, a desktop entry or
    /// another program can each be filed differently. Rather than guess, the compositor is asked which
    /// shortcuts it knows and the one carrying our own description is picked out.
    /// </summary>
    private static string? FindPortalAppId(string socket)
    {
        string? listing = Send(socket, "globalshortcuts");
        if (listing == null) return null;
        foreach (string line in listing.Split('\n'))
        {
            int arrow = line.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0) continue;
            string name = line.Substring(0, arrow).Trim();
            string description = line.Substring(arrow + 2).Trim();
            if (description != ScanDescription || !name.EndsWith(":scan", StringComparison.Ordinal)) continue;
            string appId = name.Substring(0, name.Length - ":scan".Length);
            Log.Write("the portal filed our shortcuts under '" + appId + "'");
            return appId;
        }
        Log.Write("the portal's shortcuts were not found in the compositor's list");
        return null;
    }

    public const string ScanDescription = "Scan the stash tab that is open";
    public const string OverlayDescription = "Show or hide the prices over the game";

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
