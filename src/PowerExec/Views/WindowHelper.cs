using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PowerExec.Services;

namespace PowerExec.Views;

internal static class WindowHelper
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>Titelleiste passend zum Farbschema (Windows 10 20H1+ / Windows 11).</summary>
    public static void ApplyTitleBar(Window w)
    {
        void Apply()
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                int dark = ThemeService.IsDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
            catch { }
        }

        if (new WindowInteropHelper(w).Handle != IntPtr.Zero) Apply();
        else w.SourceInitialized += (_, _) => Apply();
    }
}
