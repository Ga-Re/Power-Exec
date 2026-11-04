# Power-Exec

Schneller Zugriff auf Scripte und Programme über ein Panel am Bildschirmrand.
Fährt die Maus an den Rand, gleitet das Panel nach einer kurzen Verweilzeit heraus.
Dort lässt sich suchen, filtern und starten: lokal, als Administrator, als anderer Benutzer
oder remote auf einem oder vielen Rechnern.

## Funktionen

**Panel**
- Hover-Zone am Bildschirmrand: Rand, Monitor, Lage, Länge und Breite einstellbar; sie übernimmt die Panelfarbe
- Panel: Breite, Höhe (30–100 %) und Lage entlang der Kante (an der Hover-Zone, oben, mittig, unten) einstellbar, ebenso Verweilzeit, Einklappzeit und Animation
- **Größe per Ziehen** am Innenrand des Panels (Breite und Höhe werden gespeichert)
- **Acrylic:** Hintergrund hinter dem Panel weichzeichnen (Panel › Darstellung)
- **Designvorlagen** (Fluent, Glas, Graphit, Papier, Mitternacht): Design, Panelfarbe, Deckkraft, Acrylic, Akzentfarbe und Fensterhintergrund mit einem Klick; **Akzentfarbe von Windows übernehmen** (passend aufgehellt bzw. abgedunkelt, folgt Änderungen in Windows sofort); **Mica** für Einstellungen und Dialoge (Windows 11 22H2+); **Schrift- und Elementgröße** 80–150 %; ein **Kontrastdesign** von Windows wird automatisch übernommen
- **Darstellung:** Liste oder **Raster** (Kacheln; Details des ausgewählten Eintrags unten im Panel), **Elementgröße Klein / Mittel / Groß** – per Knopf im Panel-Kopf, Rechtsklick oder in den Einstellungen
- **Rechtsklick auf freie Fläche:** neue Gruppe, neue smarte Gruppe (Ordner), „Neuer Eintrag“ (Arten, Dateien, aus Vorlagen, aus Zwischenablage), Import, Darstellung
- **Strg+V** im Panel (außerhalb des Suchfelds) bzw. **Strg+Umschalt+V**: erkennt URL, Pfad, Rechnername, `ssh user@host` oder Code und schlägt einen Eintrag vor
- **Unscharfe Suche:** findet auch Anfangsbuchstaben (`gds` → *Get-DiskSpace*, `usr pw` → *Reset-ADUserPassword*) und einen Tippfehler (`dsik`); Treffer werden im Namen und in der Info-Zeile **hervorgehoben**; häufig und kürzlich gestartete Einträge stehen weiter oben, bei vielen Treffern zuerst als **„Beste Treffer“** (Enter startet den ersten)
- **Filter im Suchfeld:** `#schlagwort`, `typ:ps1|cmd|exe|programm|web|ordner|rdp|ssh|snippet|befehl|werkzeug|datei`, `gruppe:Name` (auch `gruppe:"Kunde A"`), `ist:fav`, `ist:admin`, `ist:remote`, `ist:problem` – erkannte Filter erscheinen als **Chips** unter dem Suchfeld und lassen sich mit ✕ entfernen
- „Zuletzt ausgeführt“ und „Favoriten“ ein- oder ausblendbar (Einstellungen › Gruppen)
- Globales Tastenkürzel (Standard: `Strg+Alt+Leertaste`) öffnet das Panel und setzt den Cursor ins Suchfeld
- Anheften, damit das Panel offen bleibt
- Panelfarbe frei wählbar (Vorlagen oder eigene Farbe als `#RRGGBB`) samt Deckkraft; bei hellen Farben wird die Schrift automatisch dunkel
- Vorausgewählter Filter beim Öffnen einstellbar (Alle, Scripte, Programme oder Favoriten)
- Symbole: Programmsymbol bei EXE, Verknüpfungen und Snap-ins, **Favicon bei Webseiten**, Ordnersymbol bei Ordnern, Remotedesktop-Symbol bei RDP, Programmsymbol bei Befehlen
- Suche über Name, Dateiname, Synopsis, Beschreibung, Notiz, Schlagworte und Snippet-Code; Filter: Alle, Scripte, Programme, Favoriten
- **Navigation in langen Listen:** der Kopf der aktuellen Gruppe bleibt beim Scrollen oben stehen (mit Pfad, z. B. „Kunde A › AD“; Klick springt zum Gruppenkopf); ab vier Gruppen zeigt eine **Sprungleiste** rechts einen Punkt je Gruppe in ihrer Farbe; **Strg+K** sucht eine Gruppe und springt hin
- Gruppen einklappbar; Kontextmenü pro Gruppe (Neuer Eintrag, Neue Untergruppe, Im Explorer öffnen, Neu einlesen, Im Arbeitsbereich ausblenden, Gruppe konfigurieren …)
- Kontextmenü pro Eintrag: Ausführen, Admin, Als anderer Benutzer, Remote, Im Editor öffnen, Speicherort öffnen, Pfad kopieren, Favorit, Ausblenden
- **Vorschau (Leertaste):** zeigt Code mit Syntaxfarben (PowerShell, Batch, Snippets; die ersten 60 Zeilen), Parameter mit Typ, Vorgabe und Hilfe, Signatur, Voraussetzungen, Notiz und wie oft/wann der Eintrag zuletzt gestartet wurde – ohne Editor; ↑ ↓ blättert zum nächsten Eintrag, Enter bzw. „Ausführen“ startet
- **Tastatur:** Pfeiltasten wählen aus, Enter startet, **Strg+Enter** als Administrator, **Strg+Umschalt+Enter** als anderer Benutzer, **Strg+R** remote, **→ oder Tab** öffnet alle Aktionen des Eintrags als Menü, **Alt+1 … 9** startet die Favoriten (Taste steht in der Gruppe „Favoriten“ am Eintrag), **Strg+D** Favorit, **F2** bearbeiten/Startprofile, **Strg+E** Editor, **Strg+Umschalt+C** Pfad kopieren, Esc leert die Suche bzw. schließt das Panel; **F1** oder **?** im leeren Suchfeld zeigt alle Kürzel
- **Rückgängig:** nach Ausblenden, Entfernen, Verschieben (Drag & Drop), Favorit entfernen sowie Gruppen löschen, umbenennen, verschieben oder ausblenden erscheint unten im Panel ein Hinweis mit „Rückgängig“ (8 Sekunden, bleibt stehen, solange die Maus darüber ist) – oder **Strg+Z**. Die Rückfragen beim Entfernen eigener Einträge und leerer Gruppen entfallen dafür
- **Verlauf** (Strg+H oder Uhr-Symbol im Panel-Kopf): alle Starts mit Zeit, Ziel, Profil, Parametern, Dauer und Ergebnis (Exit-Code bzw. Zählung bei Remote), filterbar nach Heute/Fehler/Remote und durchsuchbar; **„↻ Gleiche Werte“** wiederholt den Lauf mit denselben Werten, Rechnern und Rechten, **„Nur fehlgeschlagene“** startet nur die Rechner, auf denen es nicht geklappt hat. Kennwörter werden nie gespeichert (`%LOCALAPPDATA%\Power-Exec\history.json`, höchstens 500 Einträge)
- Gruppe **„Zuletzt ausgeführt“** (mit dem verwendeten Profil, Anzahl einstellbar)
- **Gruppen mit Untergruppen** direkt im Panel anlegen (Rechtsklick auf eine Gruppe oder + unten), umbenennen, löschen; **Gruppen per Drag & Drop verschieben** (oberer/unterer Rand = davor/dahinter, Mitte = als Untergruppe hinein); Einträge per **Drag & Drop** in jede Gruppe verschieben oder auf „Favoriten“ ziehen; Reihenfolge per Drag & Drop
- **Gruppenfarben:** Palette oder eigene Farbe; die Gruppe erscheint als getönte Karte mit farbigem Symbol, Untergruppen erben die Farbe
- **Liste oder Raster je Gruppe:** im Gruppenkopf, per Rechtsklick › Darstellung oder in Einstellungen › Gruppen
- **Smarte Gruppen:** eine Gruppe mit Ordner zeigt automatisch dessen Scripte und Programme – auf Wunsch mit Unterordnern als Untergruppen
- **Aus dem Explorer und Browser ziehen:** Scripte, Programme, Ordner und Links ins Panel bzw. direkt in eine Gruppe ziehen (auch an den Bildschirmrand – das Panel fährt dann aus). Dateien verhalten sich wie gefundene Dateien (Synopsis, Parameter-Dialog, Remote, Profile)
- Neue Einträge direkt in einer Gruppe anlegen: Webseite, Ordner, Remotedesktop, SSH, Snippet, Werkzeug, Befehl, Dateien, aus Vorlagen oder aus der Zwischenablage
- Doppelklick startet das Standardprofil; Split-Button für weitere Profile
- **Aktivitäten-Leiste** unten im Panel: laufende und gerade beendete Starts mit Laufzeit; Remote-Läufe mit Fortschritt (z. B. „28 / 40 Rechner · 2 Fehler“), **Abbrechen** und **Ausgabe** (öffnet die Ergebnisliste); lokale Scripte mit Ende und Exit-Code. Solange etwas läuft, **pulsiert die Hover-Zone**. Beendete Einträge bleiben 15 Minuten stehen (höchstens drei)
- Hover-Zone wird bei **Vollbild-Programmen** (Video, Präsentation, Spiel, RDP) automatisch deaktiviert
- **Mini-Dock** (Panel › Darstellung, standardmäßig aus): schmale Leiste mit den ersten neun Favoriten am gewählten Rand – ein Klick startet, „⋯“ öffnet das Panel; ausgeblendet, solange das Panel offen ist oder ein Vollbild-Programm läuft
- **Tray-Schnellstart:** Favoriten und „Zuletzt ausgeführt“ direkt aus dem Tray-Menü starten, Panel öffnen, Arbeitsbereich wechseln

