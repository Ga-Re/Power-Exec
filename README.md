# Power-Exec

Schneller Zugriff auf Scripte und Programme über ein Panel am Bildschirmrand.
Fährt die Maus an den Rand, gleitet das Panel nach einer kurzen Verweilzeit heraus.
Dort lässt sich suchen, filtern und starten: lokal, als Administrator, als anderer Benutzer
oder remote auf einem oder vielen Rechnern.

## Funktionen

- **Panel am Bildschirmrand:** Hover-Zone oder Tastenkürzel, Liste oder Raster, Gruppen mit Untergruppen, Drag & Drop, anheften
- **Suche:** unscharf, mit Filtern (`#schlagwort`, `typ:`, `gruppe:`, `ist:`), Vorschau mit der Leertaste, Spotlight-Modus
- **Starten:** lokal, als Administrator, als anderer Benutzer oder remote auf vielen Rechnern; Startprofile, Parameter-Dialog, Verlauf
- **Eigene Einträge:** Webseiten, Ordner, RDP, SSH, Snippets, Windows-Werkzeuge, Befehle; Platzhalter und Variablen
- **Smarte Gruppen:** Ordner (auch Freigaben) erscheinen als Gruppe, Unterordner als Untergruppen
- **Abläufe und Zeitpläne:** mehrere Einträge nacheinander, einmalig oder wiederkehrend
- **Arbeitsbereiche, Team-Kataloge, Marktplatz:** Inhalte je Kunde oder Aufgabe, im Team teilen, fertige Einträge und Designs laden
- **Aussehen:** Designvorlagen, Glas oder Vollton, Panelfarbe, Akzent, Ecken, Schriftgröße; Deutsch und Englisch

Alle Funktionen im Einzelnen: [docs/FEATURES.md](docs/FEATURES.md) · Änderungen je Version: [CHANGELOG.md](CHANGELOG.md) ·
Mitarbeiten: [CONTRIBUTING.md](CONTRIBUTING.md)

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
- Scripte mit Umlauten als **UTF-8 mit BOM** speichern – sonst zeigt Windows PowerShell 5.1 sie falsch an.

### Beispielscripte

Die Einrichtung legt sie unter Dokumente › Power-Exec › Scripte an; später geht das unter Einstellungen › Hilfe › Diagnose › „Beispielscripte anlegen“ (vorhandene Dateien bleiben unverändert).

| Script | Zeigt |
|---|---|
| [`Get-DiskSpace.ps1`](samples/Get-DiskSpace.ps1) | Pflichtparameter, Auswahlliste, Schalter, CSV-Export, `PowerExec: CopyFolder` |
| [`Get-SystemInfo.ps1`](samples/Get-SystemInfo.ps1) | Systemdaten lokal oder remote auf vielen Rechnern, Ausgabe als JSON |
| [`Test-NetworkConnection.ps1`](samples/Test-NetworkConnection.ps1) | mehrere Ziele in einem Feld, Ping oder TCP-Port mit Zeitlimit |
| [`Get-LargeFiles.ps1`](samples/Get-LargeFiles.ps1) | Ordner durchsuchen, Wertebereiche, Export |
| [`Get-InstalledSoftware.ps1`](samples/Get-InstalledSoftware.ps1) | installierte Programme aus der Registrierung, Filter mit Platzhaltern |
| [`Get-RecentErrors.ps1`](samples/Get-RecentErrors.ps1) | Fehler aus der Ereignisanzeige, nach Quelle gruppiert |
| [`Clear-TempFiles.ps1`](samples/Clear-TempFiles.ps1) | Probelauf (`-WhatIf`), Admin-Option für Windows-Temp |
| [`New-Password.ps1`](samples/New-Password.ps1) | sichere Kennwörter, Ergebnis in der Zwischenablage |

## Entwicklung

```bash
dotnet test tests/PowerExec.Core.Tests
```

```bash
# Leistung des Datenteils (BenchmarkDotNet); Oberfläche misst der Selbsttest in der CI
dotnet run -c Release --project tests/PowerExec.Core.Benchmarks -- --filter *
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
- `tests/PowerExec.Core.Benchmarks` – Benchmarks; Messverfahren, Ausgangswerte und Verlauf in [`docs/PERFORMANCE.md`](docs/PERFORMANCE.md)
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
