using System.Drawing;

namespace PowerExec.Core.Layout;

/// <summary>Erkennt, ob ein Fenster einen Monitor vollständig bedeckt (Vollbild-Video, Präsentation, Spiel, RDP).</summary>
public static class Fullscreen
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow"
    };

    public static bool IsFullscreen(Rectangle window, Rectangle monitorBounds, string? windowClass)
    {
        if (windowClass != null && ShellClasses.Contains(windowClass)) return false;
        if (window.Width <= 0 || window.Height <= 0) return false;
        const int tolerance = 2;
        return window.Left <= monitorBounds.Left + tolerance &&
               window.Top <= monitorBounds.Top + tolerance &&
               window.Right >= monitorBounds.Right - tolerance &&
               window.Bottom >= monitorBounds.Bottom - tolerance;
    }
}
