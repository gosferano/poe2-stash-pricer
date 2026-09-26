using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json.Serialization;

namespace Poe2StashPricer.Tabs
{
    /// <summary>
    /// What was learned about one stash tab on its first scan: its name, where its slots are and how it looks.
    /// Everything is stored relative to the stash area, so it keeps working after a resolution change.
    /// </summary>
    public class TabProfile
    {
        public string Key { get; set; }
        public string Name { get; set; }              // shown in the app, e.g. "Essence" (guessed from the items)
        public string Kind { get; set; }              // the guess from the items when learned (Name can be renamed); null before 1.3
        public DateTime Saved { get; set; }
        public double CellFrac { get; set; }          // cell size / stash width
        public List<double[]> Slots { get; set; }     // x, y, w, h as fractions of the stash area
        public List<double> Signature { get; set; }   // coarse greyscale picture of the stash area
        public List<double> ItemMask { get; set; }    // 1 where a signature cell showed items (ignored when comparing)
        public double[] FrameColor { get; set; }      // colour of the tab's frame, or null
        public bool Paged { get; set; }               // shows one of several pages at a time: not saved, not in the total
        [JsonIgnore]
        public bool BuiltIn { get; set; }             // a layout shipped with the app (Layouts.json), not learned

        public TabProfile() { Slots = new List<double[]>(); Signature = new List<double>(); }

        public List<Rectangle> SlotsIn(Size area)
        {
            List<Rectangle> res = new List<Rectangle>();
            foreach (double[] s in Slots)
                res.Add(new Rectangle((int)Math.Round(s[0] * area.Width), (int)Math.Round(s[1] * area.Height),
                                      (int)Math.Round(s[2] * area.Width), (int)Math.Round(s[3] * area.Height)));
            return res;
        }
    }
}
