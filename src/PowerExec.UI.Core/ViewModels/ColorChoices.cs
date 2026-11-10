using PowerExec.Core.Localization;
using PowerExec.Core.Config;

namespace PowerExec.ViewModels;

/// <summary>Farbauswahl für Gruppen und Schlagworte (Auswahlliste: Wert = #RRGGBB, Text = Name).</summary>
public static class ColorChoices
{
    /// <summary>„Keine“ plus die Akzent-Vorlagen.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> WithNone { get; } =
        new[] { new KeyValuePair<string, string>(string.Empty, L.T("Keine / wie übergeordnet")) }
            .Concat(PanelColors.AccentPresets.Select(p => new KeyValuePair<string, string>(p.Hex, p.Name))).ToList();

    /// <summary>Liste plus die eigene Farbe, falls sie keine Vorlage ist (sonst bliebe die Auswahl leer).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Including(string own) =>
        own.Length > 0 && !WithNone.Any(c => c.Key.Equals(own, StringComparison.OrdinalIgnoreCase))
            ? WithNone.Append(new KeyValuePair<string, string>(own, L.T("Eigene ") + own)).ToList()
            : WithNone;
}