**Arbeitsbereiche**
- Eine Auswahl der Gruppen, z. B. je Kunde oder Aufgabe: Gruppen, Ordner und eigene Einträge gibt es nur einmal; je Arbeitsbereich wählst du, **welche Gruppen** erscheinen
- Eigene Favoriten, „Zuletzt ausgeführt“, Startprofile und eingeklappte Gruppen je Arbeitsbereich
- Wechsel im Panel-Kopf, im Tray-Menü oder in den Einstellungen; neuer Arbeitsbereich leer oder als Kopie des aktuellen

**Eigene Einträge**
- Arten: Webseite, Ordner/Freigabe, Remotedesktop, **SSH** (Windows Terminal, Konsole oder PuTTY), **Snippet** (PowerShell – läuft wie ein Script, auch als Admin, als anderer Benutzer oder remote – oder Text, das in die Zwischenablage kopiert wird), **Windows-Werkzeug** (Snap-ins, Systemsteuerung, `ms-settings:`), Befehl, einzelne Datei
- **Eintragsdialog je Art:** erst die Art als Kachel wählen (oder direkt aus dem Menü), dann ein eigenes Formular nur mit den passenden Feldern; oben Symbol, Anzeigename und Beschreibung; Art jederzeit änderbar (passende Werte bleiben); Prüfung **live am Feld**, Speichern erst bei gültigen Pflichtfeldern; Fenstergröße wird je Art gemerkt
  - **Webseite:** Titel und Favicon werden automatisch vorgeschlagen, Browser/privat/Profil, **App-Fenster** (ohne Tabs und Adressleiste), Platzhalter-Hilfe
  - **Remotedesktop:** Rechner, Port, **Benutzer** (vorbelegt, ohne Kennwort), **Vollbild oder Fenstergröße**, **alle Monitore**, **Admin-Sitzung**, **RD-Gateway** – oder eine vorhandene **.rdp-Datei**
  - **SSH:** **Befehl nach dem Login**, **Sprungserver** (-J), **Port-Weiterleitungen** (-L), **Windows-Terminal-Profil**
  - **PowerShell-Snippet:** Editor mit Zeilennummern und **Syntaxprüfung**, **Vorschau der abgefragten Parameter**, Ausführungsart (Admin, anderer Benutzer, remote, PowerShell 5.1/7), **Testen**
  - **Text-Snippet:** eingebaute Variablen **{{$Datum}}, {{$Uhrzeit}}, {{$Benutzer}}, {{$Computer}}, {{$Zwischenablage}}**, Platzhalter mit Vorschau, **formatiert (HTML) kopieren**, **direkt in das vorher aktive Fenster einfügen**
  - **Ordner:** Erreichbarkeitsprüfung mit UNC-Hinweis, **Öffnen mit** (Explorer, neues Fenster, Programm, Terminal), als anderer Benutzer, „als smarte Gruppe anlegen“
  - **Befehl, Datei, Werkzeug:** **Arbeitsordner**, **Fenster** (normal, maximiert, minimiert), Ausführen als (aktuelles Konto, Administrator, anderer Benutzer), Werkzeug-Auswahl mit Suche
  - **Startprofile für alle Arten** (Liste links, Einstellungen rechts): je Profil eigene Startoptionen – z. B. Webseite „Arbeit“ in Edge mit Browserprofil A, „Privat“ in Chrome mit Browserprofil B; RDP mit anderem Benutzer oder Vollbild; SSH über anderen Sprungserver; Ordner im Terminal statt Explorer. Ziel, Adresse und Code gelten für alle Profile. Gleicher Aufbau für Scripte und Programme aus smarten Gruppen (⚙)
  - Browser-Auswahl zeigt **nur installierte Browser**, Browserprofile werden **gefunden und zur Auswahl angeboten** (Edge, Chrome, Firefox)
