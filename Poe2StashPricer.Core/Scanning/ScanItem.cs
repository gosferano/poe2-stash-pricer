using System.Drawing;
using Poe2StashPricer.Pricing;

namespace Poe2StashPricer.Scanning
{
    public class ScanItem
    {
        public string Text;        // raw clipboard text
        public ParsedItem Item;
        public PriceInfo Price;
        public int Qty;
        public bool CountUnread;   // count had to come from the screen and couldn't be read (Qty is 1)
        public double TotalDiv;
        public Rectangle Bounds;   // screen coordinates
    }
}
