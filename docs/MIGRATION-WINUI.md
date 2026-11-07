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

### Phase 0 – ViewModels herauslösen (in WPF) – abgeschlossen

- Neues Projekt `src/PowerExec.UI.Core` (net10.0, ohne WPF/WinUI) mit CommunityToolkit.Mvvm.
- Logik aus dem Code-behind in ViewModels und Dienste verschieben; UI-Abhängigkeiten hinter Schnittstellen
  (Fenster/Dialoge, Zwischenablage, Tray, Dispatcher, Bildschirme, Symbole).
- Neues Testprojekt `tests/PowerExec.UI.Core.Tests`.
- Prüfen, dass Einstellungs- und Verlaufsdateien (`%LOCALAPPDATA%\Power-Exec\…`) von zwei Oberflächen
  gelesen werden können, ohne dass eine die Dateien für die andere unbrauchbar macht.
- Vorschlag für das erste Fenster: `RunWindow` (überschaubar, aber echt).

**Fertig, wenn:** WPF unverändert auf den neuen ViewModels läuft und alle Tests grün sind.

#### Fortschritt Phase 0

Befund beim Start: Es gibt bereits 21 ViewModels in `src/PowerExec/ViewModels`. Phase 0 ist deshalb vor allem
**Entkoppeln** (WPF-Typen und `AppController` hinter Schnittstellen) und **Verschieben**, weniger Neuschreiben.

Vorgehen je ViewModel: WPF-Abhängigkeiten durch eine schmale Host-Schnittstelle ersetzen (`I…Host`, von
`AppController` explizit implementiert, Fenster als `object? owner`), Datei nach `PowerExec.UI.Core` verschieben
(Namespace bleibt), Tests mit einem Fake-Host schreiben.

| Baustein | Stand |
|---|---|
| Projekte `PowerExec.UI.Core`, `PowerExec.UI.Core.Tests`; Tests in CI und Release | erledigt |
| `ObservableObject`, `RelayCommand` – `CommandManager` ersetzt durch `CommandRequery` (WPF: `WpfCommandRequery`) | erledigt |
| `AppLog`, `RunCredential` | erledigt |
| `RunViewModel` (+ `ParameterViewModel`, `MachineViewModel`) über `IRunHost` | erledigt, 14 Tests |
| `UpdateState`, `HistoryRowViewModel` (`IHistoryHost`), `ScheduleViewModel` (`IScheduleHost`), `WorkflowEditorViewModel` (`IWorkflowHost`), `WelcomeViewModel` (`IWelcomeHost`, Ordnerauswahl über den Host; Beispiel-Script als Ressource in UI.Core) | erledigt, 20 Tests |
| `DisplayText` (Dauer, Tastenkürzel) – gemeinsam statt in `ActivityViewModel`/`HotkeyService` | erledigt |
| `ActivityViewModel`, `ActivityList` – `IUiDispatcher`/`IUiTimer` statt `Dispatcher`, `IRemoteRunProgress` statt `ResultsViewModel` | erledigt, 12 Tests |
| **Panel-Block**: `PanelViewModel`, `GroupViewModel`, `ItemViewModel`, `PeekViewModel`, `InlineParametersViewModel`, `SpotlightViewModel` über `IPanelHost`/`IPanelContext` | erledigt, 13 Tests |
| ↳ dafür: `ArgbColor` + `ArgbBrushConverter` (Farben), `IconImage`/`IIconProvider` + `IconImageConverter` (Symbole), `IShell`, `IDialogs`, `IThemeInfo`, `IItemSource`, `HealthService`, `SignatureService`, `TextPart`, `EntryBadge` | erledigt |
| `ResultsViewModel` + `RemoteSession` über `IResultsHost`; `FilteredView<T>` statt `CollectionView` | erledigt, 9 Tests |
| `CustomEntryViewModel`, `ProfilesViewModel`, `CustomEntryListViewModel` über `ICustomEntryHost`/`IProfilesHost`/`ICustomEntryListHost`; `BrowserCatalog` | erledigt, 14 Tests |
| `TagsViewModel`, `PanelSettingsViewModel`, `SettingsExtras` über `ISettingsHost` | erledigt, 11 Tests |
| `SettingsViewModel` (+ Ordner-, Gruppen-, Katalog-, Designvorlagen-Zeilen) über `ISettingsWindowHost` | erledigt, 12 Tests |