- **Vorlagen-Katalog** mit rund 70 Admin-Werkzeugen, Systemsteuerungs- und Einstellungsseiten
- **Platzhalter** `{{Name}}` bzw. `{{Name:Vorgabe}}` in Adresse, Argumenten oder Code werden beim Start abgefragt (mit Verlauf), z. B. `https://ticket/{{Nummer}}` oder `mstsc /v:{{Rechner}}`
- Webseiten in einem bestimmten **Browser** (Edge, Chrome, Firefox), privat und mit Browserprofil öffnen
- **Eigenes Symbol** aus Fluent UI System Icons (≈2.500), Heroicons (324), Segoe-Glyphen, Emoji oder eigener Bilddatei – auch für **Gruppen**
- **Mit einem Klick starten** statt aufklappen (je Eintrag); **Duplizieren**; **Schlagworte**
- **Erreichbarkeits-Prüfung**: tote Links, fehlende Ordner/Dateien, nicht erreichbare RDP-/SSH-Rechner und fehlende Programme werden markiert
- Infos ohne Wiederholungen: bei Webseiten Titel und Adresse, bei Ordnern Anzahl Dateien und Unterordner, Größe und letzte Änderung
- Einstellungen › Eigene Einträge: kompakte, durchsuchbare Liste mit echten Symbolen und Filtern (Art, Gruppe, Schlagwort, Probleme), ⚙ zum Bearbeiten, **Mehrfachauswahl** (verschieben, exportieren, löschen)
- **Teilen:** Einträge oder ganze Gruppen als Datei exportieren und bei Kollegen importieren (samt Gruppen, Symbolen, Schlagworten und Profilen – ohne Kennwörter)

