using PowerExec.Core.Localization;
namespace PowerExec.Core.Updates;

/// <summary>Neuerungen einer Version (für die Karte „Neu in dieser Version“ im Panel).</summary>
public sealed record ReleaseNote(Version Version, string Title, IReadOnlyList<string> Highlights);

public static class ReleaseNotes
{
    public static readonly IReadOnlyList<ReleaseNote> All = new[]
    {
        new ReleaseNote(new Version(0, 28, 0), L.T("Englische Oberfläche"), new[]
        {
            L.T("Power-Exec gibt es jetzt auch auf Englisch: die Sprache folgt Windows (Deutsch bei deutschem Windows, sonst Englisch) und lässt sich unter Einstellungen › Allgemein › Sprache umstellen"),
            L.T("Alle Fenster, Meldungen, Werkzeug-Vorlagen und Versionshinweise sind übersetzt; die Suche versteht englische Filter wie type:, group: und is: – die deutschen gehen weiter"),
            L.T("Nach dem Umstellen der Sprache startet Power-Exec auf Knopfdruck neu")
        }),
        new ReleaseNote(new Version(0, 27, 0), L.T("Marktplatz aus Git-Ordnern, Variablen-Tabelle, Snackbars"), new[]
        {
            L.T("Marktplatz: statt Paketen Ordner aus dem Git-Repository – aufklappbar, mit Tabs Einträge · Scripte · Designs · Installiert und kompakter Liste"),
            L.T("Ganze Ordner oder einzelne Dateien per Häkchen herunterladen oder deinstallieren; Einträge und Scripte landen in den eigenen Einträgen in der Gruppe deiner Wahl (vorgeschlagen: der Ordnername)"),
            L.T("Deinstallieren entfernt Einträge, Designs und Script-Dateien wieder; selbst geänderte Einträge nur nach Rückfrage. Updates bleiben in ihrer Gruppe"),
            L.T("Variablen als kompakte Tabelle mit Suche, Häkchen und eigenem Bearbeiten-Dialog; eingebaute Variablen heißen jetzt {{$Date}}, {{$Time}}, {{$User}}, {{$Computer}} und {{$Clipboard}}"),
            L.T("Erfolgsmeldungen erscheinen kurz als Snackbar und verschwinden wieder; Fehler bleiben stehen"),
            L.T("Der Favoriten-Stern folgt der Akzentfarbe")
        }),
        new ReleaseNote(new Version(0, 26, 0), L.T("Eigene Marktplatz-Quellen"), new[]
        {
            L.T("Marktplatz › Quellen: weitere Marktplätze einfach per Repository-URL hinzufügen (GitHub oder GitLab, auch Unterordner) – z. B. den Katalog deines Teams"),
            L.T("Fremde Quellen werden vor der ersten Installation einmal als vertrauenswürdig bestätigt; danach zeigt die Liste ihren Stand, der Quellenfilter grenzt die Elemente ein"),
            L.T("Eine nicht erreichbare Quelle hält die anderen nicht auf; Entfernen einer Quelle lässt bereits Installiertes stehen")
        }),
        new ReleaseNote(new Version(0, 25, 0), L.T("Variablen, Marktplatz und aufgeräumte Einstellungen"), new[]
        {
            L.T("Neu: Marktplatz (Inhalte › Marktplatz) – Pakete (z. B. Matrix42, Recherche), Links (Google, Microsoft Learn, VirusTotal, IP-Whois, GitHub/GitLab-Issues), Designs und die Beispielscripte mit einem Klick installieren; vorher zeigt eine Vorschau den Inhalt, benötigte Variablen werden abgefragt, Änderungen erscheinen als „Update verfügbar“"),
            L.T("Neu: Variablen (Inhalte › Variablen). {{$CompanyName}} wird in Texten, Links, Befehlen, Zielen, PowerShell-Code und Script-Parametern ohne Rückfrage eingesetzt – als fester Text, je Arbeitsbereich, geheim (verschlüsselt), aus einer Umgebungsvariable oder als Ergebnis eines PowerShell-Befehls (mit Zwischenspeicher)"),
            L.T("{{$Datum}}, {{$Uhrzeit}}, {{$Benutzer}}, {{$Computer}} und {{$Zwischenablage}} sind eingebaut, das Datums- und Uhrzeitformat ist einstellbar; normale {{Platzhalter}} werden weiter vor dem Start abgefragt"),
            L.T("Im Eintrags-Editor: Vorschläge beim Tippen von {{$, Einfügen per Klick und ein Hinweis bei Variablen, die es nicht gibt; die Vorschau im Panel zeigt die eingesetzten Werte"),
            L.T("Einstellungen: einheitliche Karten, kompakte Typ-Filter unter „Eigene Einträge“, farbige Rückmeldungen (InfoBar), Akzentbalken in der Navigation und „Alle Änderungen gespeichert“ in der Fußzeile")
        }),
        new ReleaseNote(new Version(0, 24, 0), L.T("Neue Designs, Vollton und ruhigere Gruppen"), new[]
        {
            L.T("Designvorlagen einklappbar und erweitert: Glas schaltet jetzt den echten Weichzeichner ein (Blur 60, Sättigung 125 %), neu sind OLED, Nordic Frost, Fluent Clean und Espresso – und eigene Designs lassen sich speichern"),
            L.T("Neuer Hintergrund „Vollton“ (deckend, ohne Transparenz); unter Panelfarbe wählst du, ob Hover-Zone, Auswahl und Knöpfe der Windows-Akzentfarbe, einer eigenen Akzentfarbe oder der Panelfarbe folgen"),
            L.T("Gruppen-Stil „Akzentstreifen“: ruhige, halbtransparente Karten mit Farbstreifen; Kacheln schweben dezent, Typ-Kürzel als Pillen; Elementgröße je Gruppe im Gruppenmenü; der fixierte Gruppenkopf sitzt genau auf der Gruppe"),
            L.T("Einstellungen aufgeräumt: Mini-Dock, Hover-Zone und Panel-Abmessungen als Ausklappbereiche, Scrollbalken nur beim Scrollen; ein angeheftetes, per Klick eingefahrenes Panel fährt beim Darüberfahren wieder aus")
        }),
        new ReleaseNote(new Version(0, 23, 0), L.T("Rechnergruppen aus Listen, OU-Auswahl und Export"), new[]
        {
            L.T("Rechnergruppen: Quelle „Dynamisch aus Liste“ – eine Namensliste nach Namen oder eine CSV nach beliebigen Spalten filtern; die Liste wird beim Synchronisieren neu gelesen. „Gruppe aus Liste importieren …“ legt so eine Gruppe direkt an"),
            L.T("Jede Rechnergruppe hat in der Kopfzeile Synchronisieren und Exportieren (CSV oder Text)"),
            L.T("Dynamisch aus AD: „OU wählen …“ zeigt den ganzen OU-Baum mit Suche und setzt die gewählte OU in „Ab OU“ ein"),
            L.T("Beispielscripte anlegen findest du jetzt unter Hilfe › Beispiele; das Panel sitzt beim Ausfahren genauer und prüft das Auto-Einfahren nur noch, wenn der Mauszeiger das Panel verlassen hat")
        }),
        new ReleaseNote(new Version(0, 22, 3), L.T("Feinschliff: Ausfahren, Auswahllisten, Einstellungen"), new[]
        {
            L.T("Panel fährt zügiger und gleichmäßiger aus; geparkt wird es ohne erneutes Platzieren gestartet"),
            L.T("Auswahllisten zeigen kein überflüssiges Eingabefeld mehr; das Panel zeigt das Programmsymbol"),
            L.T("Einstellungen: Panelfarbe und Hintergrund als Ausklappbereiche – darunter je nach Hintergrund Blur-Stärke, Sättigung, Milchglas und Deckkraft"),
            L.T("Glas: Inhalt sitzt nach einem Wechsel des Hintergrunds wieder genau im Panel")
        }),
        new ReleaseNote(new Version(0, 22, 2), L.T("Ruhigeres Ausfahren, Details bei Rastergruppen"), new[]
        {
            L.T("Das Panel fährt sanfter aus: Fokus und Hintergrundprüfungen folgen erst nach der Bewegung, beim Gleiten wird nichts unnötig neu vermessen oder gezeichnet"),
            L.T("Steht das Panel auf „Liste“ und eine Gruppe auf „Raster“, erscheinen die Details des ausgewählten Eintrags jetzt unten im Panel")
        }),
        new ReleaseNote(new Version(0, 22, 1), L.T("Fehlerbehebung beim Ausführen mit Glas"), new[]
        {
            L.T("Mit dem Hintergrund „Glas“ erschien beim Ausführen eines Scripts aus dem Panel die Meldung „Value cannot be null (defaultDestinationValue)“ – behoben"),
            L.T("Ersteinrichtung: Beispielscripte lassen sich zusätzlich zum eigenen Ordner anlegen (Gruppe „Beispiele“)")
        }),
        new ReleaseNote(new Version(0, 22, 0), L.T("Flüssiger: Panel passt sich direkt an, Glas gleitet ruhig"), new[]
        {
            L.T("Geänderte Einstellungen wirken sofort im offenen Panel – ohne Flackern und ohne Neuaufbau (nur beim Wechsel des Designs oder zwischen Klarglas und Glas wird es neu erzeugt)"),
            L.T("Glas: das Panel fährt sanft an und bleibt eingefahren hinter dem Rand bereit – kein Ruckeln mehr beim Ausfahren"),
            L.T("Einstellungen: Bereiche klappen weich auf und zu, der Auswahlbalken gleitet zum neuen Menüpunkt"),
            L.T("Acht Beispielscripte (Systeminfo, Netzwerktest, große Dateien, installierte Programme, Fehlerprotokoll, Temp aufräumen, Kennwörter, Speicherplatz) – unter Inhalte › Gruppen mit „Beispielscripte anlegen“")
        }),
        new ReleaseNote(new Version(0, 21, 0), L.T("Glas mit eigenem Weichzeichner, neue Einstellungen"), new[]
        {
            L.T("Neuer Hintergrund „Glas“ (Windows 11): der Desktop hinter dem Panel wird weichgezeichnet und mit der Panelfarbe getönt – Blur-Stärke und Sättigung einstellbar. Aero-Blur, Acrylic und Mica sind entfallen, Klarglas bekommt ein ruhigeres Milchglas"),
            L.T("Einstellungen mit Navigationsleiste: einklappbarer Rand, einklappbare Bereiche und Zurück; die Panelfarbe wählst du wie die Akzentfarbe in Windows"),
            L.T("Vorschau (Leertaste) in einem eigenen Fenster neben dem Panel – sie folgt dem ausgewählten Eintrag"),
            L.T("Angeheftetes Panel per Klick auf die Hover-Zone ein- und ausfahren; Ordner als Gruppe ablegen öffnet gleich den Gruppendialog mit Farbe und Darstellung"),
            L.T("Beim Ändern von Einstellungen bleibt das Panel sichtbar und behält seine Größe; das Tastenkürzel (Strg+Alt+Leertaste) lässt sich schon bei der Einrichtung festlegen; der Installer folgt hell/dunkel von Windows")
        }),
        new ReleaseNote(new Version(0, 20, 0), L.T("Neues Aussehen im Stil von Windows 11"), new[]
        {
            L.T("Schaltflächen, Schalter, Eingabefelder, Auswahllisten, Menüs und Bildlaufleisten im Fluent-Design – in den Einstellungen, allen Dialogen, im Panel, in der Spotlight-Suche und im Mini-Dock"),
            L.T("Hell, Dunkel, Kontrastdesign und Akzentfarbe gelten für alle Bedienelemente und wechseln auch bei offenen Fenstern mit; im Panel richten sie sich nach der Panelfarbe"),
            L.T("Unten im Panel steht, welche Version läuft und ob sie installiert oder portabel ist")
        }),
        new ReleaseNote(new Version(0, 19, 5), L.T("Weichzeichner auch an Taskleiste und zweitem Monitor flüssig, .NET 10"), new[]
        {
            L.T("Liegt die Taskleiste oder ein weiterer Monitor an der Panelkante, gleitet das Panel jetzt ebenfalls als Ganzes herein und wird am Rand des Arbeitsbereichs abgeschnitten – kein Wachsen mehr, kein Ruckeln, nicht mehr über der Taskleiste"),
            L.T("Läuft jetzt auf .NET 10 (Langzeitsupport bis 2028) und PowerShell 7.6")
        }),
        new ReleaseNote(new Version(0, 19, 4), L.T("Flüssiger mit Weichzeichner"), new[]
        {
            L.T("Mit Aero-Blur, Acrylic oder Mica wird das Panel beim Ein- und Ausfahren als Ganzes verschoben statt neu vermessen – deutlich flüssiger (bei einem weiteren Bildschirm hinter dem Rand wächst es wie bisher)")
        }),
        new ReleaseNote(new Version(0, 19, 3), L.T("Weichzeichner fährt mit dem Panel"), new[]
        {
            L.T("Aero-Blur, Acrylic und Mica fahren genau mit dem Panel ein und aus – kein Hintergrund mehr vor oder nach dem Panel")
        }),
        new ReleaseNote(new Version(0, 19, 2), L.T("Weichzeichner repariert"), new[]
        {
            L.T("Aero-Blur bleibt nach dem Einfahren erhalten; kein unscharfer Streifen mehr neben dem Panel"),
            L.T("Kein Schließen-Knopf von Windows mehr am Panel"),
            L.T("Deckkraft steht direkt bei Hintergrund und Milchglas, jetzt ab 20 %; mit Weichzeichner wird sie auf 70 % gesetzt, wenn sie auf 100 % stand")
        }),
        new ReleaseNote(new Version(0, 19, 1), L.T("Hintergrund wählen: Aero, Acrylic, Mica oder Klarglas"), new[]
        {
            L.T("Hintergrund hinter dem Panel wählbar: Klarglas, Aero-Blur, Acrylic oder Mica – Ecken rundet Windows 11 jetzt selbst und glatt"),
            L.T("Milchglas-Regler statt Stufen; Windows 10 bekommt bei Acrylic und Mica automatisch Aero-Blur"),
            L.T("„Nach Profil fragen“ greift jetzt auch beim Start aus dem Panel, mit Alt+1 … 9 und im Spotlight"),
            L.T("Feste Gruppen (Zuletzt ausgeführt, Favoriten, Eigene Einträge) lassen sich auch im Panel verschieben"),
            L.T("Liste ↔ Raster je Gruppe ändert die Höhe weich; Schieberegler bei „Zuletzt ausgeführt“ wieder bedienbar")
        }),
        new ReleaseNote(new Version(0, 19, 0), L.T("Dynamische Rechnergruppen, feste Gruppen frei anordnen"), new[]
        {
            L.T("Rechnergruppen dynamisch aus dem AD: Regeln wie „Betriebssystem enthält Server“, in Blöcken mit UND/ODER kombiniert; Aktualisierung beim Start und regelmäßig"),
            L.T("Zuletzt ausgeführt, Favoriten und Eigene Einträge stehen mit Schloss in der Gruppenliste, lassen sich verschieben und je Arbeitsbereich ein- oder ausblenden"),
            L.T("Laufende Scripte: Ende wird erkannt, auch wenn das Fenster offen bleibt; „Erneut starten“ nach dem Ende; „läuft“ direkt am Eintrag"),
            L.T("Beim Start nach dem Profil fragen (global oder je Eintrag) – im selben Fenster wie die Werte für {{Platzhalter}}"),
            L.T("Gruppen klappen weich auf und zu, Knöpfe im Gruppenkopf blenden ein, „Zuletzt ausgeführt“ zeigt die Zeit"),
            L.T("Stärke des Weichzeichners einstellbar, Deckkraft wieder genau wie eingestellt; angeheftetes Panel überdeckt die Einstellungen nicht mehr"),
            L.T("Ordner ins Panel ziehen: als Gruppe oder als Link; Schlagworte mit eigener Farbe und einzeln von Einträgen entfernbar")
        }),
        new ReleaseNote(new Version(0, 18, 0), L.T("Filtern aus dem Suchfeld, besser lesbar in Hell"), new[]
        {
            L.T("Klick ins Suchfeld zeigt die Filterarten (Schlagwort, Typ, Gruppe, Merkmal) – die Auswahl listet die vorhandenen Werte; die Filterknöpfe unter der Suche entfallen"),
            L.T("Helles Design: offene Fenster wechseln sofort mit, Gruppenfarben und Symbole mit mehr Kontrast, helle Panels kaum noch durchscheinend"),
            L.T("Gruppenmenü aufgeräumt: „Neue Untergruppe“ oben, „Eigene Farbe“ und „Farbe entfernen“ im Untermenü Farbe, ohne „Nach oben/unten“"),
            L.T("Suchfeld runder")
        }),
        new ReleaseNote(new Version(0, 17, 2), L.T("Gruppen anlegen und ordnen"), new[]
        {
            L.T("Neue Gruppe mit Symbol oder Bild, Farbe, Darstellung und optional einem Ordner (wird zur smarten Gruppe) in einem Dialog"),
            L.T("Rechtsklick auf eine Gruppe legt eine Untergruppe an, auf freie Fläche eine neue Gruppe unten"),
            L.T("Einstellungen › Gruppen: Untergruppen im Rahmen ihrer Gruppe, Verschieben und Verschachteln wie im Panel"),
            L.T("Panel: Menü-Knopf im Gruppenkopf erst beim Darüberfahren; beim Hineinziehen leuchtet der Gruppenrahmen")
        }),
        new ReleaseNote(new Version(0, 17, 1), L.T("Gruppen verschieben: sehen, wo sie landen"), new[]
        {
            L.T("Beim Ziehen einer Gruppe öffnet sich an der Zielstelle eine Lücke mit Platzhalter – im Panel und in den Einstellungen"),
            L.T("Zwischen zwei Gruppen wählt seitliches Ziehen die Ebene: nach rechts tiefer, nach links höher"),
            L.T("Ziehbild am Mauszeiger mit Name, Farbe und Zahl der Untergruppen"),
            L.T("Unter Panel › Verhalten einstellbar: Ziehbild, weiche Bewegung, Inhalte zuklappen, Begründung an ungültigen Stellen")
        }),
        new ReleaseNote(new Version(0, 17, 0), L.T("Spürbar schneller beim Tippen"), new[]
        {
            L.T("Suche im Panel im Mittel drei- bis fünfmal schneller (gemessen mit 500 Einträgen)"),
            L.T("Filter wechseln und Weitertippen bauen die Liste nicht mehr komplett neu auf"),
            L.T("Kontextmenüs entstehen erst beim Öffnen – weniger Speicher, schnelleres Einblenden"),
            L.T("Auswahl, Haken der Mehrfachauswahl und Gruppenkopf bleiben beim Tippen stehen")
        }),
        new ReleaseNote(new Version(0, 16, 2), L.T("Acrylic passt sich dem Panel an"), new[]
        {
            L.T("Weichgezeichneter Hintergrund mit abgerundeten Ecken wie das Panel"),
            L.T("Hintergrund fährt beim Ein- und Ausfahren mit dem Panel mit")
        }),
        new ReleaseNote(new Version(0, 16, 1), L.T("Feinschliff nach den ersten Rückmeldungen"), new[]
        {
            L.T("Vorschläge beim Tippen: #schlagwort, typ:, gruppe:, ist: – nur Vorhandenes, mit Anzahl"),
            L.T("Farbwähler für Gruppen- und Panelfarbe; Farbmenü zeigt wieder alle Farben"),
            L.T("Gruppen in den Einstellungen per Drag & Drop sortieren"),
            L.T("Sprungleiste: zweiter Klick klappt zu; Sprungleiste und Scrollbalken abschaltbar"),
            L.T("Mini-Dock ersetzt die Hover-Zone, Anzahl der Favoriten einstellbar"),
            L.T("Dateiendungen mit Semikolon getrennt")
        }),
        new ReleaseNote(new Version(0, 16, 0), L.T("Schneller finden, sehen was läuft"), new[]
        {
            L.T("Unscharfe Suche mit Hervorhebung, „Beste Treffer“ und Filtern wie typ:ps1, gruppe:, ist:fav"),
            L.T("Rückgängig (Strg+Z) nach Ausblenden, Entfernen und Verschieben"),
            L.T("Tastatur: Strg+Enter als Admin, Alt+1…9 für Favoriten, F1 zeigt alle Kürzel"),
            L.T("Fixierter Gruppenkopf, Sprungleiste und Strg+K für Gruppen"),
            L.T("Aktivitäten-Leiste mit Fortschritt; Kachelkarte für Remote-Läufe"),
            L.T("Vorschau mit der Leertaste: Code, Parameter, Signatur"),
            L.T("Designvorlagen, Windows-Akzentfarbe, Mica und Schriftgröße"),
            L.T("Einführungs-Assistent und optionales Mini-Dock am Bildschirmrand")
        }),
    };

    /// <summary>Neuerungen seit <paramref name="lastSeen"/> (exklusive) bis <paramref name="current"/> (inklusive), neueste zuerst.</summary>
    public static IReadOnlyList<ReleaseNote> Since(Version? lastSeen, Version current) =>
        All.Where(n => (lastSeen == null || Normalize(n.Version) > Normalize(lastSeen)) && Normalize(n.Version) <= Normalize(current))
           .OrderByDescending(n => n.Version).ToList();

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static Version? Parse(string? text) => Version.TryParse(text, out var v) ? v : null;
}
