# Änderungen

Erzeugt aus `src/PowerExec.Core/Updates/ReleaseNotes.cs` (`dotnet run --project tools/Changelog`) – nicht von Hand bearbeiten.

## 0.38.1 – Helle Design-Varianten tragen ihre Farbe, die Hover-Zone blendet beim Annähern stufenlos ein

- Helle Varianten sind deutlich in der Farbe des Designs getönt statt fast weiß; Linien, Auswahl und Text greifen die Akzentfarbe auf – neutrale Designs bleiben neutral
- Alle Designs im Marktplatz haben eine neu berechnete helle Variante (auch Mint); eigene Designs bekommen das neue helle Gegenstück im Designer
- Hover-Zone „Erst beim Annähern zeigen“: je näher der Mauszeiger kommt, desto deutlicher erscheint der Streifen – stufenlos statt auf einen Schlag

## 0.38.0 – Marktplatz: eigene Detailseite je Eintrag, weiche Übergänge, sauber umbrechende Kacheln

- Ein Klick auf eine Karte öffnet ihre eigene Detailseite; „← Entdecken“, die Zurück-Taste der Maus oder Alt+← führen zurück – mit weichem Ein- und Ausblenden
- Design-Details in zwei Spalten: links eine große Live-Vorschau mit Umschalter hell/dunkel, oben rechts alle Aktionen (Ausprobieren, Installieren und anwenden, Deinstallieren)
- Farben und Gruppenpalette zeigen beide Varianten; die der Vorschau ist hervorgehoben, ein Klick auf die andere schaltet um – ein Klick auf eine Farbe kopiert ihren Hexwert
- Schriften mit Beispieltext und Prüfung, ob sie installiert sind; Ecken, Hintergrund und Akzentfarbe mit kleinen Vorschauen
- Karten, Farben und Gruppenpalette behalten eine sinnvolle Breite und brechen sauber um; ändert sich die Fenstergröße, gleiten sie an ihren neuen Platz

## 0.37.0 – Designs: Designer für alles, eigener Bereich im Marktplatz, Gruppenfarben vom Design

- Einstellungen › Darstellung wählt jetzt nur noch das Design; Farben, Hintergrund, Ecken, Gruppenstil und Schrift stellt man im Designer ein
- Mitgelieferte und heruntergeladene Designs sind schreibgeschützt – „Als eigenes Design bearbeiten“ legt eine Kopie an, das Original bekommt weiter Updates
- Farben von Gruppen und Schlagworten: „Vom Design (automatisch)“, eine feste Stelle der Designpalette, die mit dem Design wechselt, feste Farben oder keine
- Jedes Design hat eine eigene, passend gewählte Gruppenpalette für die helle und die dunkle Variante
- Designs haben englische Namen mit Übersetzungen (aus Wald wird Forest/Wald) und eine Kategorie
- Marktplatz: Designs haben einen eigenen Bereich mit Entdecken, Kategorien und Meine Designs; die Details zeigen Farben, Palette, Schrift, Ecken und Hintergrund; ein Design lässt sich vor dem Übernehmen ausprobieren
- Das bisherige Aussehen bleibt erhalten: passt es zu keinem Design, wird es zum eigenen Design „Meine Einstellungen“

## 0.36.1 – Glas gleitet flüssig neben einem anderen Monitor oder der Taskleiste

- An einer Kante mit Nachbarmonitor oder Taskleiste ändert das Glas-Panel beim Gleiten nicht mehr seine Fenstergröße: das Fenster steht fest, der Inhalt gleitet wie bei Klarglas herein und der Weichzeichner wird darunter freigelegt
- Das Panel erscheint weiterhin nie auf dem Nachbarmonitor

## 0.36.0 – Neuer Eintrags-Editor: Reiter, Profile erben von ★ Standard, Variablen je Profil