**Smarte Gruppen (Ordner)**
- Beliebig viele Ordner, auch Netzwerkfreigaben (UNC); Anzeigename frei wählbar, Symbol wie bei jeder Gruppe
- Alles unter Einstellungen › Inhalte › Gruppen: „Neu ▾“ → Gruppe oder smarte Gruppe; Löschen nur nach Rückfrage
- Die Einstellungen zeigen pro smarter Gruppe **alle gefundenen Einträge** in einer Tabelle: Haken = im Panel sichtbar, dazu Startprofile, Admin-Markierung und ⚙ zum Konfigurieren; Suche, Typfilter, „Alle anzeigen“ / „Keine“
- **Neue Dateien** pro Ordner: automatisch anzeigen oder erst nach Freigabe (Hinweis im Panel und in den Einstellungen, „Alle freigeben“ / „Ausgeblendet lassen“)
- Ordner-Optionen: Unterordner ja/nein, **Unterordner im Panel als Untergruppen**, Dateiendungen, Muster-Ausschlüsse (`*.exe`, `*\Archiv\*`, `_*`); per Muster ausgeschlossene Dateien erscheinen ausgegraut mit Begründung; zusätzlich globale Ausschlüsse
- Dateiwächter: Änderungen erscheinen automatisch
- **Offline-fest:** Antwortet ein Ordner nicht innerhalb des Zeitlimits (z. B. Freigabe offline), zeigt das Panel den zuletzt eingelesenen Stand; beim Start erscheint sofort der zwischengespeicherte Stand
- **Umbenennen/Verschieben:** Profile, Favorit, Sortierung und Verlauf wandern automatisch mit (Erkennung über Dateiinhalt bzw. gleichen Namen)

