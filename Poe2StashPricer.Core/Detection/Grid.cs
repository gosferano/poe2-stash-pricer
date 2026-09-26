using System;
using System.Collections.Generic;
using System.Drawing;
using Poe2StashPricer.Scanning;

namespace Poe2StashPricer.Detection;

public static class Grid
{
    public static Rectangle Cell(Rectangle region, int cols, int rows, int r, int c)
    {
        int x0 = region.X + (int)Math.Round(region.Width * (double)c / cols);
        int x1 = region.X + (int)Math.Round(region.Width * (double)(c + 1) / cols);
        int y0 = region.Y + (int)Math.Round(region.Height * (double)r / rows);
        int y1 = region.Y + (int)Math.Round(region.Height * (double)(r + 1) / rows);
        return new Rectangle(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Standard deviation of luminance in the cell's inner area: empty cells are flat, icons are detailed.</summary>
    public static double Busyness(PixelBuffer pb, Rectangle cell)
    {
        int mx = Math.Max(1, cell.Width / 5), my = Math.Max(1, cell.Height / 5);
        double sum = 0, sq = 0; int n = 0;
        for (int y = Math.Max(0, cell.Top + my); y < Math.Min(pb.Height, cell.Bottom - my); y++)
            for (int x = Math.Max(0, cell.Left + mx); x < Math.Min(pb.Width, cell.Right - mx); x++)
            {
                int o = y * pb.Stride + x * 4;
                double l = 0.114 * pb.Px[o] + 0.587 * pb.Px[o + 1] + 0.299 * pb.Px[o + 2];
                sum += l; sq += l * l; n++;
            }
        if (n == 0) return 0;
        double mean = sum / n;
        return Math.Sqrt(Math.Max(0, sq / n - mean * mean));
    }

    private static Rectangle Offset(Rectangle r, Point by) { r.Offset(by); return r; }

    /// <summary>
    /// Splits an item area into slots of about <paramref name="slot"/> pixels. Touching or nearly touching
    /// items form one area, and slots can have gaps between them (Currency: 75 px slots, 90 px apart), so an
    /// even split by cell size lands on the edges. Slots in a row are evenly spaced: the first one starts at
    /// the area's left edge, the last one ends at its right edge, the others are spread between. How many
    /// slots there are is not simply length / slot (five slots with gaps look like six without), so each
    /// plausible count is tried and the one whose slot borders fall on lines without item background wins.
    /// </summary>
    public static Rectangle[,] Split(PixelBuffer pb, Rectangle blob, double slot, int sensitivity)
    {
        List<int[]> xs = Spans(pb, blob, slot, sensitivity, true), ys = Spans(pb, blob, slot, sensitivity, false);
        Rectangle[,] cells = new Rectangle[ys.Count, xs.Count];
        for (int r = 0; r < ys.Count; r++)
            for (int c = 0; c < xs.Count; c++)
                cells[r, c] = new Rectangle(xs[c][0], ys[r][0], xs[c][1] - xs[c][0], ys[r][1] - ys[r][0]);
        return cells;
    }

    private static List<int[]> Spans(PixelBuffer pb, Rectangle blob, double slot, int sensitivity, bool alongX)
    {
        int start = alongX ? blob.Left : blob.Top, len = alongX ? blob.Width : blob.Height;
        int guess = Math.Max(1, (int)Math.Round(len / slot));
        List<int[]> best = null;
        double bestScore = double.MaxValue;
        for (int n = Math.Max(1, (int)(len / (slot * 1.4))); n <= Math.Max(1, (int)Math.Ceiling(len / (slot * 0.9))); n++)
        {
            int size = n == 1 ? len : (int)Math.Round(Math.Min(slot, len / (double)n));
            if (n > 1)
            {
                double gap = (len - n * size) / (double)(n - 1);
                if (size < slot * 0.85 || gap > slot * 0.4) continue;   // slots this small or this far apart: not this count
            }
            List<int[]> spans = new List<int[]>();
            double pitch = n == 1 ? 0 : (len - size) / (double)(n - 1);
            for (int i = 0; i < n; i++)
            {
                int a = start + (int)Math.Round(i * pitch);
                spans.Add(new[] { a, a + size });
            }
            // Item background on the borders between neighbouring slots (little is good); a count close to
            // length / slot is preferred when the picture can't tell.
            double score = Math.Abs(n - guess) * 0.05;
            if (n > 1 && pb != null)
            {
                double border = 0;
                for (int i = 0; i + 1 < n; i++)
                {
                    int mid = (spans[i][1] + spans[i + 1][0]) / 2;
                    Rectangle band = alongX ? new Rectangle(mid - 1, blob.Top, 3, blob.Height) : new Rectangle(blob.Left, mid - 1, blob.Width, 3);
                    border += SlotDetector.TintFraction(pb, band, sensitivity, 0);
                }
                score += border / (n - 1);
            }
            else if (n == 1 && len > slot * 1.4) score += 2;   // an area this long holds several slots
            if (score < bestScore) { bestScore = score; best = spans; }
        }
        if (best == null)
        {
            best = new List<int[]>();
            for (int i = 0; i < guess; i++)
                best.Add(new[] { start + (int)Math.Round(len * (double)i / guess), start + (int)Math.Round(len * (double)(i + 1) / guess) });
        }
        return best;
    }

    /// <summary>
    /// Size of one slot as drawn: the median size of the areas that hold exactly one item, or a bit more
    /// than a cell (slots of the fixed-layout tabs are a little bigger than inventory cells).
    /// </summary>
    public static double SlotSize(List<Rectangle> blobs, double cs)
    {
        List<double> sizes = new List<double>();
        foreach (Rectangle b in blobs)
            if (b.Width >= cs * 0.8 && b.Width <= cs * 1.3 && b.Height >= cs * 0.8 && b.Height <= cs * 1.3)
                sizes.Add(Math.Max(b.Width, b.Height));
        if (sizes.Count < 2) return cs;
        sizes.Sort();
        return sizes[sizes.Count / 2];
    }

    /// <summary>
    /// Size of one slot. The stash area is 12 normal cells wide and the supported fixed-layout tabs
    /// use normal-sized slots. (Quad tabs would be 24 wide; small faded placeholder icons made guessing
    /// that from the picture unreliable, so it is not attempted.)
    /// </summary>
    public static double CellSizeFor(int stashWidth)
    {
        return stashWidth / 12.0;
    }

    /// <summary>
    /// Decides where to hover. <paramref name="pb"/> is a capture of <c>cfg.Region</c>;
    /// <paramref name="known"/> are the slots of the recognised tab (area coordinates), or null.
    /// <paramref name="exact"/>: they are a built-in layout, measured slot by slot, so every slot of the tab
    /// is in the list and nothing else is hovered.
    /// </summary>
    public static List<ProbeGroup> Plan(PixelBuffer pb, ScanConfig cfg, List<Rectangle> known, bool exact = false)
    {
        List<ProbeGroup> groups = new List<ProbeGroup>();
        Point origin = cfg.Region.Location;
        double cs = cfg.CellSize;

        // Slots learned from the user's screenshot of this tab: exact positions, checked for an item now.
        // A learned slot bigger than one cell (a 2x2 slot, or touching slots saved by version 1.2.0) is
        // checked cell by cell; repeated reads of one item are merged afterwards.
        List<Rectangle> blobs = SlotDetector.Detect(pb, cs, cfg.TintSensitivity);
        double slotSize = SlotSize(blobs, cs);
        // Item areas seen in this picture, one slot in size: where an item really sits right now.
        // Touching items form one area; its cells stand for them.
        List<Rectangle> single = new List<Rectangle>();
        foreach (Rectangle b in blobs)
        {
            if (b.Width < cs * 0.6 || b.Height < cs * 0.6 || b.Width * b.Height > cs * cs * 20) continue;
            foreach (Rectangle cell in Split(pb, b, slotSize, cfg.TintSensitivity)) single.Add(cell);
        }

        if (known != null)
            foreach (Rectangle slot in known)
            {
                // A measured big slot holds one item: hovered once, in its middle.
                int kx = exact ? 1 : Math.Max(1, (int)Math.Round(slot.Width / cs)), ky = exact ? 1 : Math.Max(1, (int)Math.Round(slot.Height / cs));
                for (int r = 0; r < ky; r++)
                    for (int c = 0; c < kx; c++)
                    {
                        Rectangle k = Cell(slot, kx, ky, r, c);
                        // A saved position can be a little off (learned by an older version, or from an
                        // item area that touched its neighbour). When an item area overlaps it, hover
                        // that area's middle instead of the saved one, which may be on the edge.
                        Rectangle snap = Rectangle.Empty;
                        double best = 0;
                        if (!exact)
                        foreach (Rectangle b in single)
                        {
                            Rectangle x = Rectangle.Intersect(b, k);
                            double share = (double)x.Width * x.Height / Math.Max(1, Math.Min(b.Width * b.Height, k.Width * k.Height));
                            if (share > 0.3 && share > best) { best = share; snap = b; }
                        }
                        if (!snap.IsEmpty) k = snap;
                        ProbeGroup g = new ProbeGroup(1, 1);
                        g.Rects[0, 0] = Offset(k, origin);
                        g.Score[0, 0] = SlotDetector.TintFraction(pb, k, cfg.TintSensitivity);
                        // Every saved slot is hovered: an icon can hide the item background, and an empty
                        // slot copies nothing, so hovering it costs only a moment.
                        g.Likely[0, 0] = Occupied(pb, k, cfg);
                        g.Active[0, 0] = true;
                        // A paged tab's grid (built-in, not fixed-slot) has no placeholder icons: an empty cell
                        // is plain black, so only the lit ones are hovered (0.00 empty, 0.42+ filled, measured
                        // at 720p to 1440p).
                        if (exact && !cfg.FixedLayout) g.Likely[0, 0] = g.Active[0, 0] = BrightShare(pb, k) >= 0.1;
                        g.Priority = -1;
                        groups.Add(g);
                    }
            }
        if (known != null && exact) return groups;

        // Fixed-slot tabs place slots in regular rows and columns. The cleanly detected single items
        // reveal those lines; testing every row x column crossing catches items whose own area was
        // missed or merged with something else.
        ProbeGroup lattice = Lattice(pb, cfg, blobs, origin);
        if (lattice != null) groups.Add(lattice);

        foreach (Rectangle blob in blobs)
        {
            // Touching items form one area: split it into cells and test each one. A big slot (Fragments
            // has 2x2 ones) is split too; its cells all read the same item, which is merged afterwards.
            Rectangle[,] parts = Split(pb, blob, slotSize, cfg.TintSensitivity);
            int ny = parts.GetLength(0), nx = parts.GetLength(1);
            ProbeGroup g = new ProbeGroup(ny, nx);
            for (int r = 0; r < ny; r++)
                for (int c = 0; c < nx; c++)
                {
                    Rectangle sub = parts[r, c];
                    g.Rects[r, c] = Offset(sub, origin);
                    g.Score[r, c] = SlotDetector.TintFraction(pb, sub, cfg.TintSensitivity);
                    // A lone area must be solidly item background, or at least framed by it when a big
                    // icon covers the middle. Faded placeholder icons in empty slots have a few navy-like
                    // pixels too, but only as a sparse speck.
                    g.Active[r, c] = nx * ny == 1
                        ? SlotDetector.TintFraction(pb, sub, cfg.TintSensitivity, 0) >= 0.2
                          || SlotDetector.RingTintFraction(pb, sub, cfg.TintSensitivity) >= 0.35
                        : Occupied(pb, sub, cfg);
                }
            // A large unsplit area may be several slots merged with the panel; let the exact slots
            // (learned ones and the lattice) win over it when they overlap.
            bool large = blob.Width > cs * 1.5 || blob.Height > cs * 1.5;
            g.Priority = nx * ny > 1 ? 2 : large ? 3 : 0;
            groups.Add(g);
        }

        // Areas can overlap; never hover the same spot twice (a stackable item would be counted twice).
        // Single-item areas are the most precise, then the slot lattice, then split multi-item areas.
        groups.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        List<Rectangle> taken = new List<Rectangle>();
        foreach (ProbeGroup g in groups)
            for (int r = 0; r < g.Rows; r++)
                for (int c = 0; c < g.Cols; c++)
                {
                    if (!g.Active[r, c]) continue;
                    Rectangle rc = g.Rects[r, c];
                    bool dup = false;
                    foreach (Rectangle t in taken)
                    {
                        // Overlapping more than a quarter of either rectangle = same spot.
                        Rectangle x = Rectangle.Intersect(t, rc);
                        double area = (double)x.Width * x.Height;
                        if (area > 0.25 * Math.Min(t.Width * t.Height, rc.Width * rc.Height)) { dup = true; break; }
                    }
                    if (dup) g.Active[r, c] = false;
                    else taken.Add(rc);
                    if (g.Priority != -1) g.Likely[r, c] = g.Active[r, c];   // found by looking, so it looked occupied
                }
        return groups;
    }

    /// <summary>
    /// Share of clearly lit pixels in the middle of a cell. An empty cell of a paged tab (Tablets...) is
    /// nearly black with a faint pattern; any item in it, whatever its colour, lights a good part of it.
    /// </summary>
    public static double BrightShare(PixelBuffer pb, Rectangle cell)
    {
        Rectangle r = Rectangle.Inflate(cell, -cell.Width / 6, -cell.Height / 6);
        r.Intersect(new Rectangle(0, 0, pb.Width, pb.Height));
        int lit = 0, all = 0;
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
            {
                int i = y * pb.Stride + x * 4;
                if (Math.Max(pb.Px[i], Math.Max(pb.Px[i + 1], pb.Px[i + 2])) >= 60) lit++;
                all++;
            }
        return all == 0 ? 0 : (double)lit / all;
    }

    /// <summary>
    /// Large icons hide most of the tint, so a detailed cell also counts as occupied,
    /// but only if some item background shows (textured panel stone has none).
    /// </summary>
    private static bool Occupied(PixelBuffer pb, Rectangle rect, ScanConfig cfg)
    {
        if (SlotDetector.RingTintFraction(pb, rect, cfg.TintSensitivity) >= 0.35) return true;
        if (SlotDetector.TintFraction(pb, rect, cfg.TintSensitivity, 0) < 0.06) return false;
        return SlotDetector.TintFraction(pb, rect, cfg.TintSensitivity) >= 0.25 || Busyness(pb, rect) >= cfg.Threshold;
    }

    private static List<double> Cluster(List<double> values, double tol)
    {
        values.Sort();
        List<double> centers = new List<double>();
        List<double> cur = new List<double>();
        foreach (double v in values)
        {
            if (cur.Count > 0 && v - cur[cur.Count - 1] > tol)
            {
                centers.Add(Average(cur));
                cur.Clear();
            }
            cur.Add(v);
        }
        if (cur.Count > 0) centers.Add(Average(cur));
        return centers;
    }

    private static double Average(List<double> l)
    {
        double s = 0;
        foreach (double v in l) s += v;
        return s / l.Count;
    }

    private static ProbeGroup Lattice(PixelBuffer pb, ScanConfig cfg, List<Rectangle> blobs, Point origin)
    {
        double cs = cfg.CellSize;
        List<double> xs = new List<double>(), ys = new List<double>(), sizes = new List<double>();
        foreach (Rectangle b in blobs)
        {
            if (b.Width < cs * 0.6 || b.Width > cs * 1.5 || b.Height < cs * 0.6 || b.Height > cs * 1.5) continue;
            xs.Add(b.X + b.Width / 2.0);
            ys.Add(b.Y + b.Height / 2.0);
            sizes.Add((b.Width + b.Height) / 2.0);
        }
        if (sizes.Count < 3) return null;
        sizes.Sort();
        double slot = sizes[sizes.Count / 2];
        List<double> cols = Cluster(xs, slot * 0.35), rows = Cluster(ys, slot * 0.35);

        ProbeGroup g = new ProbeGroup(rows.Count, cols.Count);
        g.Priority = 1;
        int half = (int)(slot / 2);
        Rectangle bounds = new Rectangle(0, 0, pb.Width, pb.Height);
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < cols.Count; c++)
            {
                Rectangle rc = Rectangle.Intersect(bounds, new Rectangle((int)cols[c] - half, (int)rows[r] - half, 2 * half, 2 * half));
                g.Rects[r, c] = Offset(rc, origin);
                g.Score[r, c] = SlotDetector.TintFraction(pb, rc, cfg.TintSensitivity);
                g.Active[r, c] = rc.Width > slot * 0.6 && rc.Height > slot * 0.6 && Occupied(pb, rc, cfg);
            }
        return g;
    }
}
