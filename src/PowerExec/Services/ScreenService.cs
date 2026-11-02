using System.Drawing;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;
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

    public static IReadOnlyList<MonitorInfo> TriggerMonitors(PanelConfig s)
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

    public static Rectangle HotZone(MonitorInfo m, PanelConfig s) =>
        PanelLayout.HotZone(m.Bounds, m.WorkArea, s, m.Scale);

    public static Rectangle PanelRect(MonitorInfo m, PanelConfig s) =>
        PanelLayout.Panel(m.Bounds, m.WorkArea, s, m.Scale);

    public static void Place(IntPtr hwnd, Rectangle r) =>
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, NativeMethods.SWP_NOACTIVATE);

    public static bool CursorInside(Rectangle r, int tolerance = 0)
    {
        NativeMethods.GetCursorPos(out var p);
        return p.X >= r.Left - tolerance && p.X < r.Right + tolerance && p.Y >= r.Top - tolerance && p.Y < r.Bottom + tolerance;
    }
}