**Startprofile je Eintrag (⚙)**
- Eigener Anzeigename und eigene Beschreibung im Panel
- Beliebig viele Profile, eines davon ist Standard (Klick im Panel); die übrigen stehen im Menü am Ausführen-Knopf
- Pro Profil: als Administrator ausführen; bei PowerShell jeder Parameter **fest**, **vorbelegt** oder **fragen**, Ziel (dieser PC, feste Rechner, beim Start wählen), Ausführen-Dialog (immer, nur wenn nötig, nie), PowerShell-Version, -NoExit, -NoProfile
- Bei EXE, Verknüpfungen, Batch und Snap-ins: zusätzliche Argumente
- Parameter vom Typ `PSCredential`/`SecureString` oder mit „Password“ im Namen werden nie gespeichert
- Optionale **Sicherheitsabfrage** vor dem Start und eigene **Kategorie** je Eintrag
- **Notiz** je Eintrag (z. B. „nur nach Rücksprache“) – sichtbar im Panel, im Tooltip und im Ausführen-Dialog, durchsuchbar

| Dateityp | Lokal | Admin | Anderer Benutzer | Remote / Masse | Info im Panel |
|---|---|---|---|---|---|
| `.ps1` | ✔ | ✔ | ✔ | ✔ | `.SYNOPSIS` / `.DESCRIPTION` |
| `.cmd` / `.bat` | ✔ | ✔ | ✔ | – | erste `REM`- oder `::`-Zeile |
| `.exe` | ✔ | ✔ | ✔ | – | Dateibeschreibung und Version |
| `.lnk` | ✔ | ✔ | ✔ | – | Kommentar bzw. Ziel |
| `.msc` | ✔ | ✔ | ✔ | – | Snap-in-Name |

**PowerShell-Scripte**
- Ein Klick startet das Standardprofil; ohne eigene Profile gilt: ohne Parameter sofort starten, mit Parametern den Dialog öffnen
- Das Formular im Dialog entsteht aus dem `param()`-Block:
  Pflichtfelder, `[ValidateSet()]` als Auswahl, `[switch]` als Kontrollkästchen, Standardwerte, Hilfetexte aus `.PARAMETER`
- Ausführen als: aktueller Benutzer, Administrator (UAC) oder anderer Benutzer
- PowerShell 7 oder Windows PowerShell 5.1, `-NoExit`, `-NoProfile`
- Befehlsvorschau zeigt, was tatsächlich gestartet wird
- **Parameter-Verlauf:** die zuletzt verwendeten Werte je Script und Parameter stehen im Dialog als Vorschläge bereit (nie bei Kennwörtern, `PSCredential` oder `SecureString`)
- **`#Requires` wird ausgewertet:** `-RunAsAdministrator` startet lokal automatisch als Administrator, `-Version 7` verwendet PowerShell 7, fehlende Module aus `-Modules` werden beim Aufklappen gemeldet

**Remote und Massenausführung**
- Ziel „Remote“: Rechner aus *Zuletzt verwendet* oder *Favoriten* wählen, mehrere Namen auf einmal per Komma einfügen
- Mehrere Rechner = parallele Ausführung (gleichzeitige Verbindungen einstellbar)
- **Rechnergruppen** (Einstellungen › Rechnergruppen): im Dialog per Klick auswählen, in Startprofilen als `@Gruppe` eintragen; befüllen per Text/CSV-Import oder **AD-Suche** (Name mit `*`, OU oder LDAP-Filter, mit dem angemeldeten Konto)
- Vorabprüfung pro Rechner: erreichbar, PowerShell-Version, Module aus `#Requires -Modules`
- Remote wird standardmäßig Windows PowerShell 5.1 verwendet; PowerShell 7 nur, wenn der Endpunkt `PowerShell.7` existiert
- **Scriptordner mitsenden:** kopiert Begleitdateien und Module über die Remote-Sitzung in einen Temp-Ordner auf dem Ziel und löscht ihn danach. Das vermeidet das Double-Hop-Problem, weil der Zielrechner nicht selbst auf die Freigabe zugreifen muss. Dauerhaft aktivierbar mit der Zeile `PowerExec: CopyFolder` unter `.NOTES`.
- Ergebnisliste mit Status (Erfolgreich / Fehler / Nicht erreichbar), aufklappbarer Ausgabe, Log, CSV-Export und „Fehlgeschlagene wiederholen“
- **Kachelkarte:** eine Kachel je Rechner in der Statusfarbe, Fehler und nicht erreichbare Rechner zuerst, live während des Laufs; Klick zeigt die Ausgabe darunter. Ab 16 Rechnern startet die Ergebnisliste so, umschaltbar mit „Liste / Kacheln“
- CredSSP wird bewusst nicht unterstützt (Anmeldedaten würden an den Zielrechner weitergegeben)
- **Verbindungstest** je Rechner im Dialog: WinRM erreichbar, Anmeldung, PowerShell-Version, PowerShell 7; Option **HTTPS (5986)**; verständliche Hinweise bei TrustedHosts- und DNS-Problemen
- **Windows-Benachrichtigung**, wenn ein Remote-Lauf fertig ist (Klick öffnet die Ergebnisliste)

