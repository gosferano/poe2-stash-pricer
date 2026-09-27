using System;
using System.Threading;
using Poe2StashPricer.App.Platform.Linux;
using Poe2StashPricer.Storage;

namespace Poe2StashPricer.App;

/// <summary>--debug-clipboard: reads the clipboard, puts a text of our own on it and holds it, so that
/// what other programs see while the app owns the selection can be checked from outside.</summary>
internal static class DebugClipboard
{
    public static int Run(int holdSeconds, bool sensitive)
    {
        AppSettings settings = AppSettings.Load();
        using LinuxPlatform platform = LinuxPlatform.Create(settings);
        Poe2StashPricer.Platform.IClipboard clip = platform.Clipboard;

        Console.WriteLine("reached through: " + platform.ClipboardKind);
        Console.WriteLine("change count at start: " + clip.ChangeCount);
        string? before = clip.GetText();
        Console.WriteLine("clipboard now: " + (before == null ? "(nothing)" : "[" + before + "]"));

        string mine = "RESTORED-BY-POE2-STASH-PRICER";
        Console.WriteLine("taking the selection with [" + mine + "], sensitive=" + sensitive);
        clip.SetText(mine, sensitive);
        Thread.Sleep(300);
        Console.WriteLine("read back through X: " + (clip.GetText() ?? "(nothing)"));
        Console.WriteLine("change count: " + clip.ChangeCount);
        Console.WriteLine("holding for " + holdSeconds + " s so other programs can read it...");
        Thread.Sleep(holdSeconds * 1000);
        Console.WriteLine("done");
        return 0;
    }
}
