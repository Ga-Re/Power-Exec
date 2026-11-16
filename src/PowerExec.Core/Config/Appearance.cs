using PowerExec.Core.Localization;
namespace PowerExec.Core.Config;

/// <summary>Hintergrund normaler Fenster (Einstellungen, Dialoge).</summary>

/// <summary>Akzentfarben für das Design, abgeleitet aus einer Grundfarbe (z. B. der Windows-Akzentfarbe).</summary>
public sealed record AccentColors(Rgb Accent, Rgb Hover, Rgb Pressed, Rgb OnAccent, Rgb Highlight);

/// <summary>
/// Eine Designvorlage: Design, Panelfarbe, Deckkraft, Hintergrund (Klarglas/Glas/Vollton), Milchglas und Akzent in einem
/// Klick. Eigene Designs der Benutzer haben dieselbe Form (<see cref="IsCustom"/>).
/// </summary>
public sealed record DesignPreset(string Key, string Name, string Description, ThemeMode Theme, string PanelColor, int Opacity, int Frost,
    bool WindowsAccent, PanelBackdrop Backdrop = PanelBackdrop.Clear, int GlassBlur = 50, int GlassSaturation = 100, string AccentColor = "",
    bool IsCustom = false, CornerStyle? Corners = null, DesignPalette? Palette = null, DesignVariant? Pair = null,
    string? FontFamily = null, string? MonoFontFamily = null, string? GroupColors = null, bool GroupColorsForTags = false,
    GroupCardStyle? GroupStyle = null, string? Category = null, Dictionary<string, DesignText>? Translations = null, string? BasedOn = null)
{
    /// <summary>Ist die Hauptvariante dunkel? (<see cref="ThemeMode.System"/> zählt als dunkel.)</summary>
    public bool PrimaryIsDark => Theme != ThemeMode.Light;

    /// <summary>Aus dem Marktplatz geladen (schreibgeschützt; Änderungen nur in einer eigenen Kopie).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDownloaded => Key.StartsWith("market:", StringComparison.Ordinal);

    /// <summary>Selbst angelegt oder als eigene Kopie bearbeitet – nur diese lassen sich ändern.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsOwn => Key.StartsWith("custom:", StringComparison.Ordinal);

    /// <summary>Gibt es eine helle und eine dunkle Variante (eigenes Paar oder Fläche „wie Windows“)?</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasBothVariants => Pair != null || Theme == ThemeMode.System;

    /// <summary>Name in der Sprache der Oberfläche (Übersetzung aus der Datei, sonst der Grundwert).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => Localized(t => t.Name, Name);

    /// <summary>Beschreibung in der Sprache der Oberfläche.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayDescription => Localized(t => t.Description, Description);

    /// <summary>Kategorie in der Sprache der Oberfläche (leer = keine).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayCategory => Localized(t => t.Category, Category ?? string.Empty);

    private string Localized(Func<DesignText, string?> pick, string fallback) =>
        Translations != null && Translations.TryGetValue(L.Language, out var t) && pick(t) is { Length: > 0 } v ? v : fallback;
}

/// <summary>Übersetzung von Name, Beschreibung und Kategorie eines Designs (leere Felder zeigen den englischen Grundwert).</summary>
public sealed record DesignText(string Name = "", string Description = "", string Category = "");

/// <summary>
/// Farben eines Designs über Panelfarbe und Akzent hinaus (je „#RRGGBB“). Leere Werte werden aus der Panelfarbe abgeleitet –
/// ein Design muss nur setzen, was es anders haben will.
/// </summary>
public sealed record DesignPalette(string Card = "", string Input = "", string Line = "", string Text = "", string TextSecondary = "", string Selected = "")
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Card.Length + Input.Length + Line.Length + Text.Length + TextSecondary.Length + Selected.Length == 0;
}

/// <summary>
/// Zweite Variante eines Designs für das jeweils andere Windows-Design (hell bzw. dunkel). Ein Design mit Paar wechselt
/// mit Windows mit.
/// </summary>
public sealed record DesignVariant(string PanelColor = "", string AccentColor = "", DesignPalette? Palette = null, string? GroupColors = null);