**Sicherheit**
- Anmeldedaten werden bei jedem Start abgefragt und nie gespeichert
- Bei Remote gehen sie nur über stdin an den lokalen Runner-Prozess, nie über die Kommandozeile oder eine Datei
- Netzlaufwerke werden vor dem Start in UNC-Pfade umgewandelt (erhöhte Prozesse und andere Konten sehen keine verbundenen Laufwerke). Fehlt dem gewählten Konto der Zugriff, erscheint ein klarer Hinweis.

- **Blockierte Dateien** (aus dem Internet heruntergeladen) werden markiert und lassen sich mit „Entsperren“ freigeben; beim Aufklappen eines Eintrags wird die **digitale Signatur** angezeigt

**Einstellungen**
- Gegliedert wie PowerToys: Navigation mit Symbolen und **einklappbaren Bereichen** (Allgemein · Inhalte: Gruppen, Eigene Einträge, Arbeitsbereiche · Panel: Darstellung, Verhalten · Ausführung · Hilfe), **Suche** „Nach Einstellungen suchen“, Karten mit Symbol und Beschreibung, aufklappbare Karten für Unteroptionen
- Ein einheitlicher Dialog für eigene Einträge und Startprofile (Symbol, Ziel, Gruppe, Schlagworte, Ein-Klick-Start, Profile)
- **Schlagwort-Verwaltung:** alle Schlagworte mit Anzahl und Einträgen, umbenennen, zusammenführen, löschen, **Farbe** je Schlagwort
- **Rechnergruppen** einklappbar; überall, wo Rechner eingegeben werden, schlägt „@“ die Gruppen vor, dazu „Gruppe einfügen ▾“
- Schlagworte als **Chips** mit globaler Vorschlagsliste und Autovervollständigung (Enter/Komma fügt hinzu, × oder Rücktaste entfernt)
- Änderungen werden **sofort übernommen** (das Panel wird nur neu aufgebaut, wenn sich an ihm etwas ändert); „Änderungen verwerfen“ stellt den Stand beim Öffnen wieder her
- Änderungen im Panel (z. B. ein Eintrag in eine Gruppe gezogen) erscheinen direkt im offenen Einstellungsfenster
- Eigene Einträge lassen sich aus der Einstellungsliste **direkt ins Panel ziehen** (an den Bildschirmrand – das Panel fährt aus – und auf eine Gruppe)

**Diagnose & Daten**
- Protokoll (`%LOCALAPPDATA%\Power-Exec\app.log`) mit Einlesezeiten, Starts und Fehlern; „Protokoll kopieren“ für Fehlermeldungen
- Einstellungen **exportieren/importieren** (zusammenführen oder ersetzen) – ohne Kennwörter

