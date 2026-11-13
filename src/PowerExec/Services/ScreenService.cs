using PowerExec.Core.Localization;
using System.Drawing;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Desktop.Interop;
using Forms = System.Windows.Forms;

namespace PowerExec.Desktop.Services;

public sealed record MonitorInfo(int Index, Rectangle Bounds, Rectangle WorkArea, bool IsPrimary)
{
    public double Scale => NativeMethods.ScaleForPoint(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2);
    public string DisplayName => IsPrimary ? L.F("Monitor {0} (primary)", Index + 1) : L.F("Monitor {0}", Index + 1);
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

    /// <summary>
    /// Aktueller Stand eines Monitors (Bereich, Arbeitsbereich). Ein <see cref="MonitorInfo"/> ist eine Momentaufnahme –
    /// wer ihn länger hält (Hover-Zone, Panel), fragt vor dem Platzieren neu. Gibt es den Monitor nicht mehr, gilt der
    /// erste Monitor des Panels.
    /// </summary>
    public static MonitorInfo Refresh(MonitorInfo monitor, PanelConfig s) =>
        GetMonitors().FirstOrDefault(m => m.Index == monitor.Index) ?? TriggerMonitors(s)[0];

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
