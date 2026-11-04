using System.Windows;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Services;
using PowerExec.ViewModels;
using PowerExec.Views;

namespace PowerExec;

/// <summary>
/// Oberflächen-Selbsttest für die CI (Aufruf: PowerExec.exe --selftest &lt;Ergebnisdatei&gt;).
/// Öffnet Panels in allen Darstellungen, Einstellungen und Dialoge mit Beispieldaten und meldet Fehler,
/// z. B. fehlende Ressourcen in XAML-Vorlagen. Verwendet eine eigene, temporäre Einstellungsdatei.
/// </summary>
public sealed partial class AppController
{
    private readonly List<string> _selfTestErrors = new();
    private string? _progressFile;
    private int _shotCounter;

    /// <summary>Fortschritt in eine Datei schreiben – bei einem Hänger sieht man den letzten Schritt.</summary>
    private void Step(string name)
    {
        if (_progressFile == null) return;
        try { File.AppendAllText(_progressFile, $"{DateTime.Now:HH:mm:ss.fff} {name}{Environment.NewLine}"); } catch { }
    }

    /// <summary>Sammelt WPF-Bindungsfehler „Eigenschaft nicht gefunden“ – z. B. falscher DataContext.</summary>
    private sealed class BindingErrorListener : System.Diagnostics.TraceListener
    {
        public readonly HashSet<string> Errors = new();
        private readonly System.Text.StringBuilder _line = new();
        public override void Write(string? message) => _line.Append(message);
        public override void WriteLine(string? message)
        {
            _line.Append(message);
            var text = _line.ToString();
            _line.Clear();
            if (text.Contains("BindingExpression path error")) lock (Errors) Errors.Add(text);
        }
    }

