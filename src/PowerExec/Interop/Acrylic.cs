using System.Runtime.InteropServices;

namespace PowerExec.Interop;

/// <summary>
/// Weichgezeichneter Hintergrund hinter einem Fenster (Aero-Blur bzw. Acrylic) über SetWindowCompositionAttribute,
/// dazu Systemhintergrund (Acrylic/Mica) und runde Ecken von Windows 11 über DWM.
/// Die Tönung liefert der halbtransparente Panelhintergrund.
/// </summary>
internal static class Acrylic
{
    private enum AccentState { Disabled = 0, BlurBehind = 3, AcrylicBlurBehind = 4 }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor; // AABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute; // 19 = WCA_ACCENT_POLICY
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    public static bool IsAcrylicSupported => Environment.OSVersion.Version.Build >= 17134;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    /// <summary>
    /// Fensterbereich auf ein abgerundetes Rechteck beschränken (Pixel relativ zum Fenster). Die Weichzeichnung
    /// hinter dem Fenster folgt diesem Bereich – so passt sie genau zur Form des Panels, auch während es einfährt.
    /// Ein leeres Rechteck blendet sie ganz aus. Windows übernimmt den Bereich (nicht selbst freigeben).
    /// </summary>
    public static void SetRoundedRegion(IntPtr hwnd, int left, int top, int right, int bottom, int radius)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var rgn = right <= left || bottom <= top
                ? CreateRectRgn(0, 0, 0, 0)
                : CreateRoundRectRgn(left, top, right + 1, bottom + 1, radius * 2, radius * 2);
            if (rgn != IntPtr.Zero) SetWindowRgn(hwnd, rgn, true);
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
    }

    /// <summary>Fensterbereich wieder auf das ganze Fenster setzen.</summary>
    public static void ClearRegion(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try { SetWindowRgn(hwnd, IntPtr.Zero, true); }
        catch (EntryPointNotFoundException) { }
    }

    /// <param name="acrylic">true = Acrylic (Windows 10 1803+), false = klassischer Weichzeichner (Aero-Blur).</param>
    /// <returns>false, wenn Windows die Funktion nicht kennt.</returns>
    public static bool Apply(IntPtr hwnd, bool enabled, bool acrylic = false)
    {
        if (hwnd == IntPtr.Zero) return false;
        var policy = new AccentPolicy
        {
            AccentState = !enabled ? AccentState.Disabled : acrylic && IsAcrylicSupported ? AccentState.AcrylicBlurBehind : AccentState.BlurBehind,
            GradientColor = 0x01000000 // minimale eigene Tönung, Farbe kommt vom Panel (geglättet von WPF gezeichnet)
        };
        var size = Marshal.SizeOf(policy);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = 19, Data = ptr, SizeOfData = size };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch (EntryPointNotFoundException) { return false; }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34, DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2, DWMSBT_NONE = 1, DWMSBT_MAINWINDOW = 2, DWMSBT_TRANSIENTWINDOW = 3;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    private static void Set(IntPtr hwnd, int attr, int value)
    {
        try { DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int)); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>
    /// Fenster für einen Hintergrund von Windows vorbereiten: Rahmen in den Inhalt erweitern (Windows zeichnet hinter
    /// den durchsichtigen Teilen), Ecken wie Windows 11 runden, keinen farbigen Rand.
    /// </summary>
    public static void PrepareSystemWindow(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var m = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref m);
        }
        catch (DllNotFoundException) { }
        Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
        Set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        Set(hwnd, DWMWA_BORDER_COLOR, DWMWA_COLOR_NONE);
    }

    /// <summary>Systemhintergrund von Windows 11 22H2+: Acrylic (wie Menüs) oder Mica; false = keiner.</summary>
    public static void SetSystemBackdrop(IntPtr hwnd, bool? acrylic) =>
        Set(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, acrylic switch { true => DWMSBT_TRANSIENTWINDOW, false => DWMSBT_MAINWINDOW, null => DWMSBT_NONE });

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
