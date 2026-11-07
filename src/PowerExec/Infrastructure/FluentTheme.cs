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

    /// <summary>Ein Fluent-Fenster: eigenes Hell/Dunkel (Panel mit eigener Farbe) und ob die eigenen Farben umgelenkt werden.</summary>
    private sealed record Entry(WeakReference<FrameworkElement> Root, Func<bool>? Dark, bool MapPalette);

    private static readonly List<Entry> Attached = new();

    static FluentTheme()
    {
        // Das Kontrollkästchen von WPF UI animiert „Tag“ (Wert null). Verlässt es den Baum eines Fluent-Fensters
        // (z. B. beim Neuaufbau einer Liste), fiele es auf den App-Stil zurück, die Vorlage würde mitten in der
        // Animation getauscht – WPF wirft dann ArgumentNullException. Daher den Stil beim Laden fest setzen.
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.CheckBox), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => PinStyle((FrameworkElement)s)));
    }

    /// <summary>Impliziten Fluent-Stil eines Elements in einem Fluent-Fenster lokal festschreiben.</summary>
    private static void PinStyle(FrameworkElement e)
    {
        if (e.ReadLocalValue(FrameworkElement.StyleProperty) != DependencyProperty.UnsetValue) return;
        if (!IsInFluentRoot(e)) return;
        if (e.TryFindResource(e.GetType()) is Style s) e.Style = s;
    }

    private static bool IsInFluentRoot(DependencyObject e)
    {
        var window = Window.GetWindow(e);
        if (window == null) return false;
        lock (Attached) return Attached.Any(a => a.Root.TryGetTarget(out var r) && ReferenceEquals(r, window));
    }

    /// <summary>
    /// Fenster (oder anderes Element) mit WPF UI ausstatten. Das XAML bindet <c>ui:ThemesDictionary</c> und
    /// <c>ui:ControlsDictionary</c> bereits ein (für <c>BasedOn</c>-Stile); hier werden Design und Farben angepasst
    /// und bei jedem Designwechsel nachgezogen.
    /// </summary>
    /// <param name="dark">Eigenes Hell/Dunkel (z. B. Panel mit heller Panelfarbe im dunklen Design); null = wie das Programm.</param>
    /// <param name="mapPalette">Eigene Farben (Karten, Texte, Flächen) auf Fluent-Farben umlenken. Das Panel setzt seine Flächenfarben selbst.</param>
    public static void Attach(FrameworkElement root, Func<bool>? dark = null, bool mapPalette = true)
    {
        var entry = new Entry(new WeakReference<FrameworkElement>(root), dark, mapPalette);
        lock (Attached)
        {
            Attached.RemoveAll(a => !a.Root.TryGetTarget(out var r) || ReferenceEquals(r, root));
            Attached.Add(entry);
        }
        Apply(root, entry);
    }

    /// <summary>Nach <see cref="ThemeService.Apply(PowerExec.Core.Config.AppSettings)"/>: alle Fluent-Fenster nachziehen.</summary>
    public static void Refresh()
    {
        List<(FrameworkElement Root, Entry Entry)> alive;
        lock (Attached)
        {
            Attached.RemoveAll(a => !a.Root.TryGetTarget(out _));
            alive = Attached.Select(a => (a.Root.TryGetTarget(out var e) ? e : null, a)).Where(x => x.Item1 != null).Select(x => (x.Item1!, x.a)).ToList();
        }
        foreach (var (root, entry) in alive) Apply(root, entry);
    }

    private static void Apply(FrameworkElement root, Entry entry)
    {
        var res = root.Resources;
        var merged = res.MergedDictionaries;
        var themeUri = new Uri(ThemePath + ThemeFile(entry.Dark?.Invoke() ?? ThemeService.IsDark) + ".xaml", UriKind.Absolute);
        // Das aktuelle Design liegt als letztes Wörterbuch direkt am Fenster und überdeckt das dunkle Vorgabe-Design
        // aus Themes/Fluent.xaml. Verschachtelt ausgetauschte Wörterbücher meldet WPF nicht zuverlässig weiter –
        // Bedienelemente behielten sonst Farben des alten Designs (helle Schrift auf hellem Grund).
        if (!AllDictionaries(res).OfType<ControlsDictionary>().Any()) merged.Add(new ControlsDictionary());
        var current = merged.LastOrDefault(IsTheme);
        if (current == null) merged.Add(new ResourceDictionary { Source = themeUri });
        else if (current.Source != themeUri)
        {
            merged.Remove(current);
            merged.Add(new ResourceDictionary { Source = themeUri });
        }

        ApplyAccent(root);

        foreach (var (own, fluent) in PaletteMap)
        {
            if (ThemeService.IsHighContrast || !entry.MapPalette) res.Remove(own);
            else if (FindIn(merged, fluent) is Brush b) res[own] = b;
        }
    }

    /// <summary>Datei des Fluent-Designs: Hell, Dunkel oder das passende Kontrastdesign von Windows.</summary>
    private static string ThemeFile(bool dark)
    {
        if (!ThemeService.IsHighContrast) return dark ? "Dark" : "Light";
        var w = SystemColors.WindowColor;
        return (w.R * 299 + w.G * 587 + w.B * 114) / 1000 < 128 ? "HCBlack" : "HCWhite";
    }

    /// <summary>
    /// Akzentfarbe wie im übrigen Programm (eigene Palette oder Windows-Akzent), in den Schlüsseln, die WPF UI
    /// erwartet. Direkt am Fenster gesetzt, damit sie die Vorgaben aus <c>Accent.xaml</c> überdecken.
    /// </summary>
    private static void ApplyAccent(FrameworkElement root)
    {
        var res = root.Resources;
        // Akzent des Programms bzw. des Panels (Panels mit Gegen-Design haben eigene Akzentwerte)
        System.Windows.Media.Color Color(string key, System.Windows.Media.Color fallback) =>
            root.TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;
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


    private static Color WithAlpha(Color c, byte a) => System.Windows.Media.Color.FromArgb(a, c.R, c.G, c.B);

    private static SolidColorBrush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static bool IsTheme(ResourceDictionary d) =>
        d is ThemesDictionary || d.Source?.OriginalString.StartsWith(ThemePath, StringComparison.OrdinalIgnoreCase) == true;

    private static IEnumerable<ResourceDictionary> AllDictionaries(ResourceDictionary d)
    {
        foreach (var m in d.MergedDictionaries)
        {
            yield return m;
            foreach (var mm in AllDictionaries(m)) yield return mm;
        }
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
