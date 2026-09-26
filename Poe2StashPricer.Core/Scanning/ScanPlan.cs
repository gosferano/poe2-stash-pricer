using System.Collections.Generic;
using Poe2StashPricer.Detection;
using Poe2StashPricer.Tabs;

namespace Poe2StashPricer.Scanning
{
    /// <summary>What to hover, decided from one clean screenshot.</summary>
    public class ScanPlan
    {
        public bool StashVisible;
        public bool BlackScreen;              // the game picture can't be captured (denied, or it came back black)
        public TabProfile Tab;                // recognised saved tab, or null
        public double Difference = 1;         // picture difference to the closest saved tab
        public TabProfile Candidate;          // a similar saved tab, when none matched for sure
        public double[] FrameColor;           // colour of the tab's frame, or null
        public PixelBuffer Snapshot;          // the stash region only
        public List<ProbeGroup> Groups = new List<ProbeGroup>();
    }
}
