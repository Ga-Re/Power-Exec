namespace PowerExec.Core.Updates;

/// <summary>Neuerungen einer Version (für die Karte „Neu in dieser Version“ im Panel).</summary>
public sealed record ReleaseNote(Version Version, string Title, IReadOnlyList<string> Highlights);

public static class ReleaseNotes
{
    public static readonly IReadOnlyList<ReleaseNote> All = new[]
    {
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
