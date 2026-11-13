using System.Runtime.InteropServices;

namespace PowerExec.Desktop.Interop;

/// <summary>
/// Win32-Aufrufe und -Konstanten der WPF-App an einer Stelle. Fenster und Dienste rufen die benannten Methoden auf;
/// <c>LibraryImport</c> erzeugt die Übergänge zur Übersetzungszeit.
/// </summary>
internal static partial class NativeMethods
{
    // ── Fenstermeldungen ─────────────────────────────────────────────────────────────

    public const int WM_SIZE = 0x0005;
    public const int WM_NCACTIVATE = 0x0086;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_HOTKEY = 0x0312;

    // ── Fensterstile ─────────────────────────────────────────────────────────────────

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_CLIPSIBLINGS = 0x04000000;
    public const long WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    // ── SetWindowPos ─────────────────────────────────────────────────────────────────

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    /// <summary>Fensterinhalt beim Verschieben nicht kopieren (DWM setzt ohnehin neu zusammen).</summary>
    public const uint SWP_NOCOPYBITS = 0x0100;
    /// <summary>Kein WM_WINDOWPOSCHANGING (spart pro Bild einen Durchlauf durch WPF beim Gleiten).</summary>
    public const uint SWP_NOSENDCHANGING = 0x0400;

    // ── Tastenkürzel ─────────────────────────────────────────────────────────────────

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // ── DWM-Fensterattribute ─────────────────────────────────────────────────────────

    public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    public const int DWMWA_CLOAK = 13;
    public const int DWMWA_USE_HOSTBACKDROPBRUSH = 17;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Margins { public int Left, Right, Top, Bottom; }

    // ── user32 ───────────────────────────────────────────────────────────────────────

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassName(IntPtr hWnd, [Out] char[] className, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr hIcon);

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, [Out] char[] value, uint winIni);

    // ── shcore, shell32, dwmapi ──────────────────────────────────────────────────────

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    // ── Benannte Hilfen ──────────────────────────────────────────────────────────────

    /// <summary>
    /// DWM-Attribut eines Fensters setzen. Rein optisch (Titelleiste, Ecken, Übergänge): fehlt die Funktion auf einem
    /// älteren Windows oder lehnt Windows das Attribut ab, bleibt das Fenster, wie es ist.
    /// </summary>
    /// <returns>HRESULT (0 = gesetzt) bzw. -1, wenn die Funktion fehlt.</returns>
    public static int SetDwmAttribute(IntPtr hwnd, int attribute, int value)
    {
        if (hwnd == IntPtr.Zero) return -1;
        try { return DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)); }
        catch (DllNotFoundException) { return -1; }
        catch (EntryPointNotFoundException) { return -1; }
    }

    /// <summary>Titelleiste eines Fensters hell oder dunkel (Windows 10 20H1+ / Windows 11).</summary>
    public static void SetDarkTitleBar(IntPtr hwnd, bool dark) => SetDwmAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);

    /// <summary>Symbol-Handle der Shell zu einem Pfad (Aufrufer gibt es mit <see cref="DestroyIcon"/> frei); Zero = keins.</summary>
    public static unsafe IntPtr GetShellIcon(string path, uint fileAttributes, uint flags)
    {
        var info = new SHFILEINFO();
        var result = SHGetFileInfo(path, fileAttributes, ref info, (uint)sizeof(SHFILEINFO), flags);
        return result == IntPtr.Zero ? IntPtr.Zero : info.hIcon;
    }

    /// <summary>Pfad des Desktop-Hintergrundbilds (leer, wenn keins gesetzt ist).</summary>
    public static string DesktopWallpaperPath()
    {
        const uint SPI_GETDESKWALLPAPER = 0x0073;
        var buffer = new char[520];
        if (!SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)buffer.Length, buffer, 0)) return string.Empty;
        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }

    /// <summary>Strg+V an das Vordergrundfenster senden.</summary>
    public static void SendCtrlV()
    {
        const byte VK_CONTROL = 0x11, VK_V = 0x56;
        const uint KEYUP = 0x0002;
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYUP, UIntPtr.Zero);
    }

    public static string ClassNameOf(IntPtr hwnd)
    {
        var buffer = new char[256];
        var length = GetClassName(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Clamp(length, 0, buffer.Length));
    }

    /// <summary>Systemmenü sowie Minimieren/Maximieren entfernen – Windows zeichnet dann keine Fensterknöpfe in den Rahmen.</summary>
    public static void RemoveCaptionButtons(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style & ~(WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)));
    }

    /// <summary>Fenster aus Alt+Tab ausblenden und optional ohne Aktivierung anzeigen.</summary>
    public static void MakeToolWindow(IntPtr hwnd, bool noActivate)
    {
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TOOLWINDOW;
        if (noActivate) ex |= WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>Skalierung des Monitors an einem Punkt (1 = 96 dpi).</summary>
    public static double ScaleForPoint(int x, int y)
    {
        try
        {
            var mon = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
            if (GetDpiForMonitor(mon, 0, out var dx, out _) == 0 && dx > 0) return dx / 96.0;
        }
        catch (DllNotFoundException) { }       // shcore fehlt (vor Windows 8.1): ohne Skalierung rechnen
        catch (EntryPointNotFoundException) { }
        return 1.0;
    }
}
