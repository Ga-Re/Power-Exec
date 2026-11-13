using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PowerExec.Core.Config;
using PowerExec.Desktop.Interop;
using PowerExec.Desktop.Services;

namespace PowerExec.Desktop.Views;

internal static class WindowHelper
{
    /// <summary>Aktuelle Einstellungen (Größe); setzt der AppController beim Start.</summary>
    public static Func<AppSettings>? Settings { get; set; }

    /// <summary>
    /// Design gewechselt: offene Fenster ziehen die Titelleiste (hell/dunkel) nach.
    /// </summary>
    public static void RefreshOpenWindows()
    {
        if (Application.Current == null) return;
        foreach (Window w in Application.Current.Windows)
        {
            NativeMethods.SetDarkTitleBar(new WindowInteropHelper(w).Handle, ThemeService.IsDark);
        }
    }

    /// <summary>
    /// Titelleiste passend zum Farbschema (Windows 10 20H1+ / Windows 11) und die eingestellte Größe von Schrift und
    /// Bedienelementen.
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
        void Apply() => NativeMethods.SetDarkTitleBar(new WindowInteropHelper(w).Handle, ThemeService.IsDark);

        if (new WindowInteropHelper(w).Handle != IntPtr.Zero) Apply();
        else w.SourceInitialized += (_, _) => Apply();
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
