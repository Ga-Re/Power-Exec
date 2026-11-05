namespace PowerExec.Core.Config;

/// <summary>Hintergrund normaler Fenster (Einstellungen, Dialoge).</summary>
public enum WindowBackdrop { Solid, Mica }

/// <summary>Akzentfarben für das Design, abgeleitet aus einer Grundfarbe (z. B. der Windows-Akzentfarbe).</summary>
public sealed record AccentColors(Rgb Accent, Rgb Hover, Rgb Pressed, Rgb OnAccent, Rgb Highlight);

/// <summary>Eine Designvorlage: Design, Panelfarbe, Deckkraft, Acrylic und Akzent in einem Klick.</summary>
public sealed record DesignPreset(string Key, string Name, string Description, ThemeMode Theme, string PanelColor, int Opacity, bool Acrylic, bool WindowsAccent, WindowBackdrop Backdrop);

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

    public static readonly IReadOnlyList<DesignPreset> Presets = new[]
    {
        new DesignPreset("fluent", "Fluent", "Wie Windows: Design und Akzentfarbe vom System", ThemeMode.System, string.Empty, 100, false, true, WindowBackdrop.Mica),
        new DesignPreset("glass", "Glas", "Dunkel, durchscheinend mit Acrylic", ThemeMode.Dark, string.Empty, 80, true, true, WindowBackdrop.Mica),
        new DesignPreset("graphite", "Graphit", "Dunkel und deckend, klassische Akzentfarbe", ThemeMode.Dark, "#1C1C1C", 100, false, false, WindowBackdrop.Solid),
        new DesignPreset("paper", "Papier", "Hell und ruhig", ThemeMode.Light, "#FAFAFA", 100, false, false, WindowBackdrop.Solid),
        new DesignPreset("midnight", "Mitternacht", "Dunkelblau getönt", ThemeMode.Dark, "#14213D", 95, false, false, WindowBackdrop.Solid),
    };

    /// <summary>Vorlage auf Einstellungen und Panels anwenden.</summary>
    public static void Apply(AppSettings s, DesignPreset p)
    {
        s.Theme = p.Theme;
        s.UseWindowsAccent = p.WindowsAccent;
        s.WindowBackdrop = p.Backdrop;
        foreach (var panel in s.Panels)
        {
            panel.PanelColor = p.PanelColor;
            panel.PanelOpacity = p.Opacity;
            panel.Acrylic = p.Acrylic;
            panel.Backdrop = p.Acrylic ? PanelBackdrop.Acrylic : PanelBackdrop.Clear;
        }
    }

    /// <summary>Passende Vorlage zum aktuellen Stand (für die Markierung), sonst null.</summary>
    public static DesignPreset? Current(AppSettings s) => Presets.FirstOrDefault(p =>
        p.Theme == s.Theme && p.WindowsAccent == s.UseWindowsAccent && p.Backdrop == s.WindowBackdrop &&
        s.Panels.All(x => string.Equals(x.PanelColor, p.PanelColor, StringComparison.OrdinalIgnoreCase) && x.PanelOpacity == p.Opacity && (x.EffectiveBackdropSetting != PanelBackdrop.Clear) == p.Acrylic));
}
