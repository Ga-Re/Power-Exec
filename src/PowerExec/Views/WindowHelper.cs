using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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

    /// <summary>Getönter Fensterhintergrund über Mica (Bg.Window, teilweise durchsichtig); setzt der ThemeService.</summary>
    public const string MicaTintKey = "Bg.WindowMica";

    private static readonly HashSet<Window> _micaWindows = new();

    /// <summary>
    /// Design gewechselt: offene Fenster ziehen Titelleiste und Mica (hell/dunkel) nach – sonst bliebe z. B. die
    /// Navigation der Einstellungen nach dem Wechsel auf „Hell“ dunkel hinterlegt.
    /// </summary>
    public static void RefreshOpenWindows()
    {
        if (Application.Current == null) return;
        foreach (Window w in Application.Current.Windows)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) continue;
            try
            {
                int dark = ThemeService.IsDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                // Kontrastdesign: Mica aus, deckender Hintergrund
                if (_micaWindows.Contains(w) && ThemeService.IsHighContrast) w.SetResourceReference(Control.BackgroundProperty, "Bg.Window");
            }
            catch { }
        }
    }

    /// <summary>Mica gibt es ab Windows 11 22H2 (Build 22621).</summary>
    public static bool IsMicaSupported => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// Titelleiste passend zum Farbschema (Windows 10 20H1+ / Windows 11), auf Wunsch Mica als Hintergrund
    /// und die eingestellte Größe von Schrift und Bedienelementen.
    /// </summary>
    /// <summary>Panels hinter bzw. wieder vor die Fenster von Power-Exec legen (setzt der AppController).</summary>
    public static Action<bool>? PanelsBehind { get; set; }

    /// <summary>
    /// Ein Fenster von Power-Exec (Einstellungen, Dialog) ist aktiv: angeheftete Panels dürfen es nicht verdecken.
    /// Wird es inaktiv und ist kein anderes mehr aktiv, kommen die Panels wieder nach vorn.
    /// </summary>
    private static void TrackActivation(Window w)
    {
        w.Activated += (_, _) => PanelsBehind?.Invoke(true);
        void Check() => w.Dispatcher.BeginInvoke(() =>
        {
            var anyActive = Application.Current?.Windows.OfType<Window>().Any(x => x.IsActive && x is not PanelWindow) == true;
            if (!anyActive) PanelsBehind?.Invoke(false);
        }, System.Windows.Threading.DispatcherPriority.Background);
        w.Deactivated += (_, _) => Check();
        w.Closed += (_, _) => Check();
    }

    public static void ApplyTitleBar(Window w)
    {
        TrackActivation(w);
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
        // Leicht getönt statt ganz durchsichtig, damit Text auf jedem Hintergrund lesbar bleibt;
        // als Ressource, damit ein Wechsel hell/dunkel offene Fenster mitnimmt
        w.SetResourceReference(Control.BackgroundProperty, MicaTintKey);
        if (_micaWindows.Add(w)) w.Closed += (_, _) => _micaWindows.Remove(w);
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
