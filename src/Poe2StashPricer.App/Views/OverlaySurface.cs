using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Poe2StashPricer.Session;

namespace Poe2StashPricer.App.Views;

/// <summary>
/// Draws the overlay: a price over each item, and one line above the stash. Everything arrives in screen
/// pixels, so it is moved to the window's origin and divided by the scaling on the way in.
/// </summary>
internal class OverlaySurface : Control
{
    private static readonly Typeface Face = new Typeface("Inter, sans-serif", FontStyle.Normal, FontWeight.SemiBold);

    private static readonly IBrush Shadow = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0));
    private static readonly IBrush Panel = new SolidColorBrush(Color.FromArgb(190, 12, 12, 16));
    private static readonly IBrush HeaderInk = new SolidColorBrush(Color.FromRgb(240, 226, 188));
    private static readonly IBrush NoteInk = new SolidColorBrush(Color.FromRgb(255, 214, 122));
    private static readonly IBrush OutlineInk = new SolidColorBrush(Color.FromArgb(200, 120, 230, 120));
    private static readonly IBrush RegionInk = new SolidColorBrush(Color.FromArgb(220, 255, 90, 60));

    private OverlayContent _content = OverlayContent.Hidden;
    private PixelPoint _origin;
    private double _scale = 1;
    private int _headerHeight;

    public void Show(OverlayContent content, PixelPoint origin, double scale, int headerHeight)
    {
        _content = content;
        _origin = origin;
        _scale = scale <= 0 ? 1 : scale;
        _headerHeight = headerHeight;
        InvalidateVisual();
    }

    private static IBrush InkFor(ValueTier tier)
    {
        switch (tier)
        {
            case ValueTier.High: return new SolidColorBrush(Color.FromRgb(255, 209, 102));   // a divine or more
            case ValueTier.Mid: return Brushes.White;
            default: return new SolidColorBrush(Color.FromRgb(184, 184, 184));
        }
    }

    /// <summary>Screen pixels to where they land inside this window.</summary>
    private Rect ToLocal(System.Drawing.Rectangle r)
    {
        return new Rect((r.X - _origin.X) / _scale, (r.Y - _origin.Y) / _scale, r.Width / _scale, r.Height / _scale);
    }

    public override void Render(DrawingContext context)
    {
        OverlayContent content = _content;
        if (content.Kind == OverlayKind.Hidden) return;

        double headerHeight = _headerHeight / _scale;
        if (content.Header != null)
        {
            FormattedText header = Text(content.Header, 13, HeaderInk);
            double width = Math.Min(Bounds.Width, header.Width + 16);
            context.FillRectangle(Panel, new Rect(0, 0, width, headerHeight));
            context.DrawText(header, new Point(8, Math.Max(0, (headerHeight - header.Height) / 2)));
        }
        if (content.Note != null)
        {
            FormattedText note = Text(content.Note, 12, NoteInk);
            context.FillRectangle(Panel, new Rect(0, headerHeight, Math.Min(Bounds.Width, note.Width + 16), note.Height + 4));
            context.DrawText(note, new Point(8, headerHeight + 2));
        }

        foreach (OverlayLabel label in content.Labels)
        {
            Rect r = ToLocal(label.Bounds);
            if (label.Outline)
            {
                IBrush ink = label.Text == null && r.Width > Bounds.Width * 0.8 ? RegionInk : OutlineInk;
                context.DrawRectangle(null, new Pen(ink, 1.5), r);
                continue;
            }
            if (label.Text == null) continue;

            FormattedText value = Text(label.Text, 12, InkFor(label.Tier));
            // Along the bottom of the slot, where an item's own artwork is darkest.
            double x = r.X + Math.Max(0, (r.Width - value.Width) / 2);
            double y = r.Bottom - value.Height - 1;
            context.FillRectangle(Panel, new Rect(x - 3, y - 1, value.Width + 6, value.Height + 2));
            // A dark pass behind the text keeps it readable over a bright icon.
            context.DrawText(Text(label.Text, 12, Shadow), new Point(x + 1, y + 1));
            context.DrawText(value, new Point(x, y));
        }
    }

    private FormattedText Text(string text, double size, IBrush ink)
    {
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, ink);
    }
}