- Das Eintrags-Fenster hat zwei Reiter: Allgemein (Schlagworte, Notiz, Verhalten im Panel) und Profile mit einklappbaren Abschnitten
- Profile übernehmen alles von ★ Standard und ändern nur einzelne Werte – Ziel oder Code, Variablen, Ausführen als, Fenster; ✎ passt an, ↺ setzt zurück
- Variablen je Profil: eine globale {{$Variable}} überschreiben, eine Abfrage vorbelegen oder festlegen oder eine Variable nur für diesen Eintrag anlegen
- Auswahl-Abfragen {{Server:A;B;C}} zeigen beim Start eine Liste; der erste Wert ist vorausgewählt, eigene Eingaben bleiben möglich
- Die Vorschau zeigt das ganze Script mit Syntaxfarben; Script-Dateien lassen sich dort direkt bearbeiten – gespeichert wird in die Datei, mit Sicherung
- Bestehende Profile werden automatisch übernommen: Werte, die ★ Standard gleichen, gelten als geerbt

## 0.35.1 – Variablen-Auswahl mit Suche, Schlagwort-Vorschläge beim Hineinklicken

- Der Knopf {{…}} öffnet eine Auswahl mit Suchfeld; lange Listen scrollen statt abgeschnitten zu werden
- Ein Klick ins Schlagwort-Feld zeigt zuerst die zuletzt verwendeten, dann alle vorhandenen Schlagworte

## 0.35.0 – Designs in hell und dunkel, Glas bleibt auf seinem Monitor, Variablen-Auswahl

- Alle Marktplatz-Designs haben jetzt eine helle und eine dunkle Variante; die zweite Variante wird aus der ersten abgeleitet statt einfach weiß zu sein
- Nach dem Herunterladen eines Designs bietet Power-Exec an, es gleich anzuwenden; die Vorschau-Rückfrage entfällt bei Designs
- Glas-Panel mit mehreren Monitoren: beim Gleiten bleibt das Panel auf seinem Monitor und erscheint nicht mehr auf dem Nachbarmonitor
- Der Knopf {{…}} im Eintrags-Editor zeigt alle Variablen und Abfragen beim Start mit Beschreibung

## 0.34.0 – Design-Editor: Palette, hell und dunkel, Schrift, Gruppenfarben

- Neu unter Darstellung: Design bearbeiten – Farben für Fläche, Karten, Text und Linien mit Live-Vorschau und Kontrastprüfung
- Ein Design kann eine helle und eine dunkle Variante enthalten und wechselt dann mit Windows mit
- Designs legen die Schrift für Text sowie für Pfade und Code fest und eine Farbreihe für Gruppen und Schlagworte ohne eigene Farbe
- Designs lassen sich als Datei exportieren; die Marktplatz-Designs bringen Paletten und Gruppenfarben mit
- Bei mehreren Monitoren wirft das Glas-Panel Schatten und Rand nicht mehr auf den Monitor daneben

## 0.33.0 – Marktplatz als eigenes Fenster; Gruppen lassen sich an die erste Stelle ziehen

- Der Marktplatz öffnet sich in einem eigenen Fenster – aus der Fußzeile des Panels, dem Tray-Menü oder den Einstellungen – und übernimmt Installationen sofort
- Marktplatz als Galerie: Karten zeigen, was ein Eintrag ausführt, links filtern Kategorien, ein Klick klappt die Details auf
- Gruppen lassen sich zuverlässig an die erste Stelle ziehen, im Panel und unter Einstellungen › Gruppen
- Dialoge und Einstellungen haben gleichmäßige Abstände; das Rechner-Feld im Ausführen-Dialog nutzt die volle Breite

## 0.32.0 – Panel fährt immer ganz aus; ruhigere Oberfläche; Einstellung „Ecken“

- Das Panel bleibt nach Standby, Docking oder geänderter Taskleiste nicht mehr teilweise außerhalb des Bildschirms: die Endlage wird nach dem Ausfahren geprüft, Monitordaten werden aufgefrischt
- Mit dem Glas-Hintergrund erscheint das Mini-Dock nach dem Einfahren des Panels wieder
- Kacheln füllen die ganze Zeile, Namen brechen an - und _ um statt abgeschnitten zu werden
- Ein Ansicht-Menü im Panel-Kopf (Liste oder Raster, Elementgröße); die Fußzeile zeigt den Status ohne Version
- Das Suchfeld zeigt jedes Mal ein anderes Filter-Beispiel; kleiner Text ist mindestens 11 pt groß
- Neu unter Darstellung: Ecken (Standard, Rund, Eckig); die Hover-Zone kann verborgen bleiben, bis der Mauszeiger nahe ist
- Das Tray-Menü zeigt die Favoriten in derselben Reihenfolge wie das Panel; das Protokoll ist immer englisch

