using System.IO;

namespace Poe2StashPricer.Storage;

public class AppSettings
{
    public string League { get; set; }
    public string DisplayCurrency { get; set; }   // auto | divine | exalted | chaos
    public int TintSensitivity { get; set; }      // item-background detection, see SlotDetector
    public int Threshold { get; set; }            // "detailed cell" threshold, see Grid.Busyness
    public Hotkey ScanKey { get; set; }           // default F7
    public Hotkey OverlayKey { get; set; }        // default F8
    public int HoverDelay { get; set; }
    public int CopyTimeout { get; set; }
    public bool HoverPrices { get; set; }         // price the item the mouse rests on instead of scanning the whole tab

    public AppSettings()
    {
        DisplayCurrency = "auto";
        TintSensitivity = 8;
        Threshold = 12;
        ScanKey = new Hotkey("F7");
        OverlayKey = new Hotkey("F8");
        HoverDelay = 45;
        CopyTimeout = 150;
    }

    private static string FilePath { get { return Path.Combine(AppPaths.Dir, "settings.json"); } }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                AppSettings s = Json.Load<AppSettings>(FilePath);
                if (s != null) return s;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Dir);
            Json.Save(FilePath, this);
        }
        catch { }
    }
}
