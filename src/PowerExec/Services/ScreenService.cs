using System.Drawing;
using PowerExec.Core.Config;
using PowerExec.Interop;
using Forms = System.Windows.Forms;

namespace PowerExec.Services;

public sealed record MonitorInfo(int Index, Rectangle Bounds, Rectangle WorkArea, bool IsPrimary)
{
    public double Scale => NativeMethods.ScaleForPoint(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2);
    public string DisplayName => IsPrimary ? $"Monitor {Index + 1} (Hauptmonitor)" : $"Monitor {Index + 1}";
}

/// <summary>Berechnet Position von Hover-Zone und Panel in physischen Pixeln.</summary>
public static class ScreenService
{
    public static IReadOnlyList<MonitorInfo> GetMonitors() =>
        Forms.Screen.AllScreens.Select((s, i) => new MonitorInfo(i, s.Bounds, s.WorkingArea, s.Primary)).ToList();

    public static IReadOnlyList<MonitorInfo> TriggerMonitors(AppSettings s)
    {
        var all = GetMonitors();
        var primary = all.FirstOrDefault(m => m.IsPrimary) ?? all[0];
        return s.MonitorMode switch
        {
            MonitorMode.All => all,
            MonitorMode.Specific => new[] { s.MonitorIndex >= 0 && s.MonitorIndex < all.Count ? all[s.MonitorIndex] : primary },
            _ => new[] { primary }
        };
    }

    public static MonitorInfo MonitorAtCursor()
    {
        NativeMethods.GetCursorPos(out var p);
        var all = GetMonitors();
        return all.FirstOrDefault(m => m.Bounds.Contains(p.X, p.Y)) ?? all.First(m => m.IsPrimary);
    }

    public static Rectangle HotZone(MonitorInfo m, AppSettings s)
    {
        var scale = m.Scale;
        int thickness = Math.Max(2, (int)Math.Round(3 * scale));
        bool vertical = s.Edge is ScreenEdge.Left or ScreenEdge.Right;
        var b = m.Bounds;
        var w = m.WorkArea;

        // Länge entlang der Kante
        int start = vertical ? w.Top : w.Left;
        int full = vertical ? w.Height : w.Width;
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
                length = Math.Min(full, (int)Math.Round(200 * scale));
                offset = (full - length) / 2;
                break;
        }

        return s.Edge switch
        {
            ScreenEdge.Left => new Rectangle(b.Left, start + offset, thickness, length),
            ScreenEdge.Top => new Rectangle(start + offset, b.Top, length, thickness),
            ScreenEdge.Bottom => new Rectangle(start + offset, b.Bottom - thickness, length, thickness),
            _ => new Rectangle(b.Right - thickness, start + offset, thickness, length)
        };
    }

    public static Rectangle PanelRect(MonitorInfo m, AppSettings s)
    {
        var scale = m.Scale;
        var w = m.WorkArea;
        int margin = (int)Math.Round(8 * scale);
        int width = Math.Min(w.Width - 2 * margin, (int)Math.Round(s.PanelWidth * scale));

        switch (s.Edge)
        {
            case ScreenEdge.Left:
                return new Rectangle(w.Left + margin, w.Top + margin, width, w.Height - 2 * margin);
            case ScreenEdge.Top:
            case ScreenEdge.Bottom:
            {
                int height = Math.Min(w.Height - 2 * margin, (int)Math.Round(Math.Max(480, w.Height * 0.7 / scale) * scale));
                var zone = HotZone(m, s);
                int x = zone.Left + zone.Width / 2 - width / 2;
                x = Math.Clamp(x, w.Left + margin, w.Right - margin - width);
                int y = s.Edge == ScreenEdge.Top ? w.Top + margin : w.Bottom - margin - height;
                return new Rectangle(x, y, width, height);
            }
            default:
                return new Rectangle(w.Right - margin - width, w.Top + margin, width, w.Height - 2 * margin);
        }
    }

    public static void Place(IntPtr hwnd, Rectangle r) =>
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, NativeMethods.SWP_NOACTIVATE);

    public static bool CursorInside(Rectangle r, int tolerance = 0)
    {
        NativeMethods.GetCursorPos(out var p);
        return p.X >= r.Left - tolerance && p.X < r.Right + tolerance && p.Y >= r.Top - tolerance && p.Y < r.Bottom + tolerance;
    }
}