**Stand:** Alle ViewModels liegen in `PowerExec.UI.Core` (net10.0, ohne WPF); das WPF-Projekt enthält nur noch
Fenster, Steuerelemente, Converter und die Win32-/WPF-Dienste. `IDialogs` deckt alle Rückfragen und
Auswahldialoge ab. **Phase 0 ist abgeschlossen** (6. November 2026): Unit-Tests der Oberflächenlogik (111) und
CI mit Selbsttest der WPF-App grün ([Lauf 148](https://github.com/Ga-Re/Power-Exec/actions/runs/42195899834)).

Muster für die Oberfläche (WPF heute, WinUI später): Farben als `ArgbColor`, Symbole als `IconImage`, Sichtbarkeit
als `bool` – die Oberfläche wandelt per Converter um. Die WPF-App greift über `InternalsVisibleTo` auf interne
Abgleich-Hilfen der ViewModels zu.

Hinweis zum Bauen: `PowerExec.UI.Core` und die Tests laufen auch unter Linux; die WPF-App kompiliert dort dank
`EnableWindowsTargeting`, startet aber nur unter Windows. Der Test `V016Tests.WorkflowEntryDescribesStepsAndClonesDeep`
erwartet Windows-Pfade und schlägt nur unter Linux fehl.

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

#### Stand Phase 1

Prototyp `src/PowerExec.WinUI.Spike` (Windows App SDK 2.6.5, WinUIEx 2.9.3, H.NotifyIcon 2.4.1; unpackaged,
self-contained). Bewusst ohne XAML-Dateien – die Oberfläche entsteht in C#, Vorlagen werden zur Laufzeit geladen –,
damit der Code auch ohne Windows typgeprüft werden kann. Er nutzt das echte `PanelViewModel` aus UI.Core mit
500 erfundenen Einträgen in 20 Gruppen. Nicht in `PowerExec.sln` (baut nur unter Windows).

| Kriterium | Umsetzung im Prototyp |
|---|---|
| P1 | Rahmenloses Topmost-Fenster; Gleiten je Bildschirmbild per `AppWindow.Move`, außerhalb des Monitors per Fensterregion abgeschnitten; Klarglas (`TransparentTintBackdrop`), Acrylic, Mica; Milchglas-Regler |
| P2 | 5-px-Streifen am rechten Rand, 250 ms Verweilzeit, Ablegen am Rand fährt das Panel aus; Vollbild-Erkennung wie in der WPF-App; Pulsieren |
| P3 | Tray-Symbol mit Menü (H.NotifyIcon), Strg+Alt+Leertaste (sonst Strg+Alt+Umschalt+Leertaste), Autostart über Run-Schlüssel mit `--background` |
| P4 | Ablegen aus Explorer/Browser → Gruppe „Abgelegt“; Gruppen umsortieren mit Lücke (`ListView.CanReorderItems`); Tipp-Messung in der Fußzeile |

**Ausprobieren:** Workflow „WinUI-Prototyp“ → Artefakt `PowerExec-WinUI-Prototyp` herunterladen, entpacken,
`PowerExec.WinUI.Spike.exe` starten. Läuft neben der WPF-Version. `PowerExec.WinUI.Spike.exe --smoke ergebnis.md`
spielt die Prüfpunkte automatisch durch (das macht auch CI). Protokoll: `spike.log` neben der EXE.

**Smoke-Test in CI** (Windows Server 2025, 4 Kerne, ein Monitor; [Lauf 3](https://github.com/Ga-Re/Power-Exec/actions/runs/42218229820)):
alle Schritte ohne Fehler. Gleiten Ø 14 Bilder in ~210 ms (größte Bildlücke 31 ms); Tippen mit 500 Einträgen
Ø 23 ms, p95 34 ms (WPF-App im selben Runner: Ø 223 ms – WinUI virtualisiert die Gruppenliste, der Vergleich ist
also nur grob); Klarglas/Acrylic/Mica, Tray, Strg+Alt+Leertaste, Autostart-Eintrag funktionieren. Gefunden:
Windows erzwang 136 px Mindestbreite für die Hover-Zone → per `WM_GETMINMAXINFO` behoben, jetzt 6 px
([Lauf 4](https://github.com/Ga-Re/Power-Exec/actions/runs/42219168771)); der Smoke-Test prüft ≤ 12 px.

Unterwegs gelernt (gilt für WinUI ohne XAML-Dateien, nicht für Phase 2 mit XAML): `EnableMsixTooling` ist auch
unpackaged nötig (sonst fehlt die `.pri`-Datei mit den WinUI-Themes), und die App muss `IXamlMetadataProvider`
selbst an `XamlControlsXamlMetaDataProvider` weiterreichen.

**Sichtprüfung (Rückmeldung, 6. November 2026):** Panel fährt unter WinUI **ohne Flackern** aus (unter WPF
flackert es) → P1 „Gleiten“ erfüllt. Hover-Zone reagierte nicht auf die Maus (Panel nur über das Tray) → Zone
erkennt die Maus jetzt zusätzlich per Abfrage der Mausposition (unabhängig vom Treffer auf das schmale Fenster),
protokolliert Auslöser und Vollbild-Ausblendung in `spike.log`; der Smoke-Test fährt die Maus echt an den Rand.

**Bestätigt am eigenen Rechner (7. November 2026):** Hover-Zone fährt das Panel aus, Panel über das Tray-Menü,
Gleiten ohne Flackern. Damit gilt Phase 1 als bestanden; die übrigen Sichtprüfungen laufen nebenher weiter.

**Noch nicht ausdrücklich bestätigt:** Glas-Wirkung, Ruckeln beim Gleiten, Taskleiste und zweiter Monitor, echtes Ziehen aus
Explorer/Browser, Lücke beim Gruppen-Ziehen, Tray-Menü, Start ohne Fenster nach Neuanmeldung.

### Phase 2 – Kernfenster

**Entscheidung (7. November 2026): gemeinsamer App-Kern.** `AppController` zieht – wie die ViewModels in Phase 0 –
nach `PowerExec.UI.Core`; Fenster, Tray, Tastenkürzel und Bildschirme erreicht er über eine Schnittstelle
(`IAppUi`), die WPF und WinUI je einmal umsetzen. So kann die Beta sofort alles mit den echten Daten; die
WPF-App wird dafür noch einmal umgebaut, der CI-Selbsttest sichert das ab. XAML-Fenster lassen sich nur unter
Windows übersetzen (XAML-Compiler), die Logik wird weiter unter Linux getestet.


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
