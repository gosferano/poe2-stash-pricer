using System;
using System.Drawing;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.PixelFormats;

namespace Poe2StashPricer.Detection
{
    /// <summary>
    /// Copy of a picture's pixels (BGRA) for fast repeated reads. Every pixel read in the app goes through
    /// here: <c>Px[o]</c> is blue, <c>Px[o + 1]</c> green, <c>Px[o + 2]</c> red, with
    /// <c>o = y * Stride + x * 4</c>.
    /// </summary>
    public class PixelBuffer
    {
        public readonly int Width, Height, Stride;
        public readonly byte[] Px;

        /// <summary>
        /// Takes over rows of raw BGRA bytes, as a screen capture hands them over (X11 ZPixmap at depth 24
        /// or 32 and Wayland shm XRGB8888/ARGB8888 all hold BGRA in memory, in that order).
        /// </summary>
        public PixelBuffer(byte[] bgra, int width, int height, int stride)
        {
            if (bgra == null) throw new ArgumentNullException("bgra");
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("width");
            if (stride < width * 4) throw new ArgumentOutOfRangeException("stride", "a row of " + width + " BGRA pixels needs " + width * 4 + " bytes");
            if (bgra.Length < stride * (long)height) throw new ArgumentException("fewer bytes than " + height + " rows of " + stride, "bgra");
            Width = width;
            Height = height;
            Stride = stride;
            Px = bgra;
        }

        PixelBuffer(int w, int h)
        {
            Width = w;
            Height = h;
            Stride = w * 4;
            Px = new byte[Stride * h];
        }

        public static PixelBuffer FromImage(SixLabors.ImageSharp.Image<Bgra32> img)
        {
            PixelBuffer pb = new PixelBuffer(img.Width, img.Height);
            img.CopyPixelDataTo(pb.Px);
            return pb;
        }

        public PixelBuffer Crop(Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, Width, Height));
            PixelBuffer c = new PixelBuffer(r.Width, r.Height);
            for (int y = 0; y < r.Height; y++)
                Buffer.BlockCopy(Px, (r.Y + y) * Stride + r.X * 4, c.Px, y * c.Stride, r.Width * 4);
            return c;
        }

        /// <summary>The picture as an image, for saving it to a file (tests, diagnostics).</summary>
        public SixLabors.ImageSharp.Image<Bgra32> ToImage()
        {
            SixLabors.ImageSharp.Image<Bgra32> img = new SixLabors.ImageSharp.Image<Bgra32>(Width, Height);
            img.ProcessPixelRows(rows =>
            {
                for (int y = 0; y < Height; y++)
                    new ReadOnlySpan<byte>(Px, y * Stride, Width * 4).CopyTo(MemoryMarshal.AsBytes(rows.GetRowSpan(y)));
            });
            return img;
        }
    }
}