**Updates**
- „Nach Updates suchen“ auch direkt in den Einstellungen; eine gefundene Version erscheint dort als Hinweis mit „Jetzt aktualisieren“
- Beim Start Prüfung der [GitHub Releases](https://github.com/Ga-Re/Power-Exec/releases); bei neuer Version erscheint ein Hinweis im Panel, installiert wird erst nach Bestätigung
- Die heruntergeladene Datei wird per SHA-256-Prüfsumme kontrolliert

## Installation

1. `PowerExec-Setup.exe` aus dem neuesten [Release](https://github.com/Ga-Re/Power-Exec/releases) herunterladen und starten.
2. Optional „Verknüpfung auf dem Desktop“ und „Mit Windows starten“ wählen.
3. Fertig. Beim ersten Start führt ein kurzer **Einführungs-Assistent** durch Scriptordner (oder ein Beispielordner mit Script), Bildschirmrand, Tastenkürzel und Autostart – jederzeit erneut über Einstellungen › Allgemein › „Einführung“. Nach einem Update zeigt das Panel einmal eine Karte **„Neu in dieser Version“**.

Der Installer braucht **keine Adminrechte**: Er installiert für den angemeldeten Benutzer nach
`%LOCALAPPDATA%\Programs\Power-Exec` und legt einen Startmenü-Eintrag sowie einen Eintrag unter
„Apps & Features“ an. Updates installiert Power-Exec nach Bestätigung selbst über den Installer (still im Hintergrund).
Beim Deinstallieren wird gefragt, ob auch die Einstellungen gelöscht werden sollen.

Ohne Installation: `PowerExec.exe` aus dem Release in einen Ordner mit Schreibrechten legen und starten (portable Nutzung).

Beide Varianten sind eigenständig, eine .NET-Installation ist nicht nötig. Die installierte Version startet schneller,
weil sie als vorkompilierter Programmordner ausgeliefert wird; die portable EXE entpackt sich beim ersten Start einmalig. Voraussetzung: Windows 10 oder 11 (x64).
Da die Dateien nicht signiert sind, kann Windows SmartScreen beim ersten Start warnen („Weitere Informationen“ → „Trotzdem ausführen“).

Einstellungen: `%APPDATA%\Power-Exec\settings.json`
Logs der Remote-Ausführung: `%LOCALAPPDATA%\Power-Exec\Logs` (einstellbar)

## Scripte für Power-Exec schreiben

```powershell
<#
.SYNOPSIS
    Eine Zeile, die im Panel erscheint.
.DESCRIPTION
    Ausführlicher Text, sichtbar beim Aufklappen und im Dialog.
.PARAMETER ComputerName
    Hilfetext unter dem Eingabefeld.
.NOTES
    PowerExec: CopyFolder   # optional: Scriptordner bei Remote immer mitsenden
#>
param(
    [Parameter(Mandatory)][string]$ComputerName,
    [ValidateSet('Schnell','Gründlich')][string]$Modus = 'Schnell',
    [switch]$Force
)
```

- Für Remote und Massenausführung kein `Read-Host` verwenden, sondern Parameter. Der Dialog warnt, wenn ein Script `Read-Host` enthält.
- Scripte für Remote sollten unter Windows PowerShell 5.1 laufen.
- Ein vollständiges Beispiel liegt in [`samples/Get-DiskSpace.ps1`](samples/Get-DiskSpace.ps1).

## Entwicklung

```bash
dotnet test tests/PowerExec.Core.Tests
```

```bash
dotnet build src/PowerExec/PowerExec.csproj
```

```bash
dotnet publish src/PowerExec/PowerExec.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o out
```

Aufbau:

- `src/PowerExec.Core` – plattformunabhängige Logik: Ordnersuche, Ausschlüsse, Script-Hilfe und Parameter (über den PowerShell-Parser), Befehlsaufbau, Remote-Runner, Update-Prüfung
- `src/PowerExec` – WPF-Anwendung: Panel, Dialoge, Einstellungen, Windows-Integration
- `tests/PowerExec.Core.Tests` – Tests für die Kernlogik
- `installer/PowerExec.iss` – Installer (Inno Setup 6)

### Releases

Ein Release entsteht automatisch, sobald auf `main` eine neue Versionsnummer in
[`Directory.Build.props`](Directory.Build.props) landet:

```xml
<Version>0.2.0</Version>
```

Die GitHub Action legt dann den Tag `v0.2.0` an, baut `PowerExec.exe` und den Installer
`PowerExec-Setup.exe` (Inno Setup, `installer/PowerExec.iss`) samt Prüfsummen und veröffentlicht das Release.
Versionen mit Bindestrich (z. B. `0.2.0-beta.1`) werden als Vorabversion markiert.
Alternativ startet „Run workflow“ unter *Actions → Release* oder ein manuell gepushter Tag `v*` den Build.
Jeder Push baut zur Kontrolle beides außerdem als Artefakt der CI.