## 0.31.1 – Kein Rahmen mehr neben dem eingefahrenen Panel

- Eingefahren hinterlässt das Panel keinen Rahmen mehr auf einem angrenzenden Monitor oder über der Taskleiste

## 0.31.0 – Neue Design-Vorschau auf deinem Desktop

- Design-Vorschauen zeigen dein echtes Desktop-Hintergrundbild, ein Fenster im Windows-Design und rechts das Panel mit Suchfeld, Zeilen, Knöpfen, Akzentfarbe und leuchtender Hover-Zone – gleich unter Darstellung und im Marktplatz
- Die Designvorlagen zeigen das aktuelle Design auch im eingeklappten Zustand
- Ein unter Darstellung gelöschtes Design gilt im Marktplatz wieder als nicht installiert; das Löschen-Kreuz leuchtet beim Darüberfahren rot

## 0.30.0 – Klarere Dialoge, drei Grund-Designs, Übersetzungen auf Basis Englisch

- Knöpfe wie „Übernehmen“ sind in den Dialogen für Variablen und Gruppen wieder gut lesbar; die Beschreibung einer Variable ist mehrzeilig
- Beim Herunterladen aus dem Marktplatz wählst du eine vorhandene Gruppe aus der Liste oder legst eine neue an
- Drei Grund-Designs (Fluent, Glas, OLED); Fluent Clean, Nordic Frost, Espresso, Graphit, Papier und Mitternacht gibt es jetzt im Marktplatz. Design-Kacheln sind gleich groß, das Löschen-Kreuz sitzt in der Kachel
- Die Art eines bestehenden Eintrags lässt sich nicht mehr ändern – nur beim Anlegen
- Übersetzungen bauen auf Englisch auf: jede weitere Sprache ist eine Datei unter Translations (z. B. de.json) und wird automatisch angeboten

## 0.29.0 – Marktplatz als eigener Bereich, Fluent-Meldungen

- Marktplatz ist ein eigener Bereich in den Einstellungen mit Einträge, Scripte, Designs, Installiert und Quellen; im Repository liegen Kategorien als Ordner, darunter die Gruppen
- Heruntergeladene Einträge landen jetzt zuverlässig in der gewählten Gruppe
- Vorschau: Designs zeigen ein Mini-Panel in ihren Farben, Einträge und Scripte klappen Ziel bzw. Code und benötigte Variablen auf
- Benötigte Variablen bringen ihre Art mit (Text, geheim, Umgebungsvariable, PowerShell …) und werden im Variablen-Dialog angelegt
- Meldungen und Rückfragen erscheinen als Fluent-Dialog statt als Windows-Meldungsfenster
- Hintergründe heißen jetzt „Glas (Blur)“ und „Transparent“; die Übersetzungen liegen im Repository unter Translations (de.json, en.json)

## 0.28.0 – Englische Oberfläche

- Power-Exec gibt es jetzt auch auf Englisch: die Sprache folgt Windows (Deutsch bei deutschem Windows, sonst Englisch) und lässt sich unter Einstellungen › Allgemein › Sprache umstellen
- Alle Fenster, Meldungen, Werkzeug-Vorlagen und Versionshinweise sind übersetzt; die Suche versteht englische Filter wie type:, group: und is: – die deutschen gehen weiter
- Nach dem Umstellen der Sprache startet Power-Exec auf Knopfdruck neu

## 0.27.0 – Marktplatz aus Git-Ordnern, Variablen-Tabelle, Snackbars

