using System.Runtime.InteropServices;

namespace PowerExec.Interop;

/// <summary>
/// Weichgezeichneter Hintergrund hinter einem Fenster (Acrylic ab Windows 10 1803, sonst Blur) über
/// SetWindowCompositionAttribute. Die Tönung liefert der halbtransparente Panelhintergrund.
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

    /// <returns>false, wenn Windows die Funktion nicht kennt.</returns>
    public static bool Apply(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero) return false;
        var policy = new AccentPolicy
        {
            AccentState = !enabled ? AccentState.Disabled : IsAcrylicSupported ? AccentState.AcrylicBlurBehind : AccentState.BlurBehind,
            GradientColor = 0x01000000 // minimale eigene Tönung, Farbe kommt vom Panel
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
}
