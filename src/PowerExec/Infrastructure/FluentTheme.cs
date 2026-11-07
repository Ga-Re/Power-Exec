using System.Windows;
using System.Windows.Media;
using PowerExec.Services;
using Wpf.Ui.Markup;

namespace PowerExec.Infrastructure;

/// <summary>
/// Fluent-Bedienelemente von WPF UI für einzelne Fenster (zunächst das Einstellungsfenster). Die Ressourcen von
/// WPF UI hängen am Fenster, nicht an der App – Panel und übrige Fenster behalten ihr bisheriges Aussehen.
/// Design (hell/dunkel/Kontrast) und Akzentfarbe kommen aus dem <see cref="ThemeService"/>; die eigenen Farben
/// (Karten, Texte) zeigen im Fenster auf die passenden Fluent-Farben, damit alles aus einem Guss wirkt.
/// </summary>
public static class FluentTheme
{
    private const string ThemePath = "pack://application:,,,/Wpf.Ui;component/Resources/Theme/";

    /// <summary>Eigene Farbschlüssel → Fluent-Farben von WPF UI (nicht im Kontrastdesign: dort gelten die Systemfarben).</summary>
    private static readonly (string Own, string Fluent)[] PaletteMap =
    {
        ("Bg.Card", "CardBackgroundFillColorDefaultBrush"),
        ("Bg.CardAlt", "CardBackgroundFillColorSecondaryBrush"),
        ("Border.Default", "CardStrokeColorDefaultBrush"),
        ("Border.Control", "ControlStrokeColorDefaultBrush"),
        ("Bg.Hover", "SubtleFillColorSecondaryBrush"),
        ("Bg.Pressed", "SubtleFillColorTertiaryBrush"),
        ("Bg.Selected", "SubtleFillColorSecondaryBrush"),
        ("Bg.Input", "ControlFillColorDefaultBrush"),
        ("Text.Primary", "TextFillColorPrimaryBrush"),
        ("Text.Secondary", "TextFillColorSecondaryBrush"),
        ("Text.Tertiary", "TextFillColorTertiaryBrush"),
        ("Text.Disabled", "TextFillColorDisabledBrush")
    };

    private static readonly List<WeakReference<FrameworkElement>> Attached = new();

    /// <summary>
    /// Fenster (oder anderes Element) mit WPF UI ausstatten. Das XAML bindet <c>ui:ThemesDictionary</c> und
    /// <c>ui:ControlsDictionary</c> bereits ein (für <c>BasedOn</c>-Stile); hier werden Design und Farben angepasst
    /// und bei jedem Designwechsel nachgezogen.
    /// </summary>
    public static void Attach(FrameworkElement root)
    {
        lock (Attached)
        {
            Attached.RemoveAll(w => !w.TryGetTarget(out _));
            Attached.Add(new WeakReference<FrameworkElement>(root));
        }
        Apply(root);
    }

    /// <summary>Nach <see cref="ThemeService.Apply(PowerExec.Core.Config.AppSettings)"/>: alle Fluent-Fenster nachziehen.</summary>
    public static void Refresh()
    {
        List<FrameworkElement> alive;
        lock (Attached)
        {
            Attached.RemoveAll(w => !w.TryGetTarget(out _));
            alive = Attached.Select(w => w.TryGetTarget(out var e) ? e : null).OfType<FrameworkElement>().ToList();
        }
        foreach (var e in alive) Apply(e);
    }

    private static void Apply(FrameworkElement root)
    {
        var res = root.Resources;
        var merged = res.MergedDictionaries;
        var themeUri = new Uri(ThemePath + ThemeFile() + ".xaml", UriKind.Absolute);
        var index = merged.ToList().FindIndex(d => d is ThemesDictionary || d.Source?.OriginalString.StartsWith(ThemePath, StringComparison.OrdinalIgnoreCase) == true);
        if (index < 0)
        {
            merged.Insert(0, new ResourceDictionary { Source = themeUri });
            if (!merged.OfType<ControlsDictionary>().Any()) merged.Insert(1, new ControlsDictionary());
        }
        else if (merged[index].Source != themeUri)
        {
            merged[index] = new ResourceDictionary { Source = themeUri };
        }

        ApplyAccent(res);

        foreach (var (own, fluent) in PaletteMap)
        {
            if (ThemeService.IsHighContrast) res.Remove(own);
            else if (FindIn(merged, fluent) is Brush b) res[own] = b;
        }
    }