- Marktplatz: statt Paketen Ordner aus dem Git-Repository – aufklappbar, mit Tabs Einträge · Scripte · Designs · Installiert und kompakter Liste
- Ganze Ordner oder einzelne Dateien per Häkchen herunterladen oder deinstallieren; Einträge und Scripte landen in den eigenen Einträgen in der Gruppe deiner Wahl (vorgeschlagen: der Ordnername)
- Deinstallieren entfernt Einträge, Designs und Script-Dateien wieder; selbst geänderte Einträge nur nach Rückfrage. Updates bleiben in ihrer Gruppe
- Variablen als kompakte Tabelle mit Suche, Häkchen und eigenem Bearbeiten-Dialog; eingebaute Variablen heißen jetzt {{$Date}}, {{$Time}}, {{$User}}, {{$Computer}} und {{$Clipboard}}
- Erfolgsmeldungen erscheinen kurz als Snackbar und verschwinden wieder; Fehler bleiben stehen
- Der Favoriten-Stern folgt der Akzentfarbe

## 0.26.0 – Eigene Marktplatz-Quellen

- Marktplatz › Quellen: weitere Marktplätze einfach per Repository-URL hinzufügen (GitHub oder GitLab, auch Unterordner) – z. B. den Katalog deines Teams
- Fremde Quellen werden vor der ersten Installation einmal als vertrauenswürdig bestätigt; danach zeigt die Liste ihren Stand, der Quellenfilter grenzt die Elemente ein
- Eine nicht erreichbare Quelle hält die anderen nicht auf; Entfernen einer Quelle lässt bereits Installiertes stehen

## 0.25.0 – Variablen, Marktplatz und aufgeräumte Einstellungen

