using System.Drawing;

namespace Poe2StashPricer.Scanning
{
    public class ScanConfig
    {
        public Rectangle Window;    // game client area, screen coordinates
        public int TintSensitivity;
        public double Threshold;
        public bool FixedLayout;    // a saved fixed-slot tab: every item type has exactly one slot (some are 2x2)
        public int HoverDelay;
        public int CopyTimeout;

        // Filled in by Scanner.Prepare from the screenshot:
        public Rectangle Region;    // stash contents, screen coordinates
        public double CellSize;     // one stash cell in pixels (normal or quad tab)
    }
}
