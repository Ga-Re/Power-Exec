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
    bool IsCustom = false);

public static class Appearance
{
    public const int MinScale = 80, MaxScale = 150;

    /// <summary>
    /// Wie Windows: im dunklen Design eine hellere, im hellen eine dunklere Variante der Akzentfarbe,
    /// damit Text und Symbole in Akzentfarbe genug Kontrast haben (mind. 3:1 zum Hintergrund).
    /// </summary>
    public static AccentColors Derive(Rgb accent, bool dark)
    {
        var background = dark ? new Rgb(0x20, 0x20, 0x20) : new Rgb(0xF3, 0xF3, 0xF3);
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
        new DesignPreset("fluent", "Fluent", L.T("Wie Windows: Design und Akzentfarbe vom System"), ThemeMode.System, string.Empty, 100, 0, true),
        new DesignPreset("glass", L.T("Glas"), L.T("Weichgezeichneter Desktop hinter dem Panel (Windows 11)"), ThemeMode.Dark, string.Empty, 70, 20, true,
            PanelBackdrop.Glass, GlassBlur: 60, GlassSaturation: 125),
        new DesignPreset("oled", "OLED", L.T("Tiefschwarz und deckend, Akzentfarbe von Windows"), ThemeMode.Dark, "#000000", 100, 0, true, PanelBackdrop.Solid),
    };

    /// <summary>Mitgelieferte und eigene Designs (eigene am Ende).</summary>
    public static IReadOnlyList<DesignPreset> All(AppSettings s) => Presets.Concat(s.CustomDesigns).ToList();

    /// <summary>Vorlage auf Einstellungen und Panels anwenden.</summary>
    public static void Apply(AppSettings s, DesignPreset p)
    {
        s.Theme = p.Theme;
        s.UseWindowsAccent = p.WindowsAccent;
        foreach (var panel in s.Panels) Apply(panel, p);
    }

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
        panel.Frost, s.UseWindowsAccent, panel.EffectiveBackdropSetting, panel.GlassBlur, panel.GlassSaturation, panel.AccentColor, IsCustom: true);

    private static string DescribeCustom(PanelConfig p) =>
        L.F("Eigenes Design · {0}", PanelBackdrops.Label(p.EffectiveBackdropSetting).Split(' ')[0]) + (p.EffectiveBackdropSetting == PanelBackdrop.Solid ? "" : $", {p.PanelOpacity} %");

    /// <summary>Passt das Panel zur Vorlage? (für die Markierung)</summary>
    public static bool Matches(AppSettings s, PanelConfig x, DesignPreset p) =>
        p.Theme == s.Theme && p.WindowsAccent == s.UseWindowsAccent &&
        string.Equals(x.PanelColor, p.PanelColor, StringComparison.OrdinalIgnoreCase) &&
        x.EffectiveBackdropSetting == p.Backdrop &&
        (p.Backdrop == PanelBackdrop.Solid || x.PanelOpacity == p.Opacity) &&
        string.Equals(x.AccentColor, p.AccentColor, StringComparison.OrdinalIgnoreCase) &&
        (p.Backdrop == PanelBackdrop.Solid || (p.Frost > 0) == (x.Frost > 0));

    /// <summary>Passende Vorlage zum aktuellen Stand (für die Markierung), sonst null.</summary>
    public static DesignPreset? Current(AppSettings s) => All(s).FirstOrDefault(p => s.Panels.Count > 0 && s.Panels.All(x => Matches(s, x, p)));
}
