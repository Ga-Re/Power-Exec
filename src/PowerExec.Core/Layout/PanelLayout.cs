using System.Drawing;
using PowerExec.Core.Config;

namespace PowerExec.Core.Layout;

/// <summary>
/// Berechnet Hover-Zone und Panel in physischen Pixeln.
/// Größen aus den Einstellungen sind DIP (96 dpi) und werden mit <c>scale</c> umgerechnet.
/// </summary>
public static class PanelLayout
{
    public static Rectangle HotZone(Rectangle bounds, Rectangle workArea, PanelConfig s, double scale)
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

    /// <summary>Dicke des Mini-Docks und Platz je Symbol (DIP).</summary>
    public const int DockThickness = 40, DockSlot = 36, DockPadding = 8;

    /// <summary>
    /// Mini-Dock: schmale Leiste am Rand des Arbeitsbereichs (nicht über der Taskleiste), mittig,
    /// lang genug für <paramref name="slots"/> Symbole.
    /// </summary>
    public static Rectangle Dock(Rectangle workArea, PanelConfig s, double scale, int slots)
    {
        int thickness = (int)Math.Round(DockThickness * scale);
        bool vertical = s.Edge is ScreenEdge.Left or ScreenEdge.Right;
        int full = vertical ? workArea.Height : workArea.Width;
        int length = Math.Min(full, (int)Math.Round((Math.Max(1, slots) * DockSlot + 2 * DockPadding) * scale));
        int start = (vertical ? workArea.Top : workArea.Left) + (full - length) / 2;
        return s.Edge switch
        {
            ScreenEdge.Left => new Rectangle(workArea.Left, start, thickness, length),
            ScreenEdge.Top => new Rectangle(start, workArea.Top, length, thickness),
            ScreenEdge.Bottom => new Rectangle(start, workArea.Bottom - thickness, length, thickness),
            _ => new Rectangle(workArea.Right - thickness, start, thickness, length)
        };
    }

    public static Rectangle Panel(Rectangle bounds, Rectangle workArea, PanelConfig s, double scale)
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

    /// <summary>
    /// Fenster beim Ein-/Ausfahren (Weichzeichner-Varianten): <paramref name="reveal"/> 0 = eingefahren, 1 = ganz da.
    /// <paramref name="move"/>: in voller Größe vom Rand hereinschieben (flüssig); sonst vom Rand aus wachsen
    /// (nötig, wenn hinter dem Rand ein weiterer Bildschirm liegt).
    /// </summary>
    public static Rectangle Reveal(Rectangle full, ScreenEdge edge, double reveal, bool move)
    {
        var f = Math.Clamp(reveal, 0, 1);
        int visW = Math.Max(1, (int)Math.Round(full.Width * f));
        int visH = Math.Max(1, (int)Math.Round(full.Height * f));
        if (move)
        {
            return edge switch
            {
                ScreenEdge.Right => new Rectangle(full.Right - visW, full.Top, full.Width, full.Height),
                ScreenEdge.Left => new Rectangle(full.Left - (full.Width - visW), full.Top, full.Width, full.Height),
                ScreenEdge.Bottom => new Rectangle(full.Left, full.Bottom - visH, full.Width, full.Height),
                _ => new Rectangle(full.Left, full.Top - (full.Height - visH), full.Width, full.Height)
            };
        }
        return edge switch
        {
            ScreenEdge.Right => new Rectangle(full.Right - visW, full.Top, visW, full.Height),
            ScreenEdge.Left => new Rectangle(full.Left, full.Top, visW, full.Height),
            ScreenEdge.Bottom => new Rectangle(full.Left, full.Bottom - visH, full.Width, visH),
            _ => new Rectangle(full.Left, full.Top, full.Width, visH)
        };
    }

    /// <summary>Liegt hinter dem Rand des Panels ein anderer Bildschirm? Dann darf das Fenster dort nicht auftauchen.</summary>
    public static bool HasMonitorBeyond(Rectangle full, ScreenEdge edge, IEnumerable<Rectangle> otherMonitors)
    {
        var beyond = edge switch
        {
            ScreenEdge.Right => new Rectangle(full.Right, full.Top, full.Width, full.Height),
            ScreenEdge.Left => new Rectangle(full.Left - full.Width, full.Top, full.Width, full.Height),
            ScreenEdge.Bottom => new Rectangle(full.Left, full.Bottom, full.Width, full.Height),
            _ => new Rectangle(full.Left, full.Top - full.Height, full.Width, full.Height)
        };
        return otherMonitors.Any(m => m.IntersectsWith(beyond));
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
