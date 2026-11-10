using PowerExec.Core.Localization;
using System.Globalization;

namespace PowerExec.Core.Config;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

public sealed record ColorPreset(string Name, string Hex);

/// <summary>Farbvorlagen und Farbberechnungen für das Panel.</summary>
public static class PanelColors
{
    /// <summary>Vorlagen; ein leerer Wert steht für die Farbe des Designs.</summary>
    public static readonly IReadOnlyList<ColorPreset> Presets =
    [
        new(L.T("Design"), ""),
        new(L.T("Anthrazit"), "#1E1E1E"),
        new(L.T("Mitternachtsblau"), "#1B2A3D"),
        new(L.T("Petrol"), "#123A40"),
        new(L.T("Tannengrün"), "#1E3328"),
        new(L.T("Aubergine"), "#2E2140"),
        new(L.T("Bordeaux"), "#3A1E26"),
        new(L.T("Schiefer"), "#3B4252"),
        new(L.T("Hellgrau"), "#F3F3F3"),
        new(L.T("Sand"), "#F2EBDD"),
        new(L.T("Eisblau"), "#E3EEF7")
    ];

    /// <summary>Farben wie in den Windows-Einstellungen (Personalisierung › Farben), zeilenweise.</summary>
    public static readonly IReadOnlyList<string> WindowsPalette =
    [
        "#FFB900", "#FF8C00", "#F7630C", "#CA5010", "#DA3B01", "#EF6950", "#D13438", "#FF4343",
        "#E74856", "#E81123", "#EA005E", "#C30052", "#E3008C", "#BF0077", "#C239B3", "#9A0089",
        "#0078D7", "#0063B1", "#8E8CD8", "#6B69D6", "#8764B8", "#744DA9", "#B146C2", "#881798",
        "#0099BC", "#2D7D9A", "#00B7C3", "#038387", "#00B294", "#018574", "#00CC6A", "#10893E",
        "#7A7574", "#5D5A58", "#68768A", "#515C6B", "#567C73", "#486860", "#498205", "#107C10",
        "#767676", "#4C4A48", "#69797E", "#4A5459", "#647C64", "#525E54", "#847545", "#7E735F"
    ];

    /// <summary>Anzahl der zuletzt verwendeten Farben (wie in Windows).</summary>
    public const int RecentCount = 5;

    /// <summary>Farbe vorn in die Liste der zuletzt verwendeten setzen (ohne Doppelte, höchstens <see cref="RecentCount"/>).</summary>
    public static List<string> AddRecent(IEnumerable<string> recent, string hex)
    {
        if (!TryParse(hex, out var c)) return NormalizeRecent(recent);
        return NormalizeRecent(new[] { c.Hex }.Concat(recent));
    }

    /// <summary>Nur gültige Farben, einheitlich geschrieben, ohne Doppelte, höchstens <see cref="RecentCount"/>.</summary>
    public static List<string> NormalizeRecent(IEnumerable<string>? recent) =>
        (recent ?? Enumerable.Empty<string>()).Select(h => TryParse(h, out var c) ? c.Hex : null).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(RecentCount).ToList();

    /// <summary>Gruppen- und Schlagwortfarben: kräftig genug für Symbole, lesbar in hell und dunkel.</summary>
    public static readonly IReadOnlyList<ColorPreset> AccentPresets =
    [
        new(L.T("Blau"), "#3B82F6"),
        new(L.T("Himmel"), "#0EA5E9"),
        new(L.T("Türkis"), "#14B8A6"),
        new(L.T("Grün"), "#22C55E"),
        new(L.T("Limette"), "#84CC16"),
        new(L.T("Gelb"), "#EAB308"),
        new(L.T("Orange"), "#F97316"),
        new(L.T("Rot"), "#EF4444"),
        new(L.T("Pink"), "#EC4899"),
        new(L.T("Violett"), "#8B5CF6"),
        new(L.T("Indigo"), "#6366F1"),
        new(L.T("Grau"), "#94A3B8")
    ];

    /// <summary>Liest "#RRGGBB", "RRGGBB" oder "#RGB".</summary>
    public static bool TryParse(string? text, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>Relative Leuchtdichte nach WCAG (0 = schwarz, 1 = weiß).</summary>
    public static double Luminance(Rgb c)
    {
        static double Channel(byte v)
        {
            var x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    public static double Contrast(Rgb a, Rgb b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Helle Hintergründe brauchen dunkle Schrift: ist Schwarz kontrastreicher als Weiß?</summary>
    public static bool IsLight(Rgb c) => Contrast(c, new Rgb(0, 0, 0)) > Contrast(c, new Rgb(255, 255, 255));

    /// <summary>Mischt <paramref name="c"/> mit <paramref name="other"/> (amount 0..1).</summary>
    public static Rgb Blend(Rgb c, Rgb other, double amount)
    {
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * Math.Clamp(amount, 0, 1));
        return new Rgb(Mix(c.R, other.R), Mix(c.G, other.G), Mix(c.B, other.B));
    }

    /// <summary>Typische Fläche des hellen bzw. dunklen Panels (Bezug für <see cref="Readable"/>).</summary>
    public static readonly Rgb LightSurface = new(0xF9, 0xF9, 0xF9), DarkSurface = new(0x20, 0x20, 0x20);

    /// <summary>
    /// Farbe für Symbole und Rahmen auf dem Panel: im hellen Design so weit abgedunkelt, im dunklen so weit aufgehellt,
    /// dass sie mindestens <paramref name="minContrast"/> zur Fläche hat (WCAG: 3:1 für Symbole). Kräftige Farben bleiben unverändert.
    /// </summary>
    public static Rgb Readable(Rgb color, bool darkSurface, double minContrast = 3.0)
    {
        var surface = darkSurface ? DarkSurface : LightSurface;
        var toward = darkSurface ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0);
        var c = color;
        for (int i = 0; i < 20 && Contrast(c, surface) < minContrast; i++) c = Blend(c, toward, 0.08);
        return c;
    }

    /// <summary>Abgeleitete Flächen: Hover, Auswahl und Rahmen leicht heller (dunkle Basis) bzw. dunkler (helle Basis).</summary>
    public static (Rgb Hover, Rgb Selected, Rgb Border, Rgb Input) Derive(Rgb baseColor)
    {
        var toward = IsLight(baseColor) ? new Rgb(0, 0, 0) : new Rgb(255, 255, 255);
        return (Blend(baseColor, toward, 0.06), Blend(baseColor, toward, 0.10), Blend(baseColor, toward, 0.16),
                IsLight(baseColor) ? Blend(baseColor, new Rgb(255, 255, 255), 0.7) : Blend(baseColor, new Rgb(0, 0, 0), 0.25));
    }
}
