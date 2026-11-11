# Mitarbeiten an Power-Exec

Diese Regeln gelten für alle Änderungen – von Hand oder mit KI-Unterstützung.

## Sprache

| Was | Sprache |
|---|---|
| Bezeichner (Klassen, Methoden, Variablen), Dateinamen | Englisch |
| Kommentare, XML-Doku, `docs/`, Commit-Nachrichten | Deutsch |
| Texte der Oberfläche | Englisch als Schlüssel in `L.T("…")` bzw. `{l:T '…'}`, Übersetzungen in `Translations/<sprache>.json` |
| Protokoll (`AppLog`) | Englisch, **nicht** über `L.T` – das Protokoll soll unabhängig von der Spracheinstellung lesbar sein |

## Schichten

| Projekt | Namensraum | Inhalt | Darf abhängen von |
|---|---|---|---|
| `PowerExec.Core` | `PowerExec.Core.*` | Modelle, Einstellungen, Suche, Layout-Berechnung – reine Logik | – |
| `PowerExec.UI.Core` | `PowerExec.UI.*` | `AppController`, ViewModels, Dienste ohne WPF | Core |
| `PowerExec` | `PowerExec.Desktop.*` | Fenster, Controls, Themes, Win32 | Core, UI.Core |

- Die App heißt im Namensraum `Desktop`, nicht `Wpf`: die Bibliothek WPF UI belegt `Wpf.Ui`, ein eigener Namensraum
  `PowerExec.Wpf` würde deren Typen in jedem Fenster verdecken.
- Logik, die sich ohne Fenster prüfen lässt, gehört nach Core bzw. UI.Core und bekommt einen Test.
- ViewModels kennen keine WPF-Typen. Was die Oberfläche können muss, steht als Schnittstelle in UI.Core (`IAppUi`, `IPanelView`, …).

## Win32

- `DllImport`/`LibraryImport` stehen nur in den Ordnern `Interop/`. Fenster und Dienste rufen benannte Methoden auf.
- Konstanten (`WM_…`, `SWP_…`) gibt es genau einmal, in `NativeMethods`. Keine Zahlen wie `0x0086` im Aufruf.

## Fehler

- Kein `catch { }` ohne Kommentar, **warum** der Fehler hier ohne Belang ist. Unerwartetes wird mit `AppLog.Warn`/`AppLog.Error` protokolliert.
- `catch` so eng wie möglich (`IOException`, `UnauthorizedAccessException`, `Win32Exception` …).
- Aufgaben, auf die niemand wartet, laufen über `Background.Run(…)` bzw. `task.Forget("…")` – Ausnahmen landen im Protokoll statt zu verschwinden.

## Zeit

- Dauern und Abstände: `Stopwatch` oder `DateTime.UtcNow`.
- Zeitstempel, die gespeichert werden: UTC.
- `DateTime.Now` nur für Anzeige und für Regeln, die ausdrücklich in Ortszeit gelten (Zeitpläne „täglich 07:00“).

## Oberfläche

- Farben, Radien, Schriftgrößen, Höhen und Animationsdauern kommen aus den Design-Tokens (`Themes/Tokens.xaml`, in C# `Tokens`). Keine Zahlen in Vorlagen oder im Code-Behind.
- Eigene Controls sind „lookless“: Verhalten in C#, Aussehen als Vorlage in `Themes/Generic.xaml`.
- Geometrie von Monitoren ist eine Momentaufnahme (`MonitorInfo`). Vor dem Platzieren neu abfragen (`ScreenService.Refresh`); Änderungen meldet `DisplayTopology`.

## Tests

- Testdateien heißen nach dem Thema (`SearchTests`, `WorkflowTests`), nicht nach der Version.
- `dotnet test tests/PowerExec.Core.Tests` und `dotnet test tests/PowerExec.UI.Core.Tests` laufen auch unter Linux.
- Die Oberfläche prüft der Selbsttest (`PowerExec.exe --selftest <Datei>`) in der CI unter Windows; dort entstehen auch Bildschirmfotos.
  Sein Code (`src/PowerExec/SelfTest/`) ist nur in Debug-Builds und mit `-p:SelfTest=true` enthalten, nicht in der ausgelieferten EXE.

## Format und Analyse

- `.editorconfig` legt die Formatierung fest; die CI prüft mit `dotnet format whitespace --verify-no-changes`.
- Der Build läuft mit `AnalysisLevel=latest-recommended` und Warnungen als Fehler. Ausnahmen stehen mit Begründung in `.editorconfig`.
- Richtwert für die Zeilenlänge: 160 Zeichen.

## Version

Jede Änderung mit sichtbarer Wirkung erhöht `<Version>` in `Directory.Build.props` und bekommt einen Eintrag in
`src/PowerExec.Core/Updates/ReleaseNotes.cs` (samt Übersetzung in `Translations/de.json`). `CHANGELOG.md` wird daraus erzeugt
(`dotnet run --project tools/Changelog`).
