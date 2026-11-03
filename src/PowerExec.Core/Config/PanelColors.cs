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
        new("Design", ""),
        new("Anthrazit", "#1E1E1E"),
        new("Mitternachtsblau", "#1B2A3D"),
        new("Petrol", "#123A40"),
        new("Tannengrün", "#1E3328"),
        new("Aubergine", "#2E2140"),
        new("Bordeaux", "#3A1E26"),
        new("Schiefer", "#3B4252"),
        new("Hellgrau", "#F3F3F3"),
        new("Sand", "#F2EBDD"),
        new("Eisblau", "#E3EEF7")
    ];

    /// <summary>Gruppen- und Schlagwortfarben: kräftig genug für Symbole, lesbar in hell und dunkel.</summary>
    public static readonly IReadOnlyList<ColorPreset> AccentPresets =
    [
        new("Blau", "#3B82F6"),
        new("Himmel", "#0EA5E9"),
        new("Türkis", "#14B8A6"),
        new("Grün", "#22C55E"),
        new("Limette", "#84CC16"),
        new("Gelb", "#EAB308"),
        new("Orange", "#F97316"),
        new("Rot", "#EF4444"),
        new("Pink", "#EC4899"),
        new("Violett", "#8B5CF6"),
        new("Indigo", "#6366F1"),
        new("Grau", "#94A3B8")
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

    /// <summary>Abgeleitete Flächen: Hover, Auswahl und Rahmen leicht heller (dunkle Basis) bzw. dunkler (helle Basis).</summary>
    public static (Rgb Hover, Rgb Selected, Rgb Border, Rgb Input) Derive(Rgb baseColor)
    {
        var toward = IsLight(baseColor) ? new Rgb(0, 0, 0) : new Rgb(255, 255, 255);
        return (Blend(baseColor, toward, 0.06), Blend(baseColor, toward, 0.10), Blend(baseColor, toward, 0.16),
                IsLight(baseColor) ? Blend(baseColor, new Rgb(255, 255, 255), 0.7) : Blend(baseColor, new Rgb(0, 0, 0), 0.25));
    }
}
