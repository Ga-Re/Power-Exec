using System.Drawing;
using PowerExec.Core.Config;

namespace PowerExec.Core.Layout;

/// <summary>
/// Berechnet Hover-Zone und Panel in physischen Pixeln.
/// Größen aus den Einstellungen sind DIP (96 dpi) und werden mit <c>scale</c> umgerechnet.
/// </summary>
public static class PanelLayout
{
    public static Rectangle HotZone(Rectangle bounds, Rectangle workArea, AppSettings s, double scale)
    {
        int thickness = Math.Max(1, (int)Math.Round(s.HotZoneThickness * scale));
        bool vertical = s.Edge is ScreenEdge.Left or ScreenEdge.Right;

        int start = vertical ? workArea.Top : workArea.Left;
        int full = vertical ? workArea.Height : workArea.Width;
        int length, offset;
        switch (s.HotZone)
        {
            case HotZoneMode.Full:
                length = full; offset = 0;
                break;
            case HotZoneMode.FirstHalf:
                length = full / 2; offset = 0;
                break;
            default:
                length = Math.Min(full, (int)Math.Round(s.HotZoneLength * scale));
                offset = (full - length) / 2;
                break;
        }

        return s.Edge switch
        {
            ScreenEdge.Left => new Rectangle(bounds.Left, start + offset, thickness, length),
            ScreenEdge.Top => new Rectangle(start + offset, bounds.Top, length, thickness),
            ScreenEdge.Bottom => new Rectangle(start + offset, bounds.Bottom - thickness, length, thickness),
            _ => new Rectangle(bounds.Right - thickness, start + offset, thickness, length)
        };
    }

    public static Rectangle Panel(Rectangle bounds, Rectangle workArea, AppSettings s, double scale)
    {
        var w = workArea;
        int margin = (int)Math.Round(8 * scale);
        int width = Math.Min(w.Width - 2 * margin, (int)Math.Round(s.PanelWidth * scale));
        int availableHeight = w.Height - 2 * margin;
        int minHeight = Math.Min(availableHeight, (int)Math.Round(240 * scale));
        int height = Math.Max(minHeight, (int)Math.Round(availableHeight * Math.Clamp(s.PanelHeightPercent, 30, 100) / 100.0));
        var zone = HotZone(bounds, workArea, s, scale);

        if (s.Edge is ScreenEdge.Left or ScreenEdge.Right)
        {
            int top = Align(s.PanelAlignment, w.Top + margin, availableHeight, height, zone.Top + zone.Height / 2);
            int x = s.Edge == ScreenEdge.Left ? w.Left + margin : w.Right - margin - width;
            return new Rectangle(x, top, width, height);
        }

        int availableWidth = w.Width - 2 * margin;
        int left = Align(s.PanelAlignment, w.Left + margin, availableWidth, width, zone.Left + zone.Width / 2);
        int y = s.Edge == ScreenEdge.Top ? w.Top + margin : w.Bottom - margin - height;
        return new Rectangle(left, y, width, height);
    }

    /// <summary>Position entlang der Kante: an der Hover-Zone zentriert, am Anfang, mittig oder am Ende.</summary>
    private static int Align(PanelAlignment alignment, int start, int available, int size, int zoneCenter)
    {
        int pos = alignment switch
        {
            PanelAlignment.Start => start,
            PanelAlignment.End => start + available - size,
            PanelAlignment.Center => start + (available - size) / 2,
            _ => zoneCenter - size / 2
        };
        return Math.Clamp(pos, start, start + Math.Max(0, available - size));
    }
}
