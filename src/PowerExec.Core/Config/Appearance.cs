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
    string? FontFamily = null, string? MonoFontFamily = null, string? GroupColors = null, bool GroupColorsForTags = false)
{
    /// <summary>Ist die Hauptvariante dunkel? (<see cref="ThemeMode.System"/> zählt als dunkel.)</summary>
    public bool PrimaryIsDark => Theme != ThemeMode.Light;
}

/// <summary>
/// Farben eines Designs über Panelfarbe und Akzent hinaus (je „#RRGGBB“). Leere Werte werden aus der Panelfarbe abgeleitet –
/// ein Design muss nur setzen, was es anders haben will.
/// </summary>
public sealed record DesignPalette(string Card = "", string Input = "", string Line = "", string Text = "", string TextSecondary = "", string Selected = "")
{
    public bool IsEmpty => Card.Length + Input.Length + Line.Length + Text.Length + TextSecondary.Length + Selected.Length == 0;
}

/// <summary>
/// Zweite Variante eines Designs für das jeweils andere Windows-Design (hell bzw. dunkel). Ein Design mit Paar wechselt
/// mit Windows mit.
/// </summary>
public sealed record DesignVariant(string PanelColor = "", string AccentColor = "", DesignPalette? Palette = null);

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
        GroupColorsForTags = GroupColorsForTags
    };

    /// <summary>Kennung zum Vergleichen zweier Stände (Markierung des aktiven Designs, „hat sich das Aussehen geändert?“).</summary>
    public string Key => string.Join("|", Palette, Pair, PairIsDark, FontFamily, MonoFontFamily, string.Join(",", GroupColors), GroupColorsForTags);
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
        s.Theme = p.Theme;
        s.UseWindowsAccent = p.WindowsAccent;
        if (p.Corners is { } corners) s.Corners = corners; // Designs ohne Angabe lassen die Ecken, wie sie sind
        s.Look = LookOf(p);
        if (p.Pair != null) s.Theme = ThemeMode.System; // Design mit zwei Varianten wechselt mit Windows mit
        foreach (var panel in s.Panels) Apply(panel, p);
    }

    /// <summary>Der app-weite Teil eines Designs (Palette, zweite Variante, Schrift, Gruppenfarben).</summary>
    public static DesignLook LookOf(DesignPreset p) => new()
    {
        Palette = p.Palette is { IsEmpty: false } ? Normalize(p.Palette) : null,
        Pair = p.Pair is { } v ? Normalize(v) : null,
        PairIsDark = !p.PrimaryIsDark,
        FontFamily = (p.FontFamily ?? string.Empty).Trim(),
        MonoFontFamily = (p.MonoFontFamily ?? string.Empty).Trim(),
        GroupColors = SplitColors(p.GroupColors),
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
        new(Hex(v.PanelColor), Hex(v.AccentColor), v.Palette is { IsEmpty: false } p ? Normalize(p) : null);

    /// <summary>
    /// Farben des Panels für das laufende Windows-Design: bei einem Design mit zwei Varianten die passende, sonst die
    /// Werte des Panels mit der Palette des Designs.
    /// </summary>
    public static DesignColors Effective(AppSettings s, PanelConfig panel, bool dark) =>
        s.Look.Pair is { } pair && s.Theme == ThemeMode.System && s.Look.PairIsDark == dark
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

    /// <summary>Schreibt ein Design als Datei für den Marktplatz bzw. zum Weitergeben (ohne Kennung des Rechners).</summary>
    public static string ToFile(DesignPreset p) =>
        System.Text.Json.JsonSerializer.Serialize(p with { Key = string.Empty, IsCustom = false }, Marketplace.Json);

    /// <summary>
    /// Farbe aus der Reihe der Gruppenfarben für die Gruppe an Stelle <paramref name="index"/> (umlaufend); leer, wenn das
    /// Design keine Reihe hat.
    /// </summary>
    public static string SeriesColor(AppSettings s, int index) =>
        s.Look.GroupColors.Count == 0 ? string.Empty : s.Look.GroupColors[(index % s.Look.GroupColors.Count + s.Look.GroupColors.Count) % s.Look.GroupColors.Count];

    /// <summary>Gleich bleibende Stelle in der Farbreihe für einen Namen (Schlagworte haben keine Reihenfolge).</summary>
    public static int SeriesIndex(string name) => name.Aggregate(0, (sum, c) => (sum * 31 + char.ToLowerInvariant(c)) & 0x7FFFFFFF);

    /// <summary>Vorlage auf ein Panel anwenden (Farbe, Deckkraft, Hintergrund, Milchglas, Weichzeichner, Akzent).</summary>
    public static void Apply(PanelConfig panel, DesignPreset p)
    {
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
        GroupColors: JoinColors(s.Look.GroupColors), GroupColorsForTags: s.Look.GroupColorsForTags);

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