    /// <summary>Datei des Fluent-Designs: Hell, Dunkel oder das passende Kontrastdesign von Windows.</summary>
    private static string ThemeFile()
    {
        if (!ThemeService.IsHighContrast) return ThemeService.IsDark ? "Dark" : "Light";
        var w = SystemColors.WindowColor;
        return (w.R * 299 + w.G * 587 + w.B * 114) / 1000 < 128 ? "HCBlack" : "HCWhite";
    }

    /// <summary>
    /// Akzentfarbe wie im übrigen Programm (eigene Palette oder Windows-Akzent), in den Schlüsseln, die WPF UI
    /// erwartet. Direkt am Fenster gesetzt, damit sie die Vorgaben aus <c>Accent.xaml</c> überdecken.
    /// </summary>
    private static void ApplyAccent(ResourceDictionary res)
    {
        var accent = Color("Accent", Colors.DodgerBlue);
        var hover = Color("Accent.Hover", accent);
        var pressed = Color("Accent.Pressed", accent);
        var onAccent = Color("OnAccent", Colors.White);

        res["SystemAccentColor"] = accent;
        res["SystemAccentColorPrimary"] = hover;
        res["SystemAccentColorSecondary"] = accent;
        res["SystemAccentColorTertiary"] = pressed;
        res["SystemAccentColorBrush"] = Solid(accent);
        res["SystemAccentColorPrimaryBrush"] = Solid(hover);
        res["SystemAccentColorSecondaryBrush"] = Solid(accent);
        res["SystemAccentColorTertiaryBrush"] = Solid(pressed);
        res["SystemAccentBrush"] = Solid(accent);
        res["SystemFillColorAttentionBrush"] = Solid(accent);
        res["AccentTextFillColorPrimaryBrush"] = Solid(accent);
        res["AccentTextFillColorSecondaryBrush"] = Solid(pressed);
        res["AccentTextFillColorTertiaryBrush"] = Solid(hover);
        res["AccentFillColorSelectedTextBackgroundBrush"] = Solid(accent);
        res["AccentFillColorDefault"] = accent;
        res["AccentFillColorDefaultBrush"] = Solid(accent);
        res["AccentFillColorSecondary"] = WithAlpha(accent, 229);
        res["AccentFillColorSecondaryBrush"] = Solid(WithAlpha(accent, 229));
        res["AccentFillColorTertiary"] = WithAlpha(accent, 204);
        res["AccentFillColorTertiaryBrush"] = Solid(WithAlpha(accent, 204));
        res["TextOnAccentFillColorPrimary"] = onAccent;
        res["TextOnAccentFillColorSecondary"] = WithAlpha(onAccent, 0xB3);
        res["TextOnAccentFillColorPrimaryBrush"] = Solid(onAccent);
        res["TextOnAccentFillColorSecondaryBrush"] = Solid(WithAlpha(onAccent, 0xB3));
        res["AccentButtonForeground"] = Solid(onAccent);
        res["AccentButtonForegroundPointerOver"] = Solid(onAccent);
        res["AccentButtonForegroundPressed"] = Solid(WithAlpha(onAccent, 0xB3));
    }

    private static Color Color(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;

    private static Color WithAlpha(Color c, byte a) => System.Windows.Media.Color.FromArgb(a, c.R, c.G, c.B);

    private static SolidColorBrush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static object? FindIn(IEnumerable<ResourceDictionary> dictionaries, string key)
    {
        foreach (var d in dictionaries.Reverse())
        {
            if (d.Contains(key)) return d[key];
            if (FindIn(d.MergedDictionaries, key) is { } found) return found;
        }
        return null;
    }
}
