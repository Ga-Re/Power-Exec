using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PowerExec.Core.Config;

namespace PowerExec.Services;

/// <summary>
/// Tauscht das Farbschema (Dunkel/Hell) zur Laufzeit aus, übernimmt auf Wunsch die Windows-Akzentfarbe
/// und folgt dem Windows-Kontrastdesign (Hochkontrast) automatisch.
/// </summary>
public static class ThemeService
{
    public static bool IsDark { get; private set; } = true;
    public static bool IsHighContrast { get; private set; }

    /// <summary>Überschriebene Farben (Akzent, Hochkontrast), damit Panels mit eigener Farbe sie übernehmen können.</summary>
    public static IReadOnlyDictionary<string, Brush> Overrides { get; private set; } = new Dictionary<string, Brush>();

    /// <summary>Windows-Design, Akzentfarbe oder Kontrastdesign haben sich geändert.</summary>
    public static event Action? SystemChanged;

    private static bool _listening;

    /// <summary>Kennung des angewendeten Stands (Design, Kontrast, Akzentfarbe) – ändert sie sich, werden die Panels neu erzeugt.</summary>
    public static string StateKey => $"{IsDark}|{IsHighContrast}|{(Overrides.TryGetValue("Accent", out var a) ? a.ToString() : string.Empty)}";

    /// <summary>Nur das Farbschema (z. B. Selbsttest ohne weitere Einstellungen).</summary>
    public static void Apply(ThemeMode mode) => Apply(new AppSettings { Theme = mode });

    public static void Apply(AppSettings settings)
    {
        IsHighContrast = SystemParameters.HighContrast;
        IsDark = settings.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemUsesDarkTheme()
        };
        var dict = new ResourceDictionary
        {
            Source = new Uri(IsDark ? "pack://application:,,,/Themes/Dark.xaml" : "pack://application:,,,/Themes/Light.xaml")
        };
        var overrides = new Dictionary<string, Brush>();
        if (IsHighContrast) HighContrast(dict, overrides);
        else if (settings.UseWindowsAccent && WindowsAccent() is { } accent)
        {
            var a = Appearance.Derive(accent, IsDark);
            overrides["Accent"] = Solid(a.Accent);
            overrides["Accent.Hover"] = Solid(a.Hover);
            overrides["Accent.Pressed"] = Solid(a.Pressed);
            overrides["OnAccent"] = Solid(a.OnAccent);
            overrides["Search.Highlight"] = Solid(a.Highlight, IsDark ? (byte)0x55 : (byte)0x40);
        }
        foreach (var (key, brush) in overrides) dict[key] = brush;
        Overrides = overrides;

        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict;
        else merged.Insert(0, dict);
        Listen();
    }

    /// <summary>Kontrastdesign: alle Flächen, Texte und Rahmen aus den Systemfarben.</summary>
    private static void HighContrast(ResourceDictionary dict, Dictionary<string, Brush> overrides)
    {
        foreach (var key in dict.Keys.OfType<string>().ToList())
        {
            if (dict[key] is not SolidColorBrush) continue;
            Brush brush = key switch
            {
                "Accent" or "Accent.Hover" or "Accent.Pressed" or "Search.Highlight" => SystemColors.HighlightBrush,
                "OnAccent" => SystemColors.HighlightTextBrush,
                "Bg.Hover" or "Bg.Pressed" or "Bg.Selected" => SystemColors.HighlightBrush,
                "Text.Disabled" => SystemColors.GrayTextBrush,
                _ when key.StartsWith("Text.") || key.StartsWith("Status.") || key == "Star" || key.EndsWith(".Fg") || key.EndsWith(".Text") || key.StartsWith("Code.")
                    => SystemColors.WindowTextBrush,
                _ when key.StartsWith("Border.") || key.EndsWith(".Border") => SystemColors.WindowTextBrush,
                _ => SystemColors.WindowBrush
            };
            overrides[key] = brush;
        }
    }

    private static SolidColorBrush Solid(Rgb c, byte alpha = 255)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    /// <summary>Akzentfarbe aus HKCU\Software\Microsoft\Windows\DWM (AccentColor, 0xAABBGGRR).</summary>
    public static Rgb? WindowsAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int v) return Appearance.FromAbgr(unchecked((uint)v));
            var c = SystemParameters.WindowGlassColor;
            return new Rgb(c.R, c.G, c.B);
        }
        catch
        {
            return null;
        }
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v ? v == 0 : true;
        }
        catch
        {
            return true;
        }
    }

    private static void Listen()
    {
        if (_listening) return;
        _listening = true;
        try
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility or UserPreferenceCategory.VisualStyle)
                    Application.Current?.Dispatcher.BeginInvoke(() => SystemChanged?.Invoke());
            };
        }
        catch { /* ohne Benachrichtigung: Änderung greift beim nächsten Start */ }
    }
}
