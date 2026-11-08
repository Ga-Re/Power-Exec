using System.Runtime.InteropServices;

namespace PowerExec.Interop;

/// <summary>
/// Fenster-Hilfen für das Panel: Fensterbereich (abgerundet, beim Gleiten beschnitten) und die Vorbereitung eines
/// Fensters ohne Transparenz-Schicht, hinter dem Windows zeichnet (runde Ecken von Windows 11, kein farbiger Rand).
/// Aero-Blur, Acrylic und Mica gibt es seit 0.21 nicht mehr.
/// </summary>
internal static class PanelWindowNative
{
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    private const int RGN_AND = 1;

    /// <summary>
    /// Fensterbereich: abgerundetes Fenster (<paramref name="width"/>×<paramref name="height"/>), beschnitten auf das
    /// Rechteck clip (Pixel relativ zum Fenster). Ein leeres Rechteck blendet das Fenster ganz aus.
    /// </summary>
    public static void SetClippedRoundedRegion(IntPtr hwnd, int width, int height, int radius, int clipLeft, int clipTop, int clipRight, int clipBottom)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            if (clipRight <= clipLeft || clipBottom <= clipTop)
            {
                var empty = CreateRectRgn(0, 0, 0, 0);
                if (empty != IntPtr.Zero) SetWindowRgn(hwnd, empty, true);
                return;
            }
            var rgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
            if (rgn == IntPtr.Zero) return;
            if (clipLeft > 0 || clipTop > 0 || clipRight < width || clipBottom < height)
            {
                var clip = CreateRectRgn(clipLeft, clipTop, clipRight, clipBottom);
                if (clip != IntPtr.Zero)
                {
                    CombineRgn(rgn, rgn, clip, RGN_AND);
                    DeleteObject(clip);
                }
            }
            SetWindowRgn(hwnd, rgn, true); // Windows übernimmt rgn
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;
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

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