/// <summary>
/// Der Teil des aktiven Designs, der für die ganze App gilt und nicht am einzelnen Panel hängt: Palette, zweite Variante,
/// Schrift und Gruppenfarben. Panelfarbe, Akzent, Deckkraft und Hintergrund stehen weiterhin am Panel.
/// </summary>
public sealed class DesignLook
{
    public DesignPalette? Palette { get; set; }
    public DesignVariant? Pair { get; set; }
    /// <summary>Gilt <see cref="Pair"/> für das dunkle Windows-Design? (Sonst für das helle.)</summary>
    public bool PairIsDark { get; set; }
    /// <summary>Schrift für Text; leer = Standard der App.</summary>
    public string FontFamily { get; set; } = string.Empty;
    /// <summary>Schrift für Pfade und Code; leer = Standard der App.</summary>
    public string MonoFontFamily { get; set; } = string.Empty;
    /// <summary>Farbreihe für Gruppen ohne eigene Farbe (der Reihe nach, „#RRGGBB“).</summary>
    public List<string> GroupColors { get; set; } = new();
    /// <summary>Farbreihe der zweiten Variante (<see cref="Pair"/>); leer = dieselbe wie <see cref="GroupColors"/>.</summary>
    public List<string> PairGroupColors { get; set; } = new();
    /// <summary>Die Farbreihe auch für Schlagworte ohne eigene Farbe verwenden.</summary>
    public bool GroupColorsForTags { get; set; }

    public DesignLook Clone() => new()
    {
        Palette = Palette,
        Pair = Pair,
        PairIsDark = PairIsDark,
        FontFamily = FontFamily,
        MonoFontFamily = MonoFontFamily,
        GroupColors = GroupColors.ToList(),
        PairGroupColors = PairGroupColors.ToList(),
        GroupColorsForTags = GroupColorsForTags
    };

    /// <summary>Kennung zum Vergleichen zweier Stände (Markierung des aktiven Designs, „hat sich das Aussehen geändert?“).</summary>
    public string Key => string.Join("|", Palette, Pair, PairIsDark, FontFamily, MonoFontFamily, string.Join(",", GroupColors), string.Join(",", PairGroupColors), GroupColorsForTags);
}

/// <summary>Ausgerechnete Farben einer Variante (gesetzt oder abgeleitet).</summary>
public sealed record ResolvedPalette(Rgb Surface, Rgb Card, Rgb Input, Rgb Line, Rgb Text, Rgb TextSecondary, Rgb Accent, Rgb Selected);

/// <summary>Was ein Panel für das gerade laufende Windows-Design verwendet: Panelfarbe, Akzent und Palette der passenden Variante.</summary>
public readonly record struct DesignColors(string PanelColor, string AccentColor, DesignPalette? Palette);

/// <summary>Form der Ecken in allen Fenstern (Einstellung „Ecken“).</summary>
public enum CornerStyle
{
    /// <summary>Wie Windows 11: Bedienelemente 4, Karten 6, Panel und Kacheln 8, schwebende Fenster 12.</summary>
    Standard,
    /// <summary>Weicher: Bedienelemente 8, Karten 12, Panel und Kacheln 12, schwebende Fenster 16.</summary>
    Rounded,
    /// <summary>Ohne Rundung.</summary>
    Square
}

/// <summary>Eckenradien in DIP für die vier Stufen der Design-Tokens (<c>Radius.Control/Card/Tile/Window</c>).</summary>
public readonly record struct CornerRadii(double Control, double Card, double Tile, double Window)
{
    public static CornerRadii Of(CornerStyle style) => style switch
    {
        CornerStyle.Rounded => new(8, 12, 12, 16),
        CornerStyle.Square => new(0, 0, 0, 0),
        _ => new(4, 6, 8, 12)
    };
}

public static class Appearance
{
    public const int MinScale = 80, MaxScale = 150;

