using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using PowerExec.Core.Config;
using PowerExec.Services;

namespace PowerExec.Views;

internal static class WindowHelper
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2; // Mica

    /// <summary>Aktuelle Einstellungen (Mica, Größe); setzt der AppController beim Start.</summary>
    public static Func<AppSettings>? Settings { get; set; }

    /// <summary>Mica gibt es ab Windows 11 22H2 (Build 22621).</summary>
    public static bool IsMicaSupported => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// Titelleiste passend zum Farbschema (Windows 10 20H1+ / Windows 11), auf Wunsch Mica als Hintergrund
    /// und die eingestellte Größe von Schrift und Bedienelementen.
    /// </summary>
    public static void ApplyTitleBar(Window w)
    {
        ApplyScale(w);
        void Apply()
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                int dark = ThemeService.IsDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                if (Settings?.Invoke().WindowBackdrop == WindowBackdrop.Mica && IsMicaSupported && !ThemeService.IsHighContrast && !w.AllowsTransparency)
                    ApplyMica(w, hwnd);
            }
            catch { }
        }

        if (new WindowInteropHelper(w).Handle != IntPtr.Zero) Apply();
        else w.SourceInitialized += (_, _) => Apply();
    }

    private static void ApplyMica(Window w, IntPtr hwnd)
    {
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target }) target.BackgroundColor = Colors.Transparent;
        // Leicht getönt statt ganz durchsichtig, damit Text auf jedem Hintergrund lesbar bleibt
        var bg = (Application.Current.Resources["Bg.Window"] as SolidColorBrush)?.Color ?? Colors.Black;
        w.Background = new SolidColorBrush(Color.FromArgb(0x99, bg.R, bg.G, bg.B));
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int type = DWMSBT_MAINWINDOW;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
    }

    /// <summary>Größe von Schrift und Bedienelementen (80–150 %): Inhalt skalieren, Fenster entsprechend größer.</summary>
    public static void ApplyScale(Window w)
    {
        var percent = Settings?.Invoke().UiScalePercent ?? 100;
        if (percent == 100 || w.Content is not FrameworkElement content) return;
        var f = percent / 100.0;
        content.LayoutTransform = new ScaleTransform(f, f);
        if (!double.IsNaN(w.Width)) w.Width *= f;
        if (!double.IsNaN(w.Height)) w.Height *= f;
        w.MinWidth *= f;
        w.MinHeight *= f;
    }

    /// <summary>Panel: nur den Inhalt skalieren (Breite und Höhe stellt das Panel selbst ein).</summary>
    public static void ApplyContentScale(FrameworkElement content)
    {
        var f = (Settings?.Invoke().UiScalePercent ?? 100) / 100.0;
        content.LayoutTransform = Math.Abs(f - 1) < 0.001 ? Transform.Identity : new ScaleTransform(f, f);
    }
}