- Neu: Marktplatz (Inhalte › Marktplatz) – Pakete (z. B. Matrix42, Recherche), Links (Google, Microsoft Learn, VirusTotal, IP-Whois, GitHub/GitLab-Issues), Designs und die Beispielscripte mit einem Klick installieren; vorher zeigt eine Vorschau den Inhalt, benötigte Variablen werden abgefragt, Änderungen erscheinen als „Update verfügbar“
- Neu: Variablen (Inhalte › Variablen). {{$CompanyName}} wird in Texten, Links, Befehlen, Zielen, PowerShell-Code und Script-Parametern ohne Rückfrage eingesetzt – als fester Text, je Arbeitsbereich, geheim (verschlüsselt), aus einer Umgebungsvariable oder als Ergebnis eines PowerShell-Befehls (mit Zwischenspeicher)
- {{$Datum}}, {{$Uhrzeit}}, {{$Benutzer}}, {{$Computer}} und {{$Zwischenablage}} sind eingebaut, das Datums- und Uhrzeitformat ist einstellbar; normale {{Platzhalter}} werden weiter vor dem Start abgefragt
- Im Eintrags-Editor: Vorschläge beim Tippen von {{$, Einfügen per Klick und ein Hinweis bei Variablen, die es nicht gibt; die Vorschau im Panel zeigt die eingesetzten Werte
- Einstellungen: einheitliche Karten, kompakte Typ-Filter unter „Eigene Einträge“, farbige Rückmeldungen (InfoBar), Akzentbalken in der Navigation und „Alle Änderungen gespeichert“ in der Fußzeile

## 0.24.0 – Neue Designs, Vollton und ruhigere Gruppen

- Designvorlagen einklappbar und erweitert: Glas schaltet jetzt den echten Weichzeichner ein (Blur 60, Sättigung 125 %), neu sind OLED, Nordic Frost, Fluent Clean und Espresso – und eigene Designs lassen sich speichern
- Neuer Hintergrund „Vollton“ (deckend, ohne Transparenz); unter Panelfarbe wählst du, ob Hover-Zone, Auswahl und Knöpfe der Windows-Akzentfarbe, einer eigenen Akzentfarbe oder der Panelfarbe folgen
- Gruppen-Stil „Akzentstreifen“: ruhige, halbtransparente Karten mit Farbstreifen; Kacheln schweben dezent, Typ-Kürzel als Pillen; Elementgröße je Gruppe im Gruppenmenü; der fixierte Gruppenkopf sitzt genau auf der Gruppe
- Einstellungen aufgeräumt: Mini-Dock, Hover-Zone und Panel-Abmessungen als Ausklappbereiche, Scrollbalken nur beim Scrollen; ein angeheftetes, per Klick eingefahrenes Panel fährt beim Darüberfahren wieder aus

## 0.23.0 – Rechnergruppen aus Listen, OU-Auswahl und Export

- Rechnergruppen: Quelle „Dynamisch aus Liste“ – eine Namensliste nach Namen oder eine CSV nach beliebigen Spalten filtern; die Liste wird beim Synchronisieren neu gelesen. „Gruppe aus Liste importieren …“ legt so eine Gruppe direkt an
- Jede Rechnergruppe hat in der Kopfzeile Synchronisieren und Exportieren (CSV oder Text)
- Dynamisch aus AD: „OU wählen …“ zeigt den ganzen OU-Baum mit Suche und setzt die gewählte OU in „Ab OU“ ein
- Beispielscripte anlegen findest du jetzt unter Hilfe › Beispiele; das Panel sitzt beim Ausfahren genauer und prüft das Auto-Einfahren nur noch, wenn der Mauszeiger das Panel verlassen hat

## 0.22.3 – Feinschliff: Ausfahren, Auswahllisten, Einstellungen

- Panel fährt zügiger und gleichmäßiger aus; geparkt wird es ohne erneutes Platzieren gestartet
- Auswahllisten zeigen kein überflüssiges Eingabefeld mehr; das Panel zeigt das Programmsymbol
- Einstellungen: Panelfarbe und Hintergrund als Ausklappbereiche – darunter je nach Hintergrund Blur-Stärke, Sättigung, Milchglas und Deckkraft
- Glas: Inhalt sitzt nach einem Wechsel des Hintergrunds wieder genau im Panel

## 0.22.2 – Ruhigeres Ausfahren, Details bei Rastergruppen

- Das Panel fährt sanfter aus: Fokus und Hintergrundprüfungen folgen erst nach der Bewegung, beim Gleiten wird nichts unnötig neu vermessen oder gezeichnet
- Steht das Panel auf „Liste“ und eine Gruppe auf „Raster“, erscheinen die Details des ausgewählten Eintrags jetzt unten im Panel

## 0.22.1 – Fehlerbehebung beim Ausführen mit Glas

- Mit dem Hintergrund „Glas“ erschien beim Ausführen eines Scripts aus dem Panel die Meldung „Value cannot be null (defaultDestinationValue)“ – behoben
- Ersteinrichtung: Beispielscripte lassen sich zusätzlich zum eigenen Ordner anlegen (Gruppe „Beispiele“)

## 0.22.0 – Flüssiger: Panel passt sich direkt an, Glas gleitet ruhig

- Geänderte Einstellungen wirken sofort im offenen Panel – ohne Flackern und ohne Neuaufbau (nur beim Wechsel des Designs oder zwischen Klarglas und Glas wird es neu erzeugt)
- Glas: das Panel fährt sanft an und bleibt eingefahren hinter dem Rand bereit – kein Ruckeln mehr beim Ausfahren
- Einstellungen: Bereiche klappen weich auf und zu, der Auswahlbalken gleitet zum neuen Menüpunkt
- Acht Beispielscripte (Systeminfo, Netzwerktest, große Dateien, installierte Programme, Fehlerprotokoll, Temp aufräumen, Kennwörter, Speicherplatz) – unter Inhalte › Gruppen mit „Beispielscripte anlegen“

## 0.21.0 – Glas mit eigenem Weichzeichner, neue Einstellungen

- Neuer Hintergrund „Glas“ (Windows 11): der Desktop hinter dem Panel wird weichgezeichnet und mit der Panelfarbe getönt – Blur-Stärke und Sättigung einstellbar. Aero-Blur, Acrylic und Mica sind entfallen, Klarglas bekommt ein ruhigeres Milchglas
- Einstellungen mit Navigationsleiste: einklappbarer Rand, einklappbare Bereiche und Zurück; die Panelfarbe wählst du wie die Akzentfarbe in Windows
- Vorschau (Leertaste) in einem eigenen Fenster neben dem Panel – sie folgt dem ausgewählten Eintrag
- Angeheftetes Panel per Klick auf die Hover-Zone ein- und ausfahren; Ordner als Gruppe ablegen öffnet gleich den Gruppendialog mit Farbe und Darstellung
- Beim Ändern von Einstellungen bleibt das Panel sichtbar und behält seine Größe; das Tastenkürzel (Strg+Alt+Leertaste) lässt sich schon bei der Einrichtung festlegen; der Installer folgt hell/dunkel von Windows

## 0.20.0 – Neues Aussehen im Stil von Windows 11

- Schaltflächen, Schalter, Eingabefelder, Auswahllisten, Menüs und Bildlaufleisten im Fluent-Design – in den Einstellungen, allen Dialogen, im Panel, in der Spotlight-Suche und im Mini-Dock
- Hell, Dunkel, Kontrastdesign und Akzentfarbe gelten für alle Bedienelemente und wechseln auch bei offenen Fenstern mit; im Panel richten sie sich nach der Panelfarbe
- Unten im Panel steht, welche Version läuft und ob sie installiert oder portabel ist

## 0.19.5 – Weichzeichner auch an Taskleiste und zweitem Monitor flüssig, .NET 10

- Liegt die Taskleiste oder ein weiterer Monitor an der Panelkante, gleitet das Panel jetzt ebenfalls als Ganzes herein und wird am Rand des Arbeitsbereichs abgeschnitten – kein Wachsen mehr, kein Ruckeln, nicht mehr über der Taskleiste
- Läuft jetzt auf .NET 10 (Langzeitsupport bis 2028) und PowerShell 7.6

## 0.19.4 – Flüssiger mit Weichzeichner

- Mit Aero-Blur, Acrylic oder Mica wird das Panel beim Ein- und Ausfahren als Ganzes verschoben statt neu vermessen – deutlich flüssiger (bei einem weiteren Bildschirm hinter dem Rand wächst es wie bisher)

## 0.19.3 – Weichzeichner fährt mit dem Panel

- Aero-Blur, Acrylic und Mica fahren genau mit dem Panel ein und aus – kein Hintergrund mehr vor oder nach dem Panel

## 0.19.2 – Weichzeichner repariert

- Aero-Blur bleibt nach dem Einfahren erhalten; kein unscharfer Streifen mehr neben dem Panel
- Kein Schließen-Knopf von Windows mehr am Panel
- Deckkraft steht direkt bei Hintergrund und Milchglas, jetzt ab 20 %; mit Weichzeichner wird sie auf 70 % gesetzt, wenn sie auf 100 % stand

## 0.19.1 – Hintergrund wählen: Aero, Acrylic, Mica oder Klarglas

- Hintergrund hinter dem Panel wählbar: Klarglas, Aero-Blur, Acrylic oder Mica – Ecken rundet Windows 11 jetzt selbst und glatt
- Milchglas-Regler statt Stufen; Windows 10 bekommt bei Acrylic und Mica automatisch Aero-Blur
- „Nach Profil fragen“ greift jetzt auch beim Start aus dem Panel, mit Alt+1 … 9 und im Spotlight
- Feste Gruppen (Zuletzt ausgeführt, Favoriten, Eigene Einträge) lassen sich auch im Panel verschieben
- Liste ↔ Raster je Gruppe ändert die Höhe weich; Schieberegler bei „Zuletzt ausgeführt“ wieder bedienbar

## 0.19.0 – Dynamische Rechnergruppen, feste Gruppen frei anordnen

- Rechnergruppen dynamisch aus dem AD: Regeln wie „Betriebssystem enthält Server“, in Blöcken mit UND/ODER kombiniert; Aktualisierung beim Start und regelmäßig
- Zuletzt ausgeführt, Favoriten und Eigene Einträge stehen mit Schloss in der Gruppenliste, lassen sich verschieben und je Arbeitsbereich ein- oder ausblenden
- Laufende Scripte: Ende wird erkannt, auch wenn das Fenster offen bleibt; „Erneut starten“ nach dem Ende; „läuft“ direkt am Eintrag
- Beim Start nach dem Profil fragen (global oder je Eintrag) – im selben Fenster wie die Werte für {{Platzhalter}}
- Gruppen klappen weich auf und zu, Knöpfe im Gruppenkopf blenden ein, „Zuletzt ausgeführt“ zeigt die Zeit
- Stärke des Weichzeichners einstellbar, Deckkraft wieder genau wie eingestellt; angeheftetes Panel überdeckt die Einstellungen nicht mehr
- Ordner ins Panel ziehen: als Gruppe oder als Link; Schlagworte mit eigener Farbe und einzeln von Einträgen entfernbar

## 0.18.0 – Filtern aus dem Suchfeld, besser lesbar in Hell

- Klick ins Suchfeld zeigt die Filterarten (Schlagwort, Typ, Gruppe, Merkmal) – die Auswahl listet die vorhandenen Werte; die Filterknöpfe unter der Suche entfallen
- Helles Design: offene Fenster wechseln sofort mit, Gruppenfarben und Symbole mit mehr Kontrast, helle Panels kaum noch durchscheinend
- Gruppenmenü aufgeräumt: „Neue Untergruppe“ oben, „Eigene Farbe“ und „Farbe entfernen“ im Untermenü Farbe, ohne „Nach oben/unten“
- Suchfeld runder

## 0.17.2 – Gruppen anlegen und ordnen

- Neue Gruppe mit Symbol oder Bild, Farbe, Darstellung und optional einem Ordner (wird zur smarten Gruppe) in einem Dialog
- Rechtsklick auf eine Gruppe legt eine Untergruppe an, auf freie Fläche eine neue Gruppe unten
- Einstellungen › Gruppen: Untergruppen im Rahmen ihrer Gruppe, Verschieben und Verschachteln wie im Panel
- Panel: Menü-Knopf im Gruppenkopf erst beim Darüberfahren; beim Hineinziehen leuchtet der Gruppenrahmen

## 0.17.1 – Gruppen verschieben: sehen, wo sie landen

- Beim Ziehen einer Gruppe öffnet sich an der Zielstelle eine Lücke mit Platzhalter – im Panel und in den Einstellungen
- Zwischen zwei Gruppen wählt seitliches Ziehen die Ebene: nach rechts tiefer, nach links höher
- Ziehbild am Mauszeiger mit Name, Farbe und Zahl der Untergruppen
- Unter Panel › Verhalten einstellbar: Ziehbild, weiche Bewegung, Inhalte zuklappen, Begründung an ungültigen Stellen

## 0.17.0 – Spürbar schneller beim Tippen

- Suche im Panel im Mittel drei- bis fünfmal schneller (gemessen mit 500 Einträgen)
- Filter wechseln und Weitertippen bauen die Liste nicht mehr komplett neu auf
- Kontextmenüs entstehen erst beim Öffnen – weniger Speicher, schnelleres Einblenden
- Auswahl, Haken der Mehrfachauswahl und Gruppenkopf bleiben beim Tippen stehen

## 0.16.2 – Acrylic passt sich dem Panel an

- Weichgezeichneter Hintergrund mit abgerundeten Ecken wie das Panel
- Hintergrund fährt beim Ein- und Ausfahren mit dem Panel mit

## 0.16.1 – Feinschliff nach den ersten Rückmeldungen

- Vorschläge beim Tippen: #schlagwort, typ:, gruppe:, ist: – nur Vorhandenes, mit Anzahl
- Farbwähler für Gruppen- und Panelfarbe; Farbmenü zeigt wieder alle Farben
- Gruppen in den Einstellungen per Drag & Drop sortieren
- Sprungleiste: zweiter Klick klappt zu; Sprungleiste und Scrollbalken abschaltbar
- Mini-Dock ersetzt die Hover-Zone, Anzahl der Favoriten einstellbar
- Dateiendungen mit Semikolon getrennt

## 0.16.0 – Schneller finden, sehen was läuft

- Unscharfe Suche mit Hervorhebung, „Beste Treffer“ und Filtern wie typ:ps1, gruppe:, ist:fav
- Rückgängig (Strg+Z) nach Ausblenden, Entfernen und Verschieben
- Tastatur: Strg+Enter als Admin, Alt+1…9 für Favoriten, F1 zeigt alle Kürzel
- Fixierter Gruppenkopf, Sprungleiste und Strg+K für Gruppen
- Aktivitäten-Leiste mit Fortschritt; Kachelkarte für Remote-Läufe
- Vorschau mit der Leertaste: Code, Parameter, Signatur
- Designvorlagen, Windows-Akzentfarbe, Mica und Schriftgröße
- Einführungs-Assistent und optionales Mini-Dock am Bildschirmrand
