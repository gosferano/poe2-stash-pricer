using System;
using System.IO;

namespace Poe2StashPricer.Storage;

/// <summary>
/// A small diagnostic log in ~/.config/poe2-stash-pricer/log.txt. When something doesn't work on
/// someone's PC, this file shows what the app saw (hotkeys, game window, stash detection).
/// </summary>
public static class Log
{
    private const long MaxSize = 256 * 1024;
    private static readonly object _sync = new object();

    public static string FilePath { get { return Path.Combine(AppPaths.Dir, "log.txt"); } }

    public static void Write(string message)
    {
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(AppPaths.Dir);
                FileInfo fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxSize)
                {
                    // Keep the newer half so the file doesn't grow without end.
                    string all = File.ReadAllText(FilePath);
                    File.WriteAllText(FilePath, all.Substring(all.Length / 2));
                }
                File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
        }
        catch { }
    }
}
