namespace PowerExec.Core.Updates;

/// <summary>Neuerungen einer Version (für die Karte „Neu in dieser Version“ im Panel).</summary>
public sealed record ReleaseNote(Version Version, string Title, IReadOnlyList<string> Highlights);

public static class ReleaseNotes
{
    public static readonly IReadOnlyList<ReleaseNote> All = new[]
    {
        new ReleaseNote(new Version(0, 17, 0), "Spürbar schneller beim Tippen", new[]
        {
            "Suche im Panel im Mittel drei- bis fünfmal schneller (gemessen mit 500 Einträgen)",
            "Filter wechseln und Weitertippen bauen die Liste nicht mehr komplett neu auf",
            "Kontextmenüs entstehen erst beim Öffnen – weniger Speicher, schnelleres Einblenden",
            "Auswahl, Haken der Mehrfachauswahl und Gruppenkopf bleiben beim Tippen stehen"
        }),
        new ReleaseNote(new Version(0, 16, 2), "Acrylic passt sich dem Panel an", new[]
        {
            "Weichgezeichneter Hintergrund mit abgerundeten Ecken wie das Panel",
            "Hintergrund fährt beim Ein- und Ausfahren mit dem Panel mit"
        }),
        new ReleaseNote(new Version(0, 16, 1), "Feinschliff nach den ersten Rückmeldungen", new[]
        {
            "Vorschläge beim Tippen: #schlagwort, typ:, gruppe:, ist: – nur Vorhandenes, mit Anzahl",
            "Farbwähler für Gruppen- und Panelfarbe; Farbmenü zeigt wieder alle Farben",
            "Gruppen in den Einstellungen per Drag & Drop sortieren",
            "Sprungleiste: zweiter Klick klappt zu; Sprungleiste und Scrollbalken abschaltbar",
            "Mini-Dock ersetzt die Hover-Zone, Anzahl der Favoriten einstellbar",
            "Dateiendungen mit Semikolon getrennt"
        }),
        new ReleaseNote(new Version(0, 16, 0), "Schneller finden, sehen was läuft", new[]
        {
            "Unscharfe Suche mit Hervorhebung, „Beste Treffer“ und Filtern wie typ:ps1, gruppe:, ist:fav",
            "Rückgängig (Strg+Z) nach Ausblenden, Entfernen und Verschieben",
            "Tastatur: Strg+Enter als Admin, Alt+1…9 für Favoriten, F1 zeigt alle Kürzel",
            "Fixierter Gruppenkopf, Sprungleiste und Strg+K für Gruppen",
            "Aktivitäten-Leiste mit Fortschritt; Kachelkarte für Remote-Läufe",
            "Vorschau mit der Leertaste: Code, Parameter, Signatur",
            "Designvorlagen, Windows-Akzentfarbe, Mica und Schriftgröße",
            "Einführungs-Assistent und optionales Mini-Dock am Bildschirmrand"
        }),
    };

    /// <summary>Neuerungen seit <paramref name="lastSeen"/> (exklusive) bis <paramref name="current"/> (inklusive), neueste zuerst.</summary>
    public static IReadOnlyList<ReleaseNote> Since(Version? lastSeen, Version current) =>
        All.Where(n => (lastSeen == null || Normalize(n.Version) > Normalize(lastSeen)) && Normalize(n.Version) <= Normalize(current))
           .OrderByDescending(n => n.Version).ToList();

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static Version? Parse(string? text) => Version.TryParse(text, out var v) ? v : null;
}
