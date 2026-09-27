using System.Collections.Generic;
using System.Drawing;
using Poe2StashPricer.Detection;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.Scanning;

public class ScanResult
{
    public List<ScanItem> Items = new List<ScanItem>();
    public int CellsTried, CellsCopied;
    public int CellsRetried, CellsRecovered;   // second, slower tries of spots that copied nothing
    public string Timing;          // where the time went, for the log
    public bool Aborted;
    public bool StashNotFound;
    public bool BlackScreen;       // the game picture can't be captured (denied, or it came back black)
    public TabProfile Tab;         // recognised saved tab, or null
    public double TabDifference = 1;   // how far the picture was from that tab (0 = identical)
    public TabProfile Candidate;   // not recognised for sure, but looks like this saved tab (decided by the items)
    public double[] FrameColor;    // colour of the tab's frame (for learning a new tab), or null
    public PixelBuffer Snapshot;   // the stash as captured before hovering (baseline for tab-change detection)
}