    /// <summary>
    /// Wie Windows: im dunklen Design eine hellere, im hellen eine dunklere Variante der Akzentfarbe,
    /// damit Text und Symbole in Akzentfarbe genug Kontrast haben (mind. 3:1 zum Hintergrund).
    /// </summary>
    public static AccentColors Derive(Rgb accent, bool dark) =>
        Derive(accent, dark, dark ? new Rgb(0x20, 0x20, 0x20) : new Rgb(0xF3, 0xF3, 0xF3));

    /// <summary>Wie <see cref="Derive(Rgb, bool)"/>, aber gegen die tatsächliche Fläche geprüft (eigene Panelfarbe).</summary>
    public static AccentColors Derive(Rgb accent, bool dark, Rgb background)
    {
        var toward = dark ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0);
        var a = accent;
        for (int i = 0; i < 10 && PanelColors.Contrast(a, background) < 3.0; i++) a = PanelColors.Blend(a, toward, 0.15);
        var hover = PanelColors.Blend(a, toward, 0.12);
        var pressed = PanelColors.Blend(a, dark ? new Rgb(0, 0, 0) : new Rgb(255, 255, 255), 0.15);
        var onAccent = PanelColors.Contrast(a, new Rgb(0, 0, 0)) >= PanelColors.Contrast(a, new Rgb(255, 255, 255)) ? new Rgb(0, 0, 0) : new Rgb(255, 255, 255);
        return new AccentColors(a, hover, pressed, onAccent, a);
    }

    /// <summary>Windows speichert die Akzentfarbe als 0xAABBGGRR.</summary>
    public static Rgb FromAbgr(uint abgr) => new((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));

    /// <summary>
    /// Mitgelieferte Grund-Designs – je eins pro Hintergrund (wie Windows, Glas, Vollton). Weitere Designs gibt es im
    /// Marktplatz (Einstellungen › Marktplatz › Designs).
    /// </summary>
    public static readonly IReadOnlyList<DesignPreset> Presets = new[]
    {
        new DesignPreset("fluent", "Fluent", L.T("Like Windows: design and accent color from the system"), ThemeMode.System, string.Empty, 100, 0, true),
        new DesignPreset("glass", L.T("Glass"), L.T("Blurred desktop behind the panel (Windows 11)"), ThemeMode.Dark, string.Empty, 70, 20, true,
            PanelBackdrop.Glass, GlassBlur: 60, GlassSaturation: 125),
        new DesignPreset("oled", "OLED", L.T("Deep black and opaque, Windows accent color"), ThemeMode.Dark, "#000000", 100, 0, true, PanelBackdrop.Solid),
    };

    /// <summary>Mitgelieferte und eigene Designs (eigene am Ende).</summary>
    public static IReadOnlyList<DesignPreset> All(AppSettings s) => Presets.Concat(s.CustomDesigns).ToList();

    /// <summary>Vorlage auf Einstellungen und Panels anwenden.</summary>
    public static void Apply(AppSettings s, DesignPreset p)
    {
        s.DesignKey = p.Key;
        s.Theme = ThemeFor(s, p);
        s.UseWindowsAccent = p.WindowsAccent;
        if (p.Corners is { } corners) s.Corners = corners; // Designs ohne Angabe lassen die Ecken, wie sie sind
        s.Look = LookOf(p);
        foreach (var panel in s.Panels) Apply(panel, p);
    }

    /// <summary>
    /// Hell oder dunkel: hat das Design beide Varianten, gilt die Wahl unter Darstellung (<see cref="AppSettings.Variant"/>),
    /// sonst die einzige Variante des Designs.
    /// </summary>
    public static ThemeMode ThemeFor(AppSettings s, DesignPreset p) => p.HasBothVariants ? s.Variant : p.Theme;

    /// <summary>
    /// Umstieg auf „ein aktives Design“ (Version 0.37): Gestaltung stellt man nur noch im Designer ein. Die gewählte
    /// Variante bleibt; passt der bisherige Stand zu keinem Design, wird er als eigenes Design „My settings“ übernommen –
    /// so geht nichts verloren. Auch Designs aus dem Marktplatz mit altem Dateinamen werden umgeschrieben.
    /// </summary>
    public static void MigrateToActiveDesign(AppSettings s)
    {
        s.Variant = s.Theme;
        Marketplace.MigrateRenamedDesigns(s);
        if (s.DesignKey.Length > 0 && Active(s) != null) return;
        if (Current(s) is { } match)
        {
            s.DesignKey = match.Key;
            return;
        }
        if (s.Panels.Count == 0) return;
        var own = Capture(s, s.Panels[0], L.T("My settings"));
        s.CustomDesigns.Add(own);
        s.DesignKey = own.Key;
    }

    /// <summary>Das aktive Design (null = unbekannt, etwa gelöscht).</summary>
    public static DesignPreset? Active(AppSettings s) => All(s).FirstOrDefault(d => d.Key == s.DesignKey);

    /// <summary>Der app-weite Teil eines Designs (Palette, zweite Variante, Schrift, Gruppenfarben).</summary>
    public static DesignLook LookOf(DesignPreset p) => new()
    {
        Palette = p.Palette is { IsEmpty: false } ? Normalize(p.Palette) : null,
        Pair = p.Pair is { } v ? Normalize(v) : null,
        PairIsDark = !p.PrimaryIsDark,
        FontFamily = (p.FontFamily ?? string.Empty).Trim(),
        MonoFontFamily = (p.MonoFontFamily ?? string.Empty).Trim(),
        GroupColors = SplitColors(p.GroupColors),
        PairGroupColors = SplitColors(p.Pair?.GroupColors),
        GroupColorsForTags = p.GroupColorsForTags
    };

    /// <summary>Farbreihe aus dem Text eines Designs („#FF8A70, #7FD1C7“): nur gültige Farben, höchstens <see cref="MaxGroupColors"/>.</summary>
    public static List<string> SplitColors(string? text) =>
        (text ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(Hex).Where(h => h.Length > 0).Take(MaxGroupColors).ToList();

    /// <summary>Farbreihe als Text für ein Design; ohne Farben null.</summary>
    public static string? JoinColors(IEnumerable<string> colors) => string.Join(", ", colors) is { Length: > 0 } t ? t : null;

    /// <summary>Höchstens so viele Farben in der Reihe der Gruppenfarben.</summary>
    public const int MaxGroupColors = 12;

    /// <summary>Gültige Farbe einheitlich als „#RRGGBB“, sonst leer (Designs kommen auch aus fremden Dateien).</summary>
    private static string Hex(string? text) => PanelColors.TryParse(text, out var c) ? c.Hex : string.Empty;

    private static DesignPalette Normalize(DesignPalette p) =>
        new(Hex(p.Card), Hex(p.Input), Hex(p.Line), Hex(p.Text), Hex(p.TextSecondary), Hex(p.Selected));

    private static DesignVariant Normalize(DesignVariant v) =>
        new(Hex(v.PanelColor), Hex(v.AccentColor), v.Palette is { IsEmpty: false } p ? Normalize(p) : null, JoinColors(SplitColors(v.GroupColors)));

    /// <summary>
    /// Farben des Panels für das laufende Windows-Design: bei einem Design mit zwei Varianten die passende, sonst die
    /// Werte des Panels mit der Palette des Designs.
    /// </summary>
    public static DesignColors Effective(AppSettings s, PanelConfig panel, bool dark) =>
        s.Look.Pair is { } pair && s.Look.PairIsDark == dark
            ? new DesignColors(pair.PanelColor, pair.AccentColor, pair.Palette)
            : new DesignColors(panel.PanelColor, panel.AccentColor, s.Look.Palette);

    /// <summary>
    /// Alle Farben einer Variante ausgerechnet: was das Design setzt, sonst die Ableitung aus Fläche und Akzent. So zeigt
    /// der Design-Editor, was „abgeleitet“ bedeutet, und prüft den Kontrast.
    /// </summary>
    public static ResolvedPalette Resolve(DesignColors c, bool dark)
    {
        var surface = PanelColors.TryParse(c.PanelColor, out var p) ? p : dark ? PanelColors.DarkSurface : PanelColors.LightSurface;
        var light = PanelColors.IsLight(surface);
        var d = PanelColors.Derive(surface);
        var palette = c.Palette ?? new DesignPalette();
        Rgb Or(string hex, Rgb fallback) => PanelColors.TryParse(hex, out var v) ? v : fallback;
        var text = Or(palette.Text, light ? new Rgb(0x1B, 0x1B, 0x1B) : new Rgb(0xFF, 0xFF, 0xFF));
        var accentSource = PanelColors.TryParse(c.AccentColor, out var a) ? a : PanelColors.TryParse(c.PanelColor, out var own) ? own : new Rgb(0x00, 0x78, 0xD4);
        return new ResolvedPalette(surface, Or(palette.Card, d.Hover), Or(palette.Input, d.Input), Or(palette.Line, d.Border), text,
            Or(palette.TextSecondary, PanelColors.Blend(text, surface, 0.35)), Derive(accentSource, !light, surface).Accent, Or(palette.Selected, d.Selected));
    }

    /// <summary>
    /// Gegenstück einer Variante für das andere Windows-Design: gleicher Farbton und gleicher Akzent, Helligkeit
    /// umgekehrt (dunkel → helle, leicht getönte Fläche; hell → dunkle). Die Palette wird passend dazu berechnet, damit
    /// das Gegenstück wie dasselbe Design aussieht und nicht wie Weiß mit Akzent. Ohne eigene Fläche folgt auch das
    /// Gegenstück der Fläche des Designs.
    /// </summary>
    public static DesignVariant Counterpart(DesignColors from, bool fromDark)
    {
        if (!PanelColors.TryParse(from.PanelColor, out var surface)) return new DesignVariant(string.Empty, from.AccentColor, null);
        var (h, sat, _) = ToHsl(surface);
        var accent = PanelColors.TryParse(from.AccentColor, out var a) ? a : surface;
        // Hell: fast weiß mit einem Hauch des Farbtons; dunkel: tief, leicht gesättigt
        var other = fromDark ? FromHsl(h, Math.Min(sat, 0.35) * 0.55, 0.955) : FromHsl(h, Math.Min(sat, 0.45), 0.12);
        return new DesignVariant(other.Hex, from.AccentColor, PaletteFor(other, accent));
    }

    /// <summary>
    /// Stimmige Palette zu Fläche und Akzent: Karten, Linien und Auswahl leicht zum Akzent getönt, Text mit genug
    /// Kontrast (mindestens 7:1, Zweittext 4,6:1). Grundlage für Gegenstücke und die Designs im Marktplatz.
    /// </summary>
    public static DesignPalette PaletteFor(Rgb surface, Rgb accent)
    {
        Rgb white = new(255, 255, 255), black = new(0, 0, 0);
        bool light = PanelColors.IsLight(surface);
        Rgb card, input, line, selected, text;
        if (light)
        {
            card = PanelColors.Blend(PanelColors.Blend(surface, accent, 0.04), white, 0.6);
            input = PanelColors.Blend(surface, white, 0.8);
            line = PanelColors.Blend(PanelColors.Blend(surface, accent, 0.18), black, 0.12);
            selected = PanelColors.Blend(surface, accent, 0.16);
            text = PanelColors.Blend(new Rgb(0x1A, 0x1A, 0x1A), accent, 0.08);
        }
        else
        {
            card = PanelColors.Blend(PanelColors.Blend(surface, accent, 0.10), white, 0.07);
            input = PanelColors.Blend(surface, black, 0.25);
            line = PanelColors.Blend(PanelColors.Blend(surface, accent, 0.25), white, 0.12);
            selected = PanelColors.Blend(PanelColors.Blend(surface, accent, 0.22), white, 0.06);
            text = PanelColors.Blend(white, accent, 0.06);
        }
        var toward = light ? black : white;
        for (int i = 0; i < 20 && Math.Min(PanelColors.Contrast(text, surface), PanelColors.Contrast(text, card)) < 7; i++) text = PanelColors.Blend(text, toward, 0.2);
        var secondary = PanelColors.Blend(text, surface, 0.32);
        for (int i = 0; i < 20 && PanelColors.Contrast(secondary, surface) < 4.6; i++) secondary = PanelColors.Blend(secondary, text, 0.15);
        return new DesignPalette(card.Hex, input.Hex, line.Hex, text.Hex, secondary.Hex, selected.Hex);
    }

    /// <summary>Farbton (0–1), Sättigung und Helligkeit (HSL).</summary>
    private static (double H, double S, double L) ToHsl(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    private static Rgb FromHsl(double h, double s, double l)
    {
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
        double r, g, b;
        if (s < 1e-9) r = g = b = l;
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3);
            g = Hue(p, q, h);
            b = Hue(p, q, h - 1.0 / 3);
        }
        static byte B(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return new Rgb(B(r), B(g), B(b));
    }

    /// <summary>Schreibt ein Design als Datei für den Marktplatz bzw. zum Weitergeben (ohne Kennung des Rechners).</summary>
    public static string ToFile(DesignPreset p) =>
        System.Text.Json.JsonSerializer.Serialize(p with { Key = string.Empty, IsCustom = false }, Marketplace.Json);

    /// <summary>
    /// Farbe aus der Reihe der Gruppenfarben für die Gruppe an Stelle <paramref name="index"/> (umlaufend); leer, wenn das
    /// Design keine Reihe hat.
    /// </summary>
    public static string SeriesColor(AppSettings s, int index)
    {
        var colors = GroupPalette(s, IsDarkNow);
        return colors.Count == 0 ? string.Empty : colors[(index % colors.Count + colors.Count) % colors.Count];
    }

    /// <summary>
    /// Farbreihe für Gruppen in der Variante <paramref name="dark"/>: die der zweiten Variante, wenn das Design dafür eine
    /// eigene hat, sonst die der Hauptvariante.
    /// </summary>
    public static IReadOnlyList<string> GroupPalette(AppSettings s, bool dark) =>
        s.Look.Pair != null && s.Look.PairIsDark == dark && s.Look.PairGroupColors.Count > 0 ? s.Look.PairGroupColors : s.Look.GroupColors;

    /// <summary>
    /// Läuft die Oberfläche gerade dunkel? Setzt das Design beim Anwenden (Windows kann jederzeit umschalten); bestimmt,
    /// welche Farbreihe der Gruppen gilt.
    /// </summary>
    public static bool IsDarkNow { get; set; } = true;

    // ---- Farbangaben für Gruppen und Schlagworte -------------------------------------------

    /// <summary>Gespeicherte Farbe „keine Farbe“ (auch nicht vom Design).</summary>
    public const string NoColor = "none";
    private const string DesignColorPrefix = "design:";

    /// <summary>Gespeicherte Farbe „Farbe Nr. <paramref name="index"/> der Reihe des Designs“ (0-basiert).</summary>
    public static string DesignColor(int index) => DesignColorPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Stelle in der Reihe des Designs, wenn <paramref name="value"/> darauf verweist; sonst null.</summary>
    public static int? DesignColorIndex(string? value) =>
        value != null && value.StartsWith(DesignColorPrefix, StringComparison.OrdinalIgnoreCase)
        && int.TryParse(value.AsSpan(DesignColorPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var i)
            ? i : null;

    /// <summary>
    /// Gespeicherte Farbe vereinheitlichen: „#RRGGBB“, „design:N“, „none“ – sonst leer (= automatisch bzw. wie die übergeordnete Gruppe).
    /// </summary>
    public static string NormalizeColorRef(string? value)
    {
        if (string.Equals(value, NoColor, StringComparison.OrdinalIgnoreCase)) return NoColor;
        if (DesignColorIndex(value) is { } i) return DesignColor(Math.Clamp(i, 0, MaxGroupColors - 1));
        return PanelColors.TryParse(value, out var c) ? c.Hex : string.Empty;
    }

    /// <summary>
    /// Farbe zu einer gespeicherten Angabe: feste Farbe, Farbe aus der Reihe des Designs oder keine (leer). Null bei leerer
    /// Angabe – dann entscheidet der Aufrufer (übergeordnete Gruppe, automatisch).
    /// </summary>
    public static string? ResolveColorRef(AppSettings s, string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (string.Equals(value, NoColor, StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (DesignColorIndex(value) is { } i) return SeriesColor(s, i);
        return PanelColors.TryParse(value, out var c) ? c.Hex : null;
    }

    /// <summary>Gleich bleibende Stelle in der Farbreihe für einen Namen (Schlagworte haben keine Reihenfolge).</summary>
    public static int SeriesIndex(string name) => name.Aggregate(0, (sum, c) => (sum * 31 + char.ToLowerInvariant(c)) & 0x7FFFFFFF);

    /// <summary>Vorlage auf ein Panel anwenden (Farbe, Deckkraft, Hintergrund, Milchglas, Weichzeichner, Akzent).</summary>
    public static void Apply(PanelConfig panel, DesignPreset p)
    {
        if (p.GroupStyle is { } style) panel.GroupStyle = style;
        panel.PanelColor = p.PanelColor;
        panel.PanelOpacity = Math.Clamp(p.Opacity, 20, 100);
        panel.Acrylic = false;
        panel.Backdrop = p.Backdrop;
        panel.Frost = Math.Clamp(p.Frost, 0, 60);
        panel.AccentColor = p.AccentColor;
        if (p.Backdrop == PanelBackdrop.Glass)
        {
            panel.GlassBlur = Math.Clamp(p.GlassBlur, 0, 100);
            panel.GlassSaturation = Math.Clamp(p.GlassSaturation, 0, 200);
        }
    }

    /// <summary>Aktuellen Stand (Design und erstes Panel) als eigenes Design festhalten.</summary>
    public static DesignPreset Capture(AppSettings s, PanelConfig panel, string name) => new(
        "custom:" + Guid.NewGuid().ToString("N")[..8], name.Trim(), DescribeCustom(panel), s.Theme, panel.PanelColor, panel.PanelOpacity,
        panel.Frost, s.UseWindowsAccent, panel.EffectiveBackdropSetting, panel.GlassBlur, panel.GlassSaturation, panel.AccentColor, IsCustom: true,
        Palette: s.Look.Palette, Pair: s.Look.Pair, FontFamily: s.Look.FontFamily.Length > 0 ? s.Look.FontFamily : null,
        MonoFontFamily: s.Look.MonoFontFamily.Length > 0 ? s.Look.MonoFontFamily : null,
        GroupColors: JoinColors(s.Look.GroupColors), GroupColorsForTags: s.Look.GroupColorsForTags, Corners: s.Corners, GroupStyle: panel.GroupStyle)
    {
        Pair = s.Look.Pair is { } pair ? pair with { GroupColors = JoinColors(s.Look.PairGroupColors) } : null
    };

    private static string DescribeCustom(PanelConfig p) =>
        L.F("Custom design · {0}", PanelBackdrops.Label(p.EffectiveBackdropSetting).Split(' ')[0]) + (p.EffectiveBackdropSetting == PanelBackdrop.Solid ? "" : $", {p.PanelOpacity} %");

    /// <summary>Passt das Panel zur Vorlage? (für die Markierung)</summary>
    public static bool Matches(AppSettings s, PanelConfig x, DesignPreset p) =>
        (p.Pair != null ? s.Theme == ThemeMode.System : p.Theme == s.Theme) && p.WindowsAccent == s.UseWindowsAccent &&
        LookOf(p).Key == s.Look.Key &&
        string.Equals(x.PanelColor, p.PanelColor, StringComparison.OrdinalIgnoreCase) &&
        x.EffectiveBackdropSetting == p.Backdrop &&
        (p.Backdrop == PanelBackdrop.Solid || x.PanelOpacity == p.Opacity) &&
        string.Equals(x.AccentColor, p.AccentColor, StringComparison.OrdinalIgnoreCase) &&
        (p.Backdrop == PanelBackdrop.Solid || (p.Frost > 0) == (x.Frost > 0));

    /// <summary>Passende Vorlage zum aktuellen Stand (für die Markierung), sonst null.</summary>
    public static DesignPreset? Current(AppSettings s) => All(s).FirstOrDefault(p => s.Panels.Count > 0 && s.Panels.All(x => Matches(s, x, p)));
}
