# Power-Exec

Schneller Zugriff auf Scripte und Programme über ein Panel am Bildschirmrand.
Fährt die Maus an den Rand, gleitet das Panel nach einer kurzen Verweilzeit heraus.
Dort lässt sich suchen, filtern und starten: lokal, als Administrator, als anderer Benutzer
oder remote auf einem oder vielen Rechnern.

## Funktionen

**Panel**
- Hover-Zone am Bildschirmrand: Rand, Monitor, Lage, Länge und Breite einstellbar; sie übernimmt die Panelfarbe
- Panel: Breite, Höhe (30–100 %) und Lage entlang der Kante (an der Hover-Zone, oben, mittig, unten) einstellbar, ebenso Verweilzeit, Einklappzeit und Animation
- **Mehrere Panels** an verschiedenen Rändern oder Monitoren, jedes mit eigener Hover-Zone, Farbe, Deckkraft, Größe, eigenem Tastenkürzel und eigenem Inhalt (Auswahl von Ordnern, Kategorien und „Eigene Einträge“)
- **Größe per Ziehen** am Innenrand des Panels (Breite und Höhe werden gespeichert)
- **Kompakte Ansicht** (eine Zeile pro Eintrag) per Knopf im Panel-Kopf, je Panel
- Globales Tastenkürzel (Standard: `Strg+Alt+Leertaste`) öffnet das Panel und setzt den Cursor ins Suchfeld
- Anheften, damit das Panel offen bleibt
- Panelfarbe frei wählbar (Vorlagen oder eigene Farbe als `#RRGGBB`) samt Deckkraft; bei hellen Farben wird die Schrift automatisch dunkel
- Vorausgewählter Filter beim Öffnen einstellbar (Alle, Scripte, Programme oder Favoriten)
- Symbole: Programmsymbol bei EXE, Verknüpfungen und Snap-ins, **Favicon bei Webseiten**, Ordnersymbol bei Ordnern, Remotedesktop-Symbol bei RDP, Programmsymbol bei Befehlen
- Suche über Dateiname, Synopsis und Beschreibung; Filter: Alle, Scripte, Programme, Favoriten
- Gruppen (Favoriten und Ordner) einklappbar; Kontextmenü pro Gruppe (Im Explorer öffnen, Neu einlesen, Einklappen …)
- Kontextmenü pro Eintrag: Ausführen, Admin, Als anderer Benutzer, Remote, Im Editor öffnen, Speicherort öffnen, Pfad kopieren, Favorit, Ausblenden
- Tastatur: Pfeiltasten wählen aus, Enter startet, Esc leert die Suche bzw. schließt das Panel
- Gruppe **„Zuletzt ausgeführt“** (mit dem verwendeten Profil, Anzahl einstellbar)
- **Eigene Gruppen** direkt im Panel anlegen (Rechtsklick auf eine Gruppe oder + unten), umbenennen, verschieben, löschen; Einträge per **Drag & Drop** hineinschieben, zurück in den Ordner ziehen oder auf „Favoriten“ ziehen; Reihenfolge per Drag & Drop
- **Aus dem Explorer und Browser ziehen:** Scripte, Programme, Ordner und Links ins Panel bzw. direkt in eine Gruppe ziehen (auch an den Bildschirmrand – das Panel fährt dann aus). Dateien verhalten sich wie gefundene Dateien (Synopsis, Parameter-Dialog, Remote, Profile)
- Neue Einträge direkt in einer Gruppe anlegen: Webseite, Ordner, Remotedesktop, Befehl oder Dateien auswählen
- Darstellung: eigene Gruppen oben, darunter die Ordner – oder nur eigene Gruppen und „Ohne Gruppe“
- Doppelklick startet das Standardprofil; Split-Button für weitere Profile
- Hover-Zone wird bei **Vollbild-Programmen** (Video, Präsentation, Spiel, RDP) automatisch deaktiviert
- **Tray-Schnellstart:** Favoriten und „Zuletzt ausgeführt“ direkt aus dem Tray-Menü starten, Panels öffnen, Arbeitsbereich wechseln

**Arbeitsbereiche**
- Getrennte Sammlungen, z. B. je Kunde oder Aufgabe: eigene Ordner, Profile, Kategorien, Favoriten, eigene Einträge und Verlauf
- Wechsel im Panel-Kopf, im Tray-Menü oder in den Einstellungen; gilt für alle Panels. Panels, Farben, Rechnergruppen und allgemeine Einstellungen sind gemeinsam
- Neuer Arbeitsbereich leer oder als Kopie des aktuellen

**Eigene Einträge ohne Datei**
- Webseiten, Ordner/Freigaben, Remotedesktop-Verbindungen (`mstsc /v:`) und beliebige Befehle mit Argumenten
- Erscheinen in der Gruppe „Eigene Einträge“, lassen sich favorisieren, kategorisieren, mit Profilen und Notizen versehen

**Ordner**
- Beliebig viele Ordner, auch Netzwerkfreigaben (UNC)
- Die Einstellungen zeigen pro Ordner **alle gefundenen Einträge** in einer Tabelle: Haken = im Panel sichtbar, dazu Startprofile, Admin-Markierung und ⚙ zum Konfigurieren; Suche, Typfilter, „Alle anzeigen“ / „Keine“
- **Neue Dateien** pro Ordner: automatisch anzeigen oder erst nach Freigabe (Hinweis im Panel und in den Einstellungen, „Alle freigeben“ / „Ausgeblendet lassen“)
- Ordner-Optionen: Unterordner ja/nein, Dateiendungen, Muster-Ausschlüsse (`*.exe`, `*\Archiv\*`, `_*`); per Muster ausgeschlossene Dateien erscheinen ausgegraut mit Begründung; zusätzlich globale Ausschlüsse
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
- CredSSP wird bewusst nicht unterstützt (Anmeldedaten würden an den Zielrechner weitergegeben)
- **Verbindungstest** je Rechner im Dialog: WinRM erreichbar, Anmeldung, PowerShell-Version, PowerShell 7; Option **HTTPS (5986)**; verständliche Hinweise bei TrustedHosts- und DNS-Problemen
- **Windows-Benachrichtigung**, wenn ein Remote-Lauf fertig ist (Klick öffnet die Ergebnisliste)

**Sicherheit**
- Anmeldedaten werden bei jedem Start abgefragt und nie gespeichert
- Bei Remote gehen sie nur über stdin an den lokalen Runner-Prozess, nie über die Kommandozeile oder eine Datei
- Netzlaufwerke werden vor dem Start in UNC-Pfade umgewandelt (erhöhte Prozesse und andere Konten sehen keine verbundenen Laufwerke). Fehlt dem gewählten Konto der Zugriff, erscheint ein klarer Hinweis.

- **Blockierte Dateien** (aus dem Internet heruntergeladen) werden markiert und lassen sich mit „Entsperren“ freigeben; beim Aufklappen eines Eintrags wird die **digitale Signatur** angezeigt

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
3. Fertig. Beim ersten Start öffnen sich die Einstellungen, um Ordner hinzuzufügen.

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
