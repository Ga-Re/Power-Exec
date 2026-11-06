using System.Runtime.InteropServices;

namespace PowerExec.WinUI.Spike;

/// <summary>Win32-Aufrufe des Prototyps.</summary>
internal static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    public const uint VK_SPACE = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Effektive DPI des Monitors an diesem Bildschirmpunkt (96 = 100 %).</summary>
    public static uint GetDpiForMonitorAt(int x, int y)
    {
        var mon = MonitorFromPoint(new POINT { X = x, Y = y }, 2);
        return GetDpiForMonitor(mon, 0, out var dx, out _) == 0 && dx > 0 ? dx : 96;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    /// <summary>Nur dieses Rechteck des Fensters zeichnen (Fensterkoordinaten); Windows übernimmt die Region.</summary>
    public static void SetWindowRegion(IntPtr hwnd, int left, int top, int right, int bottom) =>
        SetWindowRgn(hwnd, CreateRectRgn(left, top, right, bottom), true);

    /// <summary>Region entfernen: ganzes Fenster sichtbar (DWM-Ecken und Hintergrund wieder normal).</summary>
    public static void ClearWindowRegion(IntPtr hwnd) => SetWindowRgn(hwnd, IntPtr.Zero, true);

    /// <summary>QUNS_*: 2 = beschäftigt (Vollbild), 3 = Direct3D-Vollbild, 4 = Präsentationsmodus.</summary>
    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    /// <summary>
    /// Läuft gerade ein Vollbild-Programm (Video, Präsentation, Spiel, RDP)? Wie die WPF-App: Windows-Status plus
    /// Vordergrundfenster, das seinen Monitor vollständig bedeckt (ohne Desktop und Shell).
    /// </summary>
    public static bool IsFullscreenAppActive()
    {
        if (SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4) return true;
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == GetShellWindow() || fg == GetDesktopWindow()) return false;
        var cls = new System.Text.StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        if (cls.ToString() is "WorkerW" or "Progman") return false;
        if (!GetWindowRect(fg, out var r)) return false;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(fg, 2), ref info)) return false;
        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }
}
