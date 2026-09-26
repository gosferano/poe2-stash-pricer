using System.Drawing;

namespace Poe2StashPricer.Scanning
{
    /// <summary>
    /// A small grid of hover points: one per detected item area (split into cells when it covers several
    /// touching items), plus one for the slot lattice of fixed-layout tabs.
    /// </summary>
    public class ProbeGroup
    {
        public int Rows, Cols;
        public Rectangle[,] Rects;   // screen coordinates
        public bool[,] Active;       // false = considered empty, not hovered
        public bool[,] Likely;       // looked occupied in the screenshot (a saved slot is hovered even when not)
        public double[,] Score;      // share of item background (shown in preview)
        public string[,] Texts;
        public int Priority;         // lower wins when probes of different groups overlap

        public ProbeGroup(int rows, int cols)
        {
            Rows = rows; Cols = cols;
            Rects = new Rectangle[rows, cols];
            Active = new bool[rows, cols];
            Likely = new bool[rows, cols];
            Score = new double[rows, cols];
            Texts = new string[rows, cols];
        }
    }
}
