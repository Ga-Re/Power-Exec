using PowerExec.Core.Config;
using PowerExec.Core.Localization;
using PowerExec.UI.Infrastructure;

namespace PowerExec.UI.ViewModels;

/// <summary>Ein Eintrag der Farbauswahl: gespeicherter Wert (#RRGGBB, „design:N“, „none“ oder leer), Name und Farbpunkt.</summary>
public sealed record ColorOption(string Key, string Name, string Swatch)
{
    public ArgbColor? SwatchColor { get; } = ArgbColor.FromHex(Swatch);
}

/// <summary>
/// Farbauswahl für Gruppen und Schlagworte: „vom Design (automatisch)“, die Farben aus der Reihe des aktiven Designs
/// (gespeichert wird die Stelle, damit die Farbe mit dem Design wechselt), feste Akzentfarben, eine eigene Farbe und
/// „keine Farbe“.
/// </summary>
public static class ColorChoices
{
    /// <summary>Name der Farbe Nr. <paramref name="index"/> (0-basiert) aus der Reihe des Designs.</summary>
    public static string DesignColorName(int index) => L.F("Design color {0}", index + 1);

    /// <summary>Text für „automatisch“: bei Untergruppen die Farbe der übergeordneten Gruppe.</summary>
    public static string AutoName(bool inheritsFromParent) => inheritsFromParent ? L.T("Like the parent group") : L.T("From the design (automatic)");

    /// <summary>Alle Einträge der Auswahl; die eigene Farbe kommt dazu, wenn sie keine Vorgabe ist (sonst bliebe die Auswahl leer).</summary>
    /// <param name="s">Einstellungen (Farbreihe des aktiven Designs).</param>
    /// <param name="own">Gespeicherter Wert.</param>
    /// <param name="automatic">Farbe, die „automatisch“ gerade ergibt (leer = keine).</param>
    /// <param name="inheritsFromParent">Untergruppe: „automatisch“ heißt „wie die übergeordnete Gruppe“.</param>
    public static IReadOnlyList<ColorOption> For(AppSettings s, string own, string automatic, bool inheritsFromParent = false)
    {
        var list = new List<ColorOption> { new(string.Empty, AutoName(inheritsFromParent), automatic) };
        var palette = Appearance.GroupPalette(s, Appearance.IsDarkNow);
        list.AddRange(palette.Select((hex, i) => new ColorOption(Appearance.DesignColor(i), DesignColorName(i), hex)));
        list.AddRange(PanelColors.AccentPresets.Select(p => new ColorOption(p.Hex, p.Name, p.Hex)));
        if (Appearance.DesignColorIndex(own) is { } index && index >= palette.Count)
            list.Add(new ColorOption(own, DesignColorName(index), Appearance.SeriesColor(s, index)));
        else if (PanelColors.TryParse(own, out var c) && !list.Any(o => o.Key.Equals(c.Hex, StringComparison.OrdinalIgnoreCase)))
            list.Add(new ColorOption(c.Hex, L.T("Custom ") + c.Hex, c.Hex));
        list.Add(new ColorOption(Appearance.NoColor, L.T("No color"), string.Empty));
        return list;
    }

    /// <summary>Farbe für den Farbwähler: der gespeicherte Wert als #RRGGBB (Verweise auf das Design aufgelöst).</summary>
    public static string PickerStart(AppSettings s, string own) => Appearance.ResolveColorRef(s, own) ?? string.Empty;
}
