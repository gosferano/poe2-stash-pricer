using System;
using System.IO;

namespace Poe2StashPricer.Storage;

/// <summary>
/// Where the app keeps its data: ~/.config/poe2-stash-pricer (settings, learned tabs, scan results,
/// learned digits and the log).
/// </summary>
public static class AppPaths
{
    public static string Dir { get { return Path.Combine(ConfigHome(), "poe2-stash-pricer"); } }

    static string ConfigHome()
    {
        string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdg)) return xdg;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
    }
}
