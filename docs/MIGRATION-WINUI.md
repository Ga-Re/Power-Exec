# Migration WPF → WinUI 3

Stand: November 2026, Version 0.19.5. Ergebnis eines Entscheidungs-Interviews; dieses Dokument ist die
Grundlage für die Umsetzung und wird bei jeder Entscheidung fortgeschrieben.

## Ziel

Power-Exec wird **vollständig** von WPF auf **WinUI 3 (Windows App SDK)** umgestellt.

- **Warum:** natives Fluent-Aussehen und Zukunftssicherheit (WPF wird nur noch gepflegt, WinUI weiterentwickelt).
- **Anspruch:** Funktionsumfang **1:1** – jede Funktion aus der [README](../README.md) bleibt erhalten.
  Ausnahme nur, wenn etwas nachweislich nicht umsetzbar ist; dann wird **Fall für Fall** entschieden und
  unten unter [Entscheidungen im Einzelfall](#entscheidungen-im-einzelfall) mit Begründung festgehalten.

> Hinweis: Das Fluent-Aussehen allein gäbe es auch in WPF (`ThemeMode="Fluent"` seit .NET 9). Der Umstieg
> wird daher vor allem mit Zukunftssicherheit begründet; diese Abwägung gilt bei jedem Einzelfall.

## Rahmenbedingungen

| Thema | Entscheidung |
|---|---|
| Auslieferung | **Unpackaged, self-contained** (Windows App SDK-Runtime im Programmordner), weiterhin mit Inno Setup (`installer/PowerExec.iss`). Die heutige Einzeldatei-EXE entfällt – ausgeliefert wird ein Ordner. |
| Windows-Versionen | **Windows 10 1809+ und Windows 11**. Mica nur unter Windows 11; unter Windows 10 Ersatz wie heute (Aero-Blur/Acrylic). |
| Bibliotheken | Bewährte Fremdbibliotheken sind erlaubt: **WinUIEx** (Fenster, transparente Backdrops), **H.NotifyIcon.WinUI** (Tray), **CommunityToolkit.Mvvm** und **CommunityToolkit WinUI Controls**. Lizenzen in `THIRD-PARTY-NOTICES.md` ergänzen. |
| Design | **Fluent als Basis** (WinUI-Standard-Controls und -Styles). Designvorlagen (Fluent, Glas, Graphit, Papier, Mitternacht), Panelfarbe, Deckkraft, Akzentfarbe, Kontrastdesign und Schrift-/Elementgröße 80–150 % werden über **ThemeResources** darübergelegt. Die heutigen `Themes/*.xaml` werden nicht 1:1 nachgebaut. |
| Architektur | **Erst ViewModels herauslösen** – noch in WPF – in eine UI-neutrale Bibliothek; WinUI setzt danach darauf auf. |
| WPF während der Migration | **Feature-Freeze**: nur noch Bugfixes. Einzige größere Änderung ist das Herauslösen der ViewModels (Phase 0). |
| Umstellung für Nutzer | **Beta parallel**, die dieselben Einstellungsdateien liest; sobald gleichwertig, wird die WinUI-Version als **1.0** die einzige. |
| Absicherung | **Unit-Tests für die ViewModels** und eine **Abnahme-Checkliste aus der README-Funktionsliste**. |
| Build | WinUI 3 baut nur unter Windows; CI läuft bereits auf `windows-latest`. |

## Ausgangslage

- `src/PowerExec.Core` (net10.0, ohne UI) ist bereits sauber getrennt und bleibt unverändert nutzbar.
- `src/PowerExec` (WPF + WinForms für `NotifyIcon`): 19 Fenster, ~6.700 Zeilen XAML, ~19.000 Zeilen C#,
  großer Teil der Logik im Code-behind.
- Kritische Stellen ohne direkte WinUI-Entsprechung:
  - 7 Fenster mit `AllowsTransparency="True"`, rahmenlos und Topmost (Panel, Hover-Zone, Overlays)
  - eigenes Interop für Acrylic/Blur/Mica (`Interop/Acrylic.cs`, `Interop/NativeMethods.cs`)
  - Tray-Icon über WinForms (`Services/TrayIcon.cs`), Bildschirm-/Monitorlogik (`Services/ScreenService.cs`)
  - Einzeldatei-Veröffentlichung (`.github/workflows/release.yml`)

## Phasen

### Phase 0 – ViewModels herauslösen (in WPF)

- Neues Projekt `src/PowerExec.UI.Core` (net10.0, ohne WPF/WinUI) mit CommunityToolkit.Mvvm.
- Logik aus dem Code-behind in ViewModels und Dienste verschieben; UI-Abhängigkeiten hinter Schnittstellen
  (Fenster/Dialoge, Zwischenablage, Tray, Dispatcher, Bildschirme, Symbole).
- Neues Testprojekt `tests/PowerExec.UI.Core.Tests`.
- Prüfen, dass Einstellungs- und Verlaufsdateien (`%LOCALAPPDATA%\Power-Exec\…`) von zwei Oberflächen
  gelesen werden können, ohne dass eine die Dateien für die andere unbrauchbar macht.
- Vorschlag für das erste Fenster: `RunWindow` (überschaubar, aber echt).

**Fertig, wenn:** WPF unverändert auf den neuen ViewModels läuft und alle Tests grün sind.

### Phase 1 – Prototyp (Machbarkeit)

Eigenes Projekt `src/PowerExec.WinUI.Spike`. Er prüft nur das Unsichere – keine Standard-Controls.

| # | Kriterium | Muss zeigen |
|---|---|---|
| P1 | **Panel: Glas & Gleiten** | Rahmenloses Topmost-Fenster, flüssiges Ein-/Ausfahren (Verschieben, nicht Größe ändern), Klarglas, Acrylic, Mica und Milchglas-Regler; korrekt an der Taskleiste und auf zweitem Monitor, verschiedene DPI |
| P2 | **Hover-Zone & Vollbild** | Schmale transparente Zone, die nicht stört, sich bei Vollbild-Programmen (Video, Präsentation, Spiel, RDP) abschaltet und pulsieren kann |
| P3 | **Tray, Hotkey, Autostart** | Tray-Icon mit Menü, globales Tastenkürzel, Start ohne sichtbares Fenster – in der unpackaged Auslieferung |
| P4 | **Drag & Drop + Leistung** | Ziehen aus Explorer und Browser, Lücke mit Platzhalter beim Gruppen-Ziehen, Liste mit 500 Einträgen, Tippen im Suchfeld im Mittel ≤ 170 ms wie heute (siehe [PERFORMANCE.md](PERFORMANCE.md)) |

**Fertig, wenn:** alle vier Kriterien erfüllt sind oder jedes nicht erfüllte als Einzelfall entschieden ist.
Ansatz für P1: eigene `SystemBackdrop` bzw. Composition (z. B. WinUIEx `TransparentTintBackdrop`).

### Phase 2 – Kernfenster

Reihenfolge nach Nutzung, aufbauend auf dem Prototyp:

1. `PanelWindow`, `SpotlightWindow`, `DockWindow`
2. `RunWindow`, `CredentialWindow`, `ResultsWindow` (inkl. Verlauf)

**Fertig, wenn:** Beta 1 als Vorabversion erscheint und dieselbe Konfiguration wie die WPF-Version liest.

### Phase 3 – Übrige Fenster

`SettingsWindow`, `CustomEntryForm`, `ProfilesWindow`, `ScheduleWindow`, `WorkflowWindow`, `CatalogWindow`,
`ColorPickerWindow`, `IconPickerWindow`, `WelcomeWindow`, danach die kleinen Dialoge `InputWindow`,
`ChoiceWindow`, `NewGroupWindow`, `PlaceholderWindow`.

**Fertig, wenn:** alle 19 Fenster portiert sind.

### Phase 4 – Abnahme und Wechsel

- Abnahme-Checkliste (aus der README-Funktionsliste) vollständig abhaken, inkl. Leistung.
- Installer auf Programmordner umstellen, CI- und Release-Workflow auf WinUI umstellen.
- WPF-Projekt entfernen, README aktualisieren.

**Fertig, wenn:** Release **1.0** nur noch WinUI enthält.

## Risiken und offene Punkte

- **Klarglas und Milchglas-Regler** sind das größte technische Risiko (P1).
- **Leistung** wird nicht durch eigene WinUI-Benchmarks abgesichert, sondern über P4 und die Abnahme-Checkliste.
- **Gemeinsame Konfiguration** von WPF und WinUI-Beta muss in Phase 0 geklärt sein.
- **Kein Bau unter Linux:** WinUI-Code kann nur unter Windows (lokal oder in CI) gebaut und getestet werden.

## Entscheidungen im Einzelfall

Hier wird jede Abweichung vom 1:1-Anspruch mit Datum, betroffener Funktion, Grund und gewähltem Ersatz
eingetragen.

| Datum | Funktion | Problem | Entscheidung |
|---|---|---|---|
| – | – | – | – |