    public async Task<string?> RunSelfTestAsync(string? progressFile = null)
    {
        _progressFile = progressFile;
        Step("Start");
        var bindingErrors = new BindingErrorListener();
        System.Diagnostics.PresentationTraceSources.Refresh();
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Error;
        var dispatcher = Application.Current.Dispatcher;
        dispatcher.UnhandledException += (_, e) =>
        {
            _selfTestErrors.Add(e.Exception.ToString());
            e.Handled = true;
        };
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "PowerExecSelfTest-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Reset-Password.ps1"), """
                <#
                .SYNOPSIS
                Setzt ein Kennwort zurück.
                #>
                #Requires -Version 5.1
                param(
                    [Parameter(Mandatory)][string]$UserName,
                    [ValidateSet('A','B')][string]$Mode = 'A',
                    [switch]$Force
                )
                Write-Output $UserName
                """);
            File.WriteAllText(Path.Combine(dir, "Cleanup.cmd"), "@echo off\r\nrem Räumt auf\r\necho ok\r\n");
            Directory.CreateDirectory(Path.Combine(dir, "Sub"));
            File.WriteAllText(Path.Combine(dir, "Sub", "Info.ps1"), "<#\n.SYNOPSIS\nZeigt Infos.\n#>\nGet-Date\n");
            var script = Path.Combine(dir, "Reset-Password.ps1");

            Store = new SettingsStore(Path.Combine(dir, "settings.json"));
            Settings = new AppSettings();
            Settings.Folders.Add(new FolderConfig { Path = dir });
            Settings.Folders.Add(new FolderConfig { Path = Path.Combine(dir, "fehlt"), ShowGroup = false });
            GroupManager.Create(Settings, "Admin");
            GroupManager.Create(Settings, "AD", "Admin");
            GroupManager.Create(Settings, "Leer");
            GroupManager.Assign(Settings, script, "Admin/AD");
            GroupManager.SetIcon(Settings, "Admin", IconRef.Parse("fluent:shield"));
            GroupManager.SetIcon(Settings, "Admin/AD", IconRef.Parse("hero:users"));
            GroupManager.SetColor(Settings, "Admin", "#22C55E");
            GroupManager.SetLayout(Settings, "Admin/AD", ItemLayout.Grid);
            Settings.CustomEntries.Add(new CustomEntry { Name = "Intranet", Kind = CustomEntryKind.Url, Target = "https://example.com" });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Freigabe", Kind = CustomEntryKind.Folder, Target = dir });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Server", Kind = CustomEntryKind.Rdp, Target = "SRV-01" });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Terminal", Kind = CustomEntryKind.Command, Target = "cmd.exe" });
            Settings.CustomEntries.Add(new CustomEntry { Kind = CustomEntryKind.File, Target = Path.Combine(dir, "Sub", "Info.ps1") });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Ping", Kind = CustomEntryKind.Snippet, Code = "param([string]$Ziel = 'localhost')\nTest-Connection $Ziel -Count 1", Icon = "hero:signal" });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Signatur", Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text, Code = "Viele Grüße\n{{Name}}" });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Linux", Kind = CustomEntryKind.Ssh, Target = "srv", User = "root", Port = 2222, Icon = "emoji:🐧" });
            Settings.CustomEntries.Add(new CustomEntry { Id = "ad", Name = "AD", Kind = CustomEntryKind.Tool, Target = "dsa.msc", Icon = "fluent:people_team" });
            Settings.Entries.Add(new EntryConfig { Path = "custom:ad", SingleClick = true });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Ticket", Kind = CustomEntryKind.Url, Target = "https://example.com/{{Nr}}", Browser = BrowserChoice.Edge, Private = true, Icon = "glyph:E8EC" });
            Settings.Favorites.Add(Path.Combine(dir, "Cleanup.cmd"));
            Settings.Entries.Add(new EntryConfig { Path = Path.Combine(dir, "Cleanup.cmd"), Notes = "Nur nach Rücksprache" });
            Settings.MachineGroups.Add(new MachineGroup { Name = "Web", Machines = ["WEB1", "WEB2"] });
            Settings.Panels.Add(new PanelConfig { Name = "Hauptpanel", Acrylic = true, PanelOpacity = 90 });
            WorkspaceManager.EnsureInitialized(Settings);
            SmartGroups.EnsureFolderGroups(Settings);
            Settings.DataVersion = 13;
            WorkspaceManager.Create(Settings, "Kunde A", copyCurrent: true);
            HistoryPath = Path.Combine(dir, "history.json");
            Views.WindowHelper.Settings = () => Settings;
            ThemeService.Apply(Settings);

            Icons = new IconService(dispatcher);
            Health = new HealthService(dispatcher);
            Repository = new ItemRepository(Settings, dispatcher);
            Launcher = new Launcher(() => Settings);
            Step("Scan");
            await Repository.ScanAsync();
            Step("Panels erzeugen");
            CreateHosts();
            if (Repository.Items.Count < 3) Fail($"Scan fand nur {Repository.Items.Count} Einträge");

            Step("Symbole prüfen");
            // Alle eingebetteten Symbole müssen sich in WPF-Geometrien umwandeln lassen
            var broken = IconLibrary.Fluent().Concat(IconLibrary.Hero()).Where(c => IconLibrary.GeometryOf(c.Ref) == null).Select(c => c.Ref.ToString()).ToList();
            if (IconLibrary.FluentCount < 1000 || IconLibrary.HeroCount < 200) Fail($"Symbole fehlen: Fluent {IconLibrary.FluentCount}, Hero {IconLibrary.HeroCount}");
            if (broken.Count > 0) Fail($"{broken.Count} Symbole ungültig, z. B. {string.Join(", ", broken.Take(5))}");

            foreach (var (layout, compact) in new[] { (ItemLayout.List, false), (ItemLayout.List, true), (ItemLayout.Grid, false), (ItemLayout.Grid, true) })
            {
                foreach (var host in _hosts)
                {
                    host.Config.Layout = layout;
                    host.Config.Compact = compact;
                    Step($"Panel {host.Config.Name} {layout} kompakt={compact}");
                    host.ViewModel.Rebuild();
                    host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                    await Settle();
                    var first = host.ViewModel.VisibleItems.FirstOrDefault(i => !i.IsSingleClick); // Ein-Klick würde starten
                    if (first == null && host.Config.Name == "Hauptpanel") Fail($"{layout}: keine Einträge im Panel");
                    first?.SelectCommand.Execute(null);
                    await Settle();
                    if (host.Window.Content is FrameworkElement panelRoot) Screenshot(panelRoot, $"panel-{host.Config.Name}-{layout}{(compact ? "-kompakt" : "")}");
                    if (host.Config.Name == "Hauptpanel" && !host.ViewModel.AllGroups.Any(g => g.GroupPath == "Admin/AD" && g.Items.Count == 1))
                        Fail($"{layout}: Untergruppe Admin/AD fehlt oder ist leer");
                    host.Window.HidePanel();
                    await Settle();
                }
            }

            Step("Suche: unscharf, Operatoren, Hervorhebung");
            {
                var host = _hosts[0];
                var vm = host.ViewModel;
                host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                await Settle();
                vm.SearchText = "clnup";
                var hit = vm.AllItems.FirstOrDefault(i => i.Item.FileName == "Cleanup.cmd");
                if (hit == null) Fail("Unscharfe Suche: „clnup“ findet Cleanup.cmd nicht");
                else if (!hit.NameParts.Any(part => part.IsHit)) Fail("Suche: Treffer im Namen werden nicht hervorgehoben");
                vm.SearchText = "typ:cmd";
                if (vm.SearchChips.Count != 1 || vm.SearchChips[0].IsInvalid) Fail("Suche: Filter-Chip für typ:cmd fehlt");
                if (!vm.AllItems.Any() || vm.AllItems.Any(i => i.Item.Type != PowerExec.Core.Models.ItemType.Batch)) Fail("Suche: typ:cmd filtert nicht nach Batch");
                await Settle();
                if (host.Window.Content is FrameworkElement searchRoot) Screenshot(searchRoot, "panel-suche-operator");
                vm.SearchChips[0].RemoveCommand.Execute(null);
                if (vm.SearchText.Length != 0) Fail("Suche: ✕ am Chip entfernt den Filter nicht");
                host.Window.HidePanel();
                await Settle();
            }

            Step("Tastatur: Schnellstart und Übersicht");
            {
                var host = _hosts[0];
                var vm = host.ViewModel;
                vm.Rebuild();
                if (!vm.AllItems.Any(i => i.QuickKeyText == "Alt+1")) Fail("Tastatur: Favorit ohne Schnellstart-Taste Alt+1");
                if (vm.RunQuickItem(0) || vm.RunQuickItem(10)) Fail("Tastatur: ungültige Schnellstart-Nummer wird angenommen");
                host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                vm.ShowShortcuts = true;
                await Settle();
                if (host.Window.Content is FrameworkElement keysRoot) Screenshot(keysRoot, "panel-tastenkuerzel");
                vm.ShowShortcuts = false;

                // Leertaste: Vorschau eines Scripts
                var psItem = vm.VisibleItems.FirstOrDefault(i => i.Item.IsPowerShell && !i.IsSingleClick);
                if (psItem == null) Fail("Vorschau: kein PowerShell-Script im Panel");
                else
                {
                    vm.Select(psItem);
                    if (!vm.TogglePeek() || vm.Peek == null) Fail("Vorschau: öffnet nicht");
                    else if (!vm.Peek.HasCode) Fail("Vorschau: kein Code für ein PowerShell-Script");
                    await Settle();
                    if (host.Window.Content is FrameworkElement peekRoot) Screenshot(peekRoot, "panel-vorschau");
                    vm.TogglePeek();
                    if (vm.ShowPeek) Fail("Vorschau: zweite Leertaste schließt nicht");
                }

                // Strg+K: Gruppe suchen und anspringen
                vm.ShowGroupJump = true;
                vm.GroupJumpText = "ad";
                await Settle();
                if (host.Window.Content is FrameworkElement jumpRoot) Screenshot(jumpRoot, "panel-gruppe-springen");
                var target = vm.GroupJumpSelected?.Group;
                if (target?.GroupPath != "Admin/AD") Fail($"Strg+K: „ad“ findet nicht die Gruppe Admin/AD, sondern {target?.GroupPath ?? "nichts"}");
                if (target != null && target.Breadcrumb != "Admin › AD") Fail("Gruppenpfad wird nicht als „Admin › AD“ angezeigt");
                vm.ConfirmGroupJump();
                await Settle();
                if (vm.ShowGroupJump) Fail("Strg+K: Auswahl schließt die Gruppensuche nicht");
                host.Window.HidePanel();
                await Settle();
            }

            Step("Aktivitäten-Leiste");
            {
                // Echter Prozess: Ende und Exit-Code werden erkannt
                var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit 3") { CreateNoWindow = true, UseShellExecute = false });
                var batch = new PowerExec.Core.Models.LaunchItem { FullPath = Path.Combine(dir, "Cleanup.cmd"), RootFolder = dir, Type = PowerExec.Core.Models.ItemType.Batch };
                if (proc == null) Fail("Aktivitäten: Testprozess startet nicht");
                else
                {
                    Activities.TrackProcess(batch, "Exit-Test", proc, "lokal");
                    var tracked = Activities.Items[0];
                    for (int wait = 0; wait < 100 && tracked.IsRunning; wait++) await Task.Delay(100);
                    if (tracked.IsRunning) Fail("Aktivitäten: Prozessende wird nicht erkannt");
                    else if (!tracked.IsError || !tracked.Detail.Contains('3')) Fail($"Aktivitäten: Exit-Code 3 nicht erkannt ({tracked.State}, {tracked.Detail})");
                }
                var remoteDemo = Activities.Add(new ActivityViewModel("Get-DiskSpace", "28 / 40 Rechner · 26 ✓ · 2 Fehler", "PS", open: () => { }, cancel: () => { }));
                remoteDemo.Progress = 0.7;
                var host = _hosts[0];
                if (!host.ViewModel.HasActivities) Fail("Aktivitäten: Panel zeigt keine Aktivitäten");
                host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                await Settle();
                if (host.Window.Content is FrameworkElement actRoot) Screenshot(actRoot, "panel-aktivitaeten");
                remoteDemo.Complete(ActivityState.Warning);
                foreach (var a in Activities.Items.ToList()) Activities.Remove(a);
                if (host.ViewModel.HasActivities) Fail("Aktivitäten: Liste wird nach dem Entfernen nicht leer");
                host.Window.HidePanel();
                await Settle();
            }

            Step("Darstellung: Windows-Akzentfarbe und Größe");
            {
                Settings.UseWindowsAccent = true;
                ThemeService.Apply(Settings);
                if (!ThemeService.Overrides.ContainsKey("Accent")) Fail("Akzentfarbe: Windows-Akzent wird nicht übernommen");
                Settings.UseWindowsAccent = false;
                ThemeService.Apply(Settings);
                if (ThemeService.Overrides.ContainsKey("Accent")) Fail("Akzentfarbe: bleibt nach dem Ausschalten aktiv");
                Settings.UiScalePercent = 120;
                await ShowAndClose(CatalogWindow.ForTest());
                Settings.UiScalePercent = 100;
            }

            Step("Einführung und Neuerungen");
            {
                var welcomeVm = new WelcomeViewModel(this);
                var welcome = new WelcomeWindow(welcomeVm);
                welcome.Show();
                for (int step = 0; step < WelcomeViewModel.StepCount; step++)
                {
                    welcomeVm.Step = step;
                    await Settle();
                    if (welcome.Content is FrameworkElement welcomeRoot) Screenshot(welcomeRoot, $"einfuehrung-{step + 1}");
                }
                if (!welcomeVm.IsLast || welcomeVm.NextLabel != "Fertig") Fail("Einführung: letzter Schritt zeigt nicht „Fertig“");
                welcome.Close();

                var host = _hosts[0];
                ShowWhatsNewForTest(PowerExec.Core.Updates.ReleaseNotes.All[0]);
                if (!host.ViewModel.WhatsNewVisible) Fail("Neuerungen: Karte erscheint nicht");
                host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                await Settle();
                if (host.Window.Content is FrameworkElement newsRoot) Screenshot(newsRoot, "panel-neuerungen");
                DismissWhatsNew();
                if (host.ViewModel.WhatsNewVisible) Fail("Neuerungen: Karte lässt sich nicht schließen");
                host.Window.HidePanel();
                await Settle();
            }

            Step("Mini-Dock");
            {
                var host = _hosts[0];
                host.Config.ShowDock = true;
                host.CreateTriggers(this);
                host.ViewModel.Rebuild();
                await Settle();
                var dock = host.Docks.FirstOrDefault();
                if (dock == null) Fail("Mini-Dock: wird trotz Einstellung nicht angezeigt");
                else
                {
                    if (dock.SlotCount != host.ViewModel.QuickItems.Count + 1) Fail($"Mini-Dock: {dock.SlotCount} Plätze statt {host.ViewModel.QuickItems.Count + 1}");
                    if (dock.Content is FrameworkElement dockRoot) Screenshot(dockRoot, "mini-dock");
                    host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                    await Settle();
                    if (dock.IsVisible) Fail("Mini-Dock: bleibt sichtbar, während das Panel offen ist");
                    host.Window.HidePanel();
                    await Settle();
                }
                host.Config.ShowDock = false;
                host.CreateTriggers(this);
                if (host.Docks.Count > 0) Fail("Mini-Dock: lässt sich nicht ausschalten");
            }

            Step("Verlauf");
            {
                var host = _hosts[0];
                var histScript = host.ViewModel.VisibleItems.FirstOrDefault(i => i.Item.IsPowerShell)?.Item;
                if (histScript == null) Fail("Verlauf: kein Script zum Eintragen");
                else
                {
                    var ok = BeginHistory(histScript, null, ExecutionMode.CurrentUser, PsEngine.WindowsPowerShell, null, new Dictionary<string, string?> { ["Pfad"] = "C:\\" });
                    CompleteHistory(ok, PowerExec.Core.Execution.HistoryResult.Success, "beendet · Exit-Code 0", 0);
                    var remote = BeginHistory(histScript, null, ExecutionMode.Administrator, PsEngine.WindowsPowerShell, new[] { "SRV-01", "SRV-02", "SRV-03" }, null);
                    CompleteHistory(remote, PowerExec.Core.Execution.HistoryResult.Warning, "3 Rechner · 2 ✓ · 1 Fehler", null, new[] { "SRV-02" });
                    host.ViewModel.ShowHistory = true;
                    if (host.ViewModel.HistoryRows.Count < 2) Fail("Verlauf: Einträge erscheinen nicht");
                    else if (!host.ViewModel.HistoryRows[0].CanRetryFailed) Fail("Verlauf: „Nur fehlgeschlagene“ fehlt beim Remote-Lauf");
                    host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                    await Settle();
                    if (host.Window.Content is FrameworkElement histRoot) Screenshot(histRoot, "panel-verlauf");
                    host.ViewModel.HistoryFilter = "errors";
                    if (host.ViewModel.HistoryRows.Count != 1) Fail("Verlauf: Filter „Fehler“ greift nicht");
                    host.ViewModel.HistoryFilter = "all";
                    host.ViewModel.ShowHistory = false;
                    host.Window.HidePanel();
                    await Settle();
                    SaveHistory();
                    if (!File.Exists(HistoryPath)) Fail("Verlauf: wird nicht gespeichert");
                }
            }

            Step("Spotlight-Modus");
            {
                var host = _hosts[0];
                host.Config.HotkeyOpensSpotlight = true;
                SpotlightStaysOpen = true;
                ShowPanelFromHotkey(host);
                await Settle();
                if (Spotlight == null) Fail("Spotlight: Tastenkürzel öffnet kein Suchfenster");
                else
                {
                    if (host.Window.IsOpen) Fail("Spotlight: Panel öffnet zusätzlich");
                    if (Spotlight.ViewModel.IsEmpty) Fail("Spotlight: ohne Suchtext keine Favoriten");
                    Spotlight.ViewModel.SearchText = "clnup";
                    await Settle();
                    if (Spotlight.ViewModel.Selected?.Item.FileName != "Cleanup.cmd") Fail("Spotlight: „clnup“ findet Cleanup.cmd nicht als besten Treffer");
                    if (Spotlight.Content is FrameworkElement spotRoot) Screenshot(spotRoot, "spotlight");
                    ShowPanelFromHotkey(host); // zweites Mal schließt
                    await Settle();
                    if (Spotlight != null) Fail("Spotlight: Tastenkürzel schließt das Suchfenster nicht");
                }
                host.Config.HotkeyOpensSpotlight = false;
                SpotlightStaysOpen = false;
            }

            Step("Parameter im Panel und Parameter-Sets");
            {
                var host = _hosts[0];
                var withParams = host.ViewModel.VisibleItems.FirstOrDefault(i => i.Item.IsPowerShell && i.Item.HasParameters && !i.IsSingleClick);
                if (withParams == null) Fail("Parameter im Panel: kein Script mit Parametern");
                else
                {
                    host.ViewModel.Select(withParams);
                    if (withParams.Inline == null || withParams.Inline.Fields.Count != withParams.Item.Parameters.Count) Fail("Parameter im Panel: Formular fehlt");
                    host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                    await Settle();
                    if (host.Window.Content is FrameworkElement formRoot) Screenshot(formRoot, "panel-parameter");
                    var before = PowerExec.Core.Profiles.ProfileResolver.GetProfiles(Settings, withParams.Item).Count;
                    AskForTest = () => "Selbsttest-Set";
                    foreach (var f in withParams.Inline!.Fields.Where(f => f.IsText && f.Text.Length == 0)) f.Text = "x";
                    withParams.Inline.SaveSetCommand.Execute(null);
                    AskForTest = null;
                    var after = PowerExec.Core.Profiles.ProfileResolver.GetProfiles(Settings, withParams.Item);
                    if (!after.Any(p => p.Name == "Selbsttest-Set")) Fail($"Parameter-Set: wird nicht als Startprofil gespeichert ({before} → {after.Count})");
                    host.Window.HidePanel();
                    await Settle();
                }
            }

            Step("Mehrfachauswahl und Abläufe");
            {
                var host = _hosts[0];
                var pick = host.ViewModel.VisibleItems.Where(i => !i.IsCustom).Take(2).ToList();
                if (pick.Count < 2) Fail("Abläufe: zu wenige Einträge für die Mehrfachauswahl");
                else
                {
                    foreach (var p in pick) host.ViewModel.ToggleChecked(p);
                    if (!host.ViewModel.HasMultiSelection || host.ViewModel.CheckedItems.Count != 2) Fail("Mehrfachauswahl: Strg+Klick wählt nicht aus");
                    host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                    await Settle();
                    if (host.Window.Content is FrameworkElement multiRoot) Screenshot(multiRoot, "panel-mehrfachauswahl");
                    AskForTest = () => "Selbsttest-Ablauf";
                    host.ViewModel.SaveCheckedAsWorkflowCommand.Execute(null);
                    AskForTest = null;
                    var wf = Settings.CustomEntries.FirstOrDefault(c => c.Kind == CustomEntryKind.Workflow && c.Name == "Selbsttest-Ablauf");
                    if (wf == null || wf.Steps.Count != 2) Fail("Ablauf: wird nicht mit zwei Schritten gespeichert");
                    else
                    {
                        if (host.ViewModel.HasMultiSelection) Fail("Mehrfachauswahl: bleibt nach dem Speichern bestehen");
                        if (!host.ViewModel.AllItems.Any(i => i.Item.FullPath == wf.Key)) Fail("Ablauf: erscheint nicht im Panel");
                        await ShowAndClose(new WorkflowWindow(new WorkflowEditorViewModel(this, wf)));
                    }
                    host.Window.HidePanel();
                    await Settle();
                }
            }

            Step("Zeitpläne");
            {
                var host = _hosts[0];
                var planned = host.ViewModel.VisibleItems.FirstOrDefault(i => i.Item.IsPowerShell && !i.IsCustom);
                if (planned == null) Fail("Zeitplan: kein Script");
                else
                {
                    var scheduleVm = new ScheduleViewModel(this, planned.Item);
                    if (scheduleVm.Error != null) Fail("Zeitplan: Standardwerte ungültig – " + scheduleVm.Error);
                    await ShowAndClose(new ScheduleWindow(scheduleVm));
                    var sch = new PowerExec.Core.Config.Schedule { Path = planned.Item.FullPath, Name = planned.DisplayName, Kind = PowerExec.Core.Config.ScheduleKind.Daily, TimeOfDay = new TimeSpan(23, 59, 0) };
                    SaveSchedule(sch);
                    if (sch.NextRunLocal == null) Fail("Zeitplan: kein nächster Termin berechnet");
                    var again = host.ViewModel.AllItems.FirstOrDefault(i => i.Item.FullPath == planned.Item.FullPath);
                    if (again == null || !again.HasSchedule || again.ScheduleText != "tägl. 23:59") Fail($"Zeitplan: Chip am Eintrag fehlt ({again?.ScheduleText})");
                    host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                    await Settle();
                    if (host.Window.Content is FrameworkElement schedRoot) Screenshot(schedRoot, "panel-zeitplan");
                    host.Window.HidePanel();
                    await Settle();
                    RemoveSchedule(sch);
                    if (Settings.Schedules.Count != 0) Fail("Zeitplan: lässt sich nicht entfernen");
                }
            }

            Step("Rückgängig");
            {
                var cleanupPath = Path.Combine(dir, "Cleanup.cmd");
                var cleanup = FindItem(cleanupPath);
                if (cleanup == null) Fail("Rückgängig: Cleanup.cmd nicht gefunden");
                else
                {
                    ToggleFavorite(cleanup); // ist Favorit → wird entfernt
                    if (IsFavorite(cleanupPath) || UndoToast == null) Fail("Rückgängig: kein Hinweis nach dem Entfernen des Favoriten");
                    if (_hosts[0].Window.Content is FrameworkElement undoRoot) { await Settle(); Screenshot(undoRoot, "panel-rueckgaengig"); }
                    Undo();
                    if (!IsFavorite(cleanupPath)) Fail("Rückgängig: Favorit wurde nicht wiederhergestellt");
                    if (UndoToast != null) Fail("Rückgängig: Hinweis bleibt nach dem Zurücknehmen stehen");
                }
            }

            Step("Gruppenfarbe und Darstellung je Gruppe");
            {
                var ad = _hosts[0].ViewModel.AllGroups.FirstOrDefault(g => g.GroupPath == "Admin/AD");
                if (ad == null || !ad.HasColor || ad.ColorHex != "#22C55E") Fail("Gruppenfarbe wird nicht an die Untergruppe vererbt");
                if (ad == null || !ad.IsGrid) Fail("Darstellung je Gruppe (Raster) wird nicht übernommen");
                var admin = _hosts[0].ViewModel.AllGroups.FirstOrDefault(g => g.GroupPath == "Admin");
                if (admin != null && _hosts[0].ViewModel.IsGrid == false && admin.IsGrid) Fail("Gruppe ohne eigene Darstellung folgt nicht dem Panel");
            }

            Step("Einstellungen");
            Step("Textfeld: Cursor");
            await CheckCaretAlignment();

            // Einstellungen: alle Bereiche
            var settingsVm = new SettingsViewModel(this, null);
            var settingsWindow = new SettingsWindow(settingsVm, this);
            settingsWindow.Show();
            await Settle();

            // Live-Übernahme: allgemeine Einstellung ohne Neuaufbau der Panels, Panel-Einstellung mit
            Step("Live-Übernahme");
            var hostBefore = _hosts[0];
            settingsVm.RecentRunsCount = 7;
            settingsWindow.ApplyNowForTest();
            if (Settings.RecentRunsCount != 7) Fail("Live-Übernahme: allgemeine Einstellung nicht übernommen");
            if (!ReferenceEquals(hostBefore, _hosts[0])) Fail("Live-Übernahme: Panels unnötig neu erzeugt");
            settingsVm.Panel.PanelWidth = 512;
            settingsWindow.ApplyNowForTest();
            if (Settings.Panels[0].PanelWidth != 512 || _hosts[0].Config.PanelWidth != 512) Fail("Live-Übernahme: Panelbreite nicht übernommen");

            // Änderung im Panel (Eintrag in Gruppe ziehen) → Einstellungsfenster zeigt den neuen Stand
            Step("Änderung im Panel");
            var entryKey = Settings.CustomEntries.First(c => c.Name == "Intranet").Key;
            var leer = _hosts[0].ViewModel.AllGroups.First(g => g.GroupPath == "Leer");
            _hosts[0].ViewModel.MoveKeyToGroup(entryKey, leer);
            await Settle();
            if (GroupManager.GroupOf(Settings, entryKey) != "Leer") Fail("Ablegen in Gruppe: Zuordnung fehlt");
            var reloaded = settingsWindow.CurrentViewModel;
            if (ReferenceEquals(reloaded, settingsVm)) Fail("Einstellungsfenster hat Änderung aus dem Panel nicht übernommen");
            else if (reloaded.EntryList.Rows.First(r => r.Entry.Key == entryKey).Group != "Leer") Fail("Einstellungsliste zeigt alte Gruppe");
            settingsVm = reloaded;
            settingsVm.SearchText = "update";
            if (!settingsVm.HasSearchResults) Fail("Suche in den Einstellungen findet nichts für „update“");
            settingsVm.SearchText = string.Empty;

            // Gruppen-Seite: smarte Gruppen haben ihren Ordner; Arbeitsbereiche bieten alle Gruppen zur Auswahl
            Step("Gruppen und Arbeitsbereiche");
            var smart = settingsVm.Categories.FirstOrDefault(c => c.IsSmart);
            if (smart == null) Fail("Gruppen-Seite: keine smarte Gruppe (Ordner) gefunden");
            else smart.IsExpanded = true;
            if (settingsVm.Categories.Count(c => c.IsSmart) != Settings.Folders.Count) Fail("Gruppen-Seite: nicht jeder Ordner hat eine smarte Gruppe");
            settingsVm.Section = "workspaces";
            await Settle();
            var wsRow = settingsVm.Workspaces.FirstOrDefault(w => w.IsActive);
            if (wsRow == null || wsRow.Groups.Count != settingsVm.Categories.Count) Fail("Arbeitsbereiche: Gruppenauswahl unvollständig");
            else
            {
                wsRow.IsExpanded = true;
                var choice = wsRow.Groups.First(g => g.Path == "Leer");
                choice.IsVisible = false;
                settingsWindow.ApplyNowForTest();
                await Settle();
                if (WorkspaceManager.ShowsGroup(Settings, "Leer")) Fail("Arbeitsbereich: Gruppe ausblenden nicht übernommen");
                if (_hosts[0].ViewModel.AllGroups.Any(g => g.GroupPath == "Leer")) Fail("Arbeitsbereich: ausgeblendete Gruppe weiter im Panel");
                settingsVm = settingsWindow.CurrentViewModel;
                settingsVm.Section = "workspaces";
                await Settle();
                settingsVm.Workspaces.First(w => w.IsActive).Groups.First(g => g.Path == "Leer").IsVisible = true;
                settingsWindow.ApplyNowForTest();
                await Settle();
                if (!WorkspaceManager.ShowsGroup(Settings, "Leer")) Fail("Arbeitsbereich: Gruppe wieder einblenden nicht übernommen");
                settingsVm = settingsWindow.CurrentViewModel;
                if (settingsVm.Workspaces.FirstOrDefault(w => w.IsActive) is { } r2) r2.IsExpanded = true;
                if (settingsVm.Categories.FirstOrDefault(c => c.IsSmart) is { } sm2) sm2.IsExpanded = true;
            }
            settingsVm.FocusGroup(Settings.Folders[0].Group);
            if (settingsVm.Section != "groups") Fail("Einstellungen: Gruppe fokussieren wechselt nicht zur Gruppen-Seite");

            foreach (var section in new[] { "general", "groups", "custom", "tags", "workspaces", "look", "behavior", "exec", "remote", "machines", "diag" })
            {
                settingsVm.Section = section;
                Step("Einstellungen " + section);
                await Settle();
                if (settingsWindow.Content is FrameworkElement root) Screenshot(root, "settings-" + section);
                await Settle();
            }
            settingsWindow.Close();

            // Dialoge
            var item = Repository.Items.First(i => i.FullPath.Equals(script, StringComparison.OrdinalIgnoreCase));
            await ShowAndClose(new ProfilesWindow(new ProfilesViewModel(this, item, Settings, saveImmediately: false), this));
            await ShowAndClose(new RunWindow(new RunViewModel(this, item, ExecutionMode.CurrentUser, remote: false)));
            await ShowAndClose(new RunWindow(new RunViewModel(this, item, ExecutionMode.CurrentUser, remote: true)));
            var request = new ExecutionRequest { Item = item, Targets = ["WEB1", "WEB2"] };
            await ShowAndClose(new ResultsWindow(new ResultsViewModel(this, request, "TEST\\user")));
            // Kachelkarte: ab 16 Rechnern automatisch, Probleme zuerst
            {
                var many = new ExecutionRequest { Item = item, Targets = Enumerable.Range(1, 24).Select(k => $"SRV-{k:00}").ToList() };
                var tilesVm = new ResultsViewModel(this, many, "TEST\\user");
                if (!tilesVm.ShowTiles) Fail("Ergebnisliste: ab 16 Rechnern startet sie nicht als Kachelkarte");
                for (int n = 0; n < tilesVm.Rows.Count; n++)
                {
                    var tileStatus = n % 8 == 3 ? RemoteStatus.Error : n % 8 == 5 ? RemoteStatus.Unreachable : n > 18 ? RemoteStatus.Running : RemoteStatus.Success;
                    tilesVm.Rows[n].Update(new RemoteResult { Computer = tilesVm.Rows[n].Computer, Status = tileStatus, DurationMs = 800 + n * 37, Output = "C: 42 % frei", Error = tileStatus == RemoteStatus.Error ? "Zugriff verweigert" : string.Empty });
                }
                tilesVm.TilesView.Refresh();
                var firstTile = tilesVm.TilesView.Cast<ResultRowViewModel>().First();
                if (firstTile.Status != RemoteStatus.Error) Fail("Kachelkarte: Fehler stehen nicht vorne");
                tilesVm.SelectedTile = firstTile;
                await ShowAndClose(new ResultsWindow(tilesVm));
            }
            await ShowAndClose(IconPickerWindow.ForTest());
            await ShowAndClose(CatalogWindow.ForTest());
            await ShowAndClose(PlaceholderWindow.ForTest(new[] { new Placeholder("Rechner", "SRV-01"), new Placeholder("Ticket", "") }));
            // Eintragsdialog: Kachelauswahl, jedes Formular, Live-Prüfung
            Step("Eintragsdialog");
            var picker = (ProfilesWindow)ProfilesWindow.ForTest(this, null);
            if (!picker.ViewModelForTest.ShowKindPicker) Fail("Eintragsdialog: ohne Art erscheint keine Kachelauswahl");
            picker.Show();
            await Settle();
            if (picker.Content is FrameworkElement pickerRoot) Screenshot(pickerRoot, "entry-auswahl");
            picker.ViewModelForTest.PickKindCommand.Execute("Rdp");
            await Settle();
            if (picker.ViewModelForTest.ShowKindPicker || picker.ViewModelForTest.Entry?.IsRdp != true) Fail("Eintragsdialog: Kachel wählt die Art nicht");
            if (picker.ViewModelForTest.SaveCommand.CanExecute(null)) Fail("Eintragsdialog: Speichern trotz fehlendem Rechner möglich");
            picker.ViewModelForTest.Entry!.Target = "SRV-01";
            if (!picker.ViewModelForTest.SaveCommand.CanExecute(null)) Fail("Eintragsdialog: Speichern trotz gültiger Eingabe gesperrt");
            picker.Close();
            var samples = new Dictionary<string, CustomEntry>
            {
                ["Url"] = new() { Kind = CustomEntryKind.Url, Target = "https://example.com", AppWindow = true },
                ["Folder"] = new() { Kind = CustomEntryKind.Folder, Target = Path.GetTempPath(), OpenWith = FolderOpener.Program },
                ["Rdp"] = new() { Kind = CustomEntryKind.Rdp, Target = "SRV-01", User = @"FIRMAdmin", Screen = RdpScreen.Window, ScreenWidth = 1600, ScreenHeight = 900 },
                ["Ssh"] = new() { Kind = CustomEntryKind.Ssh, Target = "srv", User = "root", JumpHost = "bastion", PortForward = "8080:localhost:80" },
                ["Snippet"] = new() { Kind = CustomEntryKind.Snippet, Code = "param([string]$Ziel = 'localhost', [switch]$Lang)\nTest-Connection $Ziel -Count 1" },
                ["TextSnippet"] = new() { Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text, Code = "Viele Grüße\n{{$Benutzer}}", CopyAsHtml = true },
                ["Tool"] = new() { Kind = CustomEntryKind.Tool, Target = "services.msc" },
                ["Command"] = new() { Kind = CustomEntryKind.Command, Target = "cmd.exe", WorkingDirectory = @"C:\" },
                ["File"] = new() { Kind = CustomEntryKind.File, Target = script }
            };
            foreach (var (key, sample) in samples)
            {
                var w = (ProfilesWindow)ProfilesWindow.ForTest(this, sample);
                w.Show();
                await Settle();
                await Task.Delay(800); // verzögerte Prüfungen (Syntax, Erreichbarkeit)
                await Settle();
                if (w.Content is FrameworkElement root) Screenshot(root, "entry-" + key);
                var vm = w.ViewModelForTest;
                if (key == "Folder" && vm.SaveCommand.CanExecute(null)) Fail("Ordner: „Öffnen mit Programm“ ohne Programm wird nicht bemängelt");
                if (key == "Snippet" && (!vm.HasParameters || vm.Entry!.HasSyntaxErrors)) Fail("PowerShell-Snippet: Parameter nicht erkannt oder falscher Syntaxfehler");
                if (key is "Url" or "Rdp" or "Ssh" or "TextSnippet" && vm.ShowRunAs) Fail($"{key}: „Ausführen als“ wird angezeigt, obwohl es nicht passt");
                if (key == "Url")
                {
                    // zweites Profil mit anderem Browser-Profil
                    vm.AddCommand.Execute(null);
                    var second = vm.Profiles.Last();
                    second.Options!.Private = true;
                    if (!second.Detail.Contains("privat")) Fail("Webseite: Kurzinfo des Profils zeigt die Startoptionen nicht");
                    if (second.ToProfile().Options?.Private != true) Fail("Webseite: Startoptionen des Profils werden nicht gespeichert");
                    await Settle();
                    if (w.Content is FrameworkElement r2) Screenshot(r2, "entry-Url-profile");
                }
                if (key is "Url" or "Rdp" or "Ssh" or "Folder" or "Command" && vm.Profiles.Any(p => p.Options == null || !p.HasOptions)) Fail($"{key}: Startprofil ohne eigene Startoptionen");
                w.Close();
            }

            Step("Snippets");
            // Eigene Einträge: Snippet-Datei, Platzhalter, Ein-Klick
            var snippet = Settings.CustomEntries.First(c => c.Name == "Ping");
            var snippetItem = DescribeCustom(snippet);
            if (!File.Exists(snippet.SnippetPath) || snippetItem.Parameters.Count != 1) Fail("PowerShell-Snippet: Datei oder param() nicht erkannt");
            if (!_hosts[0].ViewModel.AllItems.Any(i => i.IsSingleClick)) Fail("Ein-Klick-Eintrag fehlt im Panel");
            // Jeder eigene Eintrag muss im Hauptpanel erscheinen (Snippets haben kein Ziel!)
            _hosts[0].ViewModel.ResetForNextOpen();
            foreach (var c in Settings.CustomEntries)
                if (!_hosts[0].ViewModel.AllItems.Any(i => i.Item.FullPath.Equals(c.Key, StringComparison.OrdinalIgnoreCase)))
                    Fail($"Eigener Eintrag fehlt im Panel: {c.Name} ({c.Kind})");

            Step("Arbeitsbereich wechseln");
            // Arbeitsbereich wechseln
            SwitchWorkspace("Kunde A");
            await Settle();
        }
        catch (Exception ex)
        {
            _selfTestErrors.Add(ex.ToString());
        }
        finally
        {
            foreach (var h in _hosts) h.Dispose();
            _hosts.Clear();
        }
        lock (bindingErrors.Errors)
            foreach (var e in bindingErrors.Errors.Take(25)) Fail("Bindungsfehler: " + e);
        return _selfTestErrors.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, _selfTestErrors);
    }

    private void Fail(string message) => _selfTestErrors.Add(message);

    /// <summary>Bildschirmfoto eines Fensters (Inhalt) für die Sichtprüfung als CI-Artefakt.</summary>
    private void Screenshot(FrameworkElement element, string name)
    {
        if (_progressFile == null || element.ActualWidth < 1 || element.ActualHeight < 1) return;
        try
        {
            var dir = Path.Combine(Path.GetDirectoryName(_progressFile)!, "screens");
            Directory.CreateDirectory(dir);
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(element);
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(element.ActualWidth * dpi.DpiScaleX), (int)(element.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(element);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(dir, name + ".png"));
            enc.Save(fs);
        }
        catch (Exception ex)
        {
            Step("Bildschirmfoto fehlgeschlagen: " + ex.Message);
        }
    }

    /// <summary>Cursor und Platzhaltertext müssen im Textfeld an derselben Stelle beginnen (Innenabstand nur einmal).</summary>
    private async Task CheckCaretAlignment()
    {
        var box = new System.Windows.Controls.TextBox { Padding = new Thickness(30, 0, 8, 0), Tag = "Suchen", Width = 300 };
        var w = new Window { Content = box, Width = 360, Height = 120, Style = (Style)Application.Current.FindResource("AppWindow") };
        w.Show();
        await Settle();
        var caret = box.GetRectFromCharacterIndex(0).Left;
        var placeholder = box.Template.FindName("Placeholder", box) as FrameworkElement;
        var placeholderLeft = placeholder != null ? placeholder.TranslatePoint(new Point(placeholder.Margin.Left > 0 ? 0 : 0, 0), box).X + 2 : double.NaN;
        w.Close();
        if (caret > 45) Fail($"Textfeld: Cursor zu weit rechts ({caret:0} px bei 30 px Innenabstand)");
        if (caret < 25) Fail($"Textfeld: Innenabstand fehlt ({caret:0} px)");
        if (!double.IsNaN(placeholderLeft) && Math.Abs(caret - placeholderLeft) > 4)
            Fail($"Textfeld: Cursor ({caret:0} px) und Platzhalter ({placeholderLeft:0} px) beginnen nicht an derselben Stelle");
    }

    private static async Task Settle()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(450);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private async Task ShowAndClose(Window w)
    {
        Step("Fenster " + w.GetType().Name + " " + w.Title);
        w.Show();
        await Settle();
        if (w.Content is FrameworkElement root) Screenshot(root, "window-" + w.GetType().Name + "-" + (++_shotCounter));
        w.Close();
    }
}
