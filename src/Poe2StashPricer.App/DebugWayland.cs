using System;
using System.Collections.Generic;
using Poe2StashPricer.App.Platform.Linux;

namespace Poe2StashPricer.App;

/// <summary>--debug-wayland: what the compositor offers, and whether the clipboard can be reached.</summary>
internal static class DebugWayland
{
    public static int Run()
    {
        using WaylandConnection? c = WaylandConnection.TryOpen();
        if (c == null) { Console.Error.WriteLine("no Wayland connection"); return 1; }

        Console.WriteLine(c.Globals.Count + " globals advertised:");
        List<string> names = new List<string>(c.Globals.Keys);
        names.Sort();
        foreach (string n in names)
            if (n.Contains("data_control") || n.Contains("seat") || n.Contains("data_device"))
                Console.WriteLine(string.Format("  {0,-42} version {1}", n, c.Globals[n].Version));

        Console.WriteLine();
        Console.WriteLine("data-control protocol: " + (c.Protocol == null ? "NONE" : c.Protocol.Manager.Name));
        Console.WriteLine("manager bound:         " + (c.Manager != IntPtr.Zero));
        Console.WriteLine("seat bound:            " + (c.Seat != IntPtr.Zero));
        return c.Protocol == null ? 1 : 0;
    }

    /// <summary>Prints every clipboard change for a while: shows whether the game's copies reach Wayland.</summary>
    public static int Watch(int seconds)
    {
        using WaylandClipboard? clip = WaylandClipboard.TryCreate();
        if (clip == null) { Console.Error.WriteLine("no Wayland clipboard"); return 1; }
        Console.WriteLine("watching the Wayland clipboard through " + clip.ProtocolName + " for " + seconds + " s");
        ulong seen = ulong.MaxValue;
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            ulong now = clip.ChangeCount;
            if (now != seen)
            {
                seen = now;
                string? t = clip.GetText();
                Console.WriteLine(string.Format("  [{0,6:0} ms] change {1}: {2}", sw.Elapsed.TotalMilliseconds, now,
                    t == null ? "(not text)" : "\"" + Shorten(t) + "\""));
            }
            System.Threading.Thread.Sleep(20);
        }
        return 0;
    }

    private static string Shorten(string s)
    {
        s = s.Replace("\n", " / ").Replace("\r", "");
        return s.Length > 60 ? s.Substring(0, 60) + "..." : s;
    }
}
