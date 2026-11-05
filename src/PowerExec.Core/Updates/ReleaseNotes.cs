namespace PowerExec.Core.Updates;

/// <summary>Neuerungen einer Version (für die Karte „Neu in dieser Version“ im Panel).</summary>
public sealed record ReleaseNote(Version Version, string Title, IReadOnlyList<string> Highlights);

public static class ReleaseNotes
{
    public static readonly IReadOnlyList<ReleaseNote> All = new[]
    {
        new ReleaseNote(new Version(0, 19, 2), "Weichzeichner repariert", new[]
        {
            "Aero-Blur bleibt nach dem Einfahren erhalten; kein unscharfer Streifen mehr neben dem Panel",
            "Kein Schließen-Knopf von Windows mehr am Panel",
            "Deckkraft steht direkt bei Hintergrund und Milchglas, jetzt ab 20 %; mit Weichzeichner wird sie auf 70 % gesetzt, wenn sie auf 100 % stand"
        }),
        new ReleaseNote(new Version(0, 19, 1), "Hintergrund wählen: Aero, Acrylic, Mica oder Klarglas", new[]
        {
            "Hintergrund hinter dem Panel wählbar: Klarglas, Aero-Blur, Acrylic oder Mica – Ecken rundet Windows 11 jetzt selbst und glatt",
            "Milchglas-Regler statt Stufen; Windows 10 bekommt bei Acrylic und Mica automatisch Aero-Blur",
            "„Nach Profil fragen“ greift jetzt auch beim Start aus dem Panel, mit Alt+1 … 9 und im Spotlight",
            "Feste Gruppen (Zuletzt ausgeführt, Favoriten, Eigene Einträge) lassen sich auch im Panel verschieben",
            "Liste ↔ Raster je Gruppe ändert die Höhe weich; Schieberegler bei „Zuletzt ausgeführt“ wieder bedienbar"
        }),
        new ReleaseNote(new Version(0, 19, 0), "Dynamische Rechnergruppen, feste Gruppen frei anordnen", new[]
        {
            "Rechnergruppen dynamisch aus dem AD: Regeln wie „Betriebssystem enthält Server“, in Blöcken mit UND/ODER kombiniert; Aktualisierung beim Start und regelmäßig",
            "Zuletzt ausgeführt, Favoriten und Eigene Einträge stehen mit Schloss in der Gruppenliste, lassen sich verschieben und je Arbeitsbereich ein- oder ausblenden",
            "Laufende Scripte: Ende wird erkannt, auch wenn das Fenster offen bleibt; „Erneut starten“ nach dem Ende; „läuft“ direkt am Eintrag",
            "Beim Start nach dem Profil fragen (global oder je Eintrag) – im selben Fenster wie die Werte für {{Platzhalter}}",
            "Gruppen klappen weich auf und zu, Knöpfe im Gruppenkopf blenden ein, „Zuletzt ausgeführt“ zeigt die Zeit",
            "Stärke des Weichzeichners einstellbar, Deckkraft wieder genau wie eingestellt; angeheftetes Panel überdeckt die Einstellungen nicht mehr",
            "Ordner ins Panel ziehen: als Gruppe oder als Link; Schlagworte mit eigener Farbe und einzeln von Einträgen entfernbar"
        }),
        new ReleaseNote(new Version(0, 18, 0), "Filtern aus dem Suchfeld, besser lesbar in Hell", new[]
        {
            "Klick ins Suchfeld zeigt die Filterarten (Schlagwort, Typ, Gruppe, Merkmal) – die Auswahl listet die vorhandenen Werte; die Filterknöpfe unter der Suche entfallen",
            "Helles Design: offene Fenster wechseln sofort mit, Gruppenfarben und Symbole mit mehr Kontrast, helle Panels kaum noch durchscheinend",
            "Gruppenmenü aufgeräumt: „Neue Untergruppe“ oben, „Eigene Farbe“ und „Farbe entfernen“ im Untermenü Farbe, ohne „Nach oben/unten“",
            "Suchfeld runder"
        }),
        new ReleaseNote(new Version(0, 17, 2), "Gruppen anlegen und ordnen", new[]
        {
            "Neue Gruppe mit Symbol oder Bild, Farbe, Darstellung und optional einem Ordner (wird zur smarten Gruppe) in einem Dialog",
            "Rechtsklick auf eine Gruppe legt eine Untergruppe an, auf freie Fläche eine neue Gruppe unten",
            "Einstellungen › Gruppen: Untergruppen im Rahmen ihrer Gruppe, Verschieben und Verschachteln wie im Panel",
            "Panel: Menü-Knopf im Gruppenkopf erst beim Darüberfahren; beim Hineinziehen leuchtet der Gruppenrahmen"
        }),
        new ReleaseNote(new Version(0, 17, 1), "Gruppen verschieben: sehen, wo sie landen", new[]
        {
            "Beim Ziehen einer Gruppe öffnet sich an der Zielstelle eine Lücke mit Platzhalter – im Panel und in den Einstellungen",
            "Zwischen zwei Gruppen wählt seitliches Ziehen die Ebene: nach rechts tiefer, nach links höher",
            "Ziehbild am Mauszeiger mit Name, Farbe und Zahl der Untergruppen",
            "Unter Panel › Verhalten einstellbar: Ziehbild, weiche Bewegung, Inhalte zuklappen, Begründung an ungültigen Stellen"
        }),
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
