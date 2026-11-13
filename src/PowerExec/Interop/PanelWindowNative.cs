using System.Runtime.InteropServices;

namespace PowerExec.Desktop.Interop;

/// <summary>
/// Fenster-Hilfen für das Panel: Fensterbereich (abgerundet, beim Gleiten beschnitten) und die Vorbereitung eines
/// Fensters ohne Transparenz-Schicht, hinter dem Windows zeichnet (runde Ecken von Windows 11, kein farbiger Rand).
/// Aero-Blur, Acrylic und Mica gibt es seit 0.21 nicht mehr.
/// </summary>
internal static partial class PanelWindowNative
{
    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int widthEllipse, int heightEllipse);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("gdi32.dll")]
    private static partial int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr obj);

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
                if (empty != IntPtr.Zero) SetWindowRgn(hwnd, empty, false); // ohne synchrones Neuzeichnen (pro Bild beim Gleiten)
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
            SetWindowRgn(hwnd, rgn, false); // Windows übernimmt rgn; false: kein synchrones GDI-Neuzeichnen pro Bild beim Gleiten
        }
        catch (EntryPointNotFoundException) { } // Fensterbereich ist Optik: ohne die Funktion bleibt das Fenster unbeschnitten
        catch (DllNotFoundException) { }
    }

    /// <summary>Fensterbereich wieder auf das ganze Fenster setzen.</summary>
    public static void ClearRegion(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try { SetWindowRgn(hwnd, IntPtr.Zero, false); } // ohne synchrones Neuzeichnen
        catch (EntryPointNotFoundException) { }       // siehe SetClippedRoundedRegion
    }

    /// <summary>Ein-/Ausblend-Übergänge von Windows abschalten: das Panel gleitet selbst, beim Neuaufbau soll nichts aufblitzen.</summary>
    public static void DisableTransitions(IntPtr hwnd) =>
        NativeMethods.SetDwmAttribute(hwnd, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, 1);

    /// <summary>
    /// Fenster für Windows unsichtbar machen, ohne es zu verstecken (Hintergrund, Composition und Kindfenster bleiben
    /// aufgebaut). Für das eingefahrene Panel: Windows zeichnet Schatten und Rand auch bei leerem Fensterbereich.
    /// </summary>
    public static void SetCloaked(IntPtr hwnd, bool cloaked) =>
        NativeMethods.SetDwmAttribute(hwnd, NativeMethods.DWMWA_CLOAK, cloaked ? 1 : 0);

    /// <summary>
    /// Fenster für einen Hintergrund von Windows vorbereiten: Rahmen in den Inhalt erweitern (Windows zeichnet hinter
    /// den durchsichtigen Teilen), Ecken wie Windows 11 runden, keinen farbigen Rand.
    /// </summary>
    public static void PrepareSystemWindow(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var m = new NativeMethods.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref m);
        }
        catch (DllNotFoundException) { } // ohne DWM bleibt der Rahmen, wie er ist
        NativeMethods.SetDarkTitleBar(hwnd, dark);
        NativeMethods.SetDwmAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, NativeMethods.DWMWCP_ROUND);
        NativeMethods.SetDwmAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, NativeMethods.DWMWA_COLOR_NONE);
    }

    /// <summary>Hintergrund eines Systemfensters wie bei einem aktiven Fenster zeichnen lassen.</summary>
    public static void ShowAsActive(IntPtr hwnd) =>
        NativeMethods.SendMessage(hwnd, NativeMethods.WM_NCACTIVATE, 1, IntPtr.Zero);
}
