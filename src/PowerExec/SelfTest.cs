using System.Windows;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Core.Search;
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
public sealed partial class SelfTestController : AppController
{
    public SelfTestController() : base(app => new WpfAppUi(app)) { }

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
            Settings.MachineGroups.Add(new MachineGroup
            {
                Name = "Server-AD", Machines = ["SRV-DC1", "SRV-FS1"], LastSyncUtc = DateTime.UtcNow.AddHours(-1),
                AdQuery = new AdGroupQuery
                {
                    Blocks =
                    {
                        new AdRuleBlock { Rules = { new AdRule { Attribute = "name", Operator = AdOperator.StartsWith, Value = "SRV-" }, new AdRule { Attribute = AdRules.Enabled, Operator = AdOperator.Equals, Value = "Ja" } } },
                        new AdRuleBlock { Rules = { new AdRule { Attribute = "operatingSystem", Operator = AdOperator.Contains, Value = "Server" } } }
                    }
                }
            });
            Settings.Panels.Add(new PanelConfig { Name = "Hauptpanel", PanelOpacity = 90, Frost = 30 });
            WorkspaceManager.EnsureInitialized(Settings);
            SmartGroups.EnsureFolderGroups(Settings);
            Settings.DataVersion = 14;
            WorkspaceManager.Create(Settings, "Kunde A", copyCurrent: true);
            HistoryPath = Path.Combine(dir, "history.json");
            Views.WindowHelper.Settings = () => Settings;
            ThemeService.Apply(Settings);

            Health = new HealthService(UiDispatcher);
            Repository = new ItemRepository(Settings, UiDispatcher);
            Launcher = new Launcher(() => Settings, Dialogs, Ui.AskCredential);
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
                foreach (var host in WpfHosts)
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
                    if (!VisualDescendants(host.Window).OfType<Wpf.Ui.Controls.Button>().Any()) Fail($"Fluent: Panel {host.Config.Name} zeigt keine Fluent-Schaltflächen");
                    if (host.Window.SearchBox.Template?.FindName("Placeholder", host.Window.SearchBox) is not System.Windows.Controls.TextBlock { IsVisible: true })
                        Fail($"Fluent: Suchfeld im Panel {host.Config.Name} ohne Hinweistext");
                    if (host.Config.Name == "Hauptpanel" && !host.ViewModel.AllGroups.Any(g => g.GroupPath == "Admin/AD" && g.Items.Count == 1))
                        Fail($"{layout}: Untergruppe Admin/AD fehlt oder ist leer");
                    host.Window.HidePanel();
                    await Settle();
                }
            }

            Step("Suche: unscharf, Operatoren, Hervorhebung");
            {
                var host = WpfHost(0);
                var vm = host.ViewModel;
                host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                await Settle();
                vm.SearchText = "clnup";
                var hit = vm.AllItems.FirstOrDefault(i => i.Item.FileName == "Cleanup.cmd");
                if (hit == null) Fail("Unscharfe Suche: „clnup“ findet Cleanup.cmd nicht");
                else if (!hit.NameParts.Any(part => part.IsHit)) Fail("Suche: Treffer im Namen werden nicht hervorgehoben");

                // Weitertippen verwendet die ViewModels wieder (0.17) – die Hervorhebung muss trotzdem neu berechnet werden
                if (hit != null)
                {
                    if (!hit.IsSelected) vm.Select(hit); // Select schaltet um
                    vm.SearchText = "clean";
                    var again = vm.AllItems.FirstOrDefault(i => i.Key == hit.Key); // dieselbe Gruppe (nicht „Beste Treffer“)
                    if (again == null) Fail("Suche: „clean“ findet Cleanup.cmd nicht");
                    else
                    {
                        if (!ReferenceEquals(again, hit)) Fail("Suche: Eintrag wird beim Weitertippen neu erzeugt statt wiederverwendet");
                        var marked = string.Concat(again.NameParts.Where(part => part.IsHit).Select(part => part.Text));
                        if (!marked.Equals("Clean", StringComparison.OrdinalIgnoreCase)) Fail($"Suche: Hervorhebung nach dem Weitertippen falsch („{marked}“ statt „Clean“)");
                        if (!again.IsSelected || vm.SelectedItem != again) Fail("Suche: Auswahl geht beim Weitertippen verloren");
                    }
                    vm.SearchText = "zzzz-nichts";
                    vm.SearchText = "clean";
                    if (vm.AllItems.Count(i => i.IsSelected) > 1) Fail("Suche: mehrere Einträge gleichzeitig ausgewählt");
                    if (vm.AllItems.FirstOrDefault(i => i.Item.FileName == "Cleanup.cmd") is { IsSelected: true })
                        Fail("Suche: Auswahl kehrt zurück, nachdem der Eintrag nicht mehr gefunden wurde");
                }
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
                var host = WpfHost(0);
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

                // Geteiltes Kontextmenü: folgt dem jeweiligen Eintrag und vergisst sein Ziel beim Schließen
                var sharedMenu = host.Window.ItemMenu;
                foreach (var menuItem in vm.VisibleItems.Where(i => !i.IsSingleClick).Take(2).ToList())
                {
                    if (!host.Window.OpenItemMenu(menuItem)) { Fail($"Kontextmenü: öffnet für „{menuItem.DisplayName}“ nicht"); continue; }
                    await Settle();
                    if (!ReferenceEquals(sharedMenu.DataContext, menuItem))
                        Fail($"Kontextmenü: zeigt nicht „{menuItem.DisplayName}“, sondern {(sharedMenu.DataContext as ItemViewModel)?.DisplayName ?? "nichts"}");
                    sharedMenu.IsOpen = false;
                    await Settle();
                    if (sharedMenu.PlacementTarget != null) Fail("Kontextmenü: Ziel bleibt nach dem Schließen gesetzt");
                }
                host.Window.HidePanel();
                await Settle();
            }

            Step("Gruppen ziehen: Lücke im Panel");
            {
                var host = WpfHost(0);
                var vm = host.ViewModel;
                vm.SearchText = string.Empty;
                vm.Rebuild();
                host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                await Settle();
                var dragLeer = vm.AllGroups.FirstOrDefault(g => g.GroupPath == "Leer");
                var dragAdmin = vm.AllGroups.FirstOrDefault(g => g.GroupPath == "Admin");
                if (dragLeer == null || dragAdmin == null) Fail("Gruppen ziehen: Testgruppen „Leer“/„Admin“ fehlen im Panel");
                else
                {
                    vm.BeginGroupDrag(dragLeer);
                    if (!dragLeer.IsDragSource || !vm.IsDraggingGroup) Fail("Gruppen ziehen: gezogene Gruppe macht keinen Platz");
                    var into = vm.PreviewGroupDrop(dragAdmin, GroupDropZone.Into, 0);
                    if (into is not { IsValid: true, Position: GroupDropPosition.Into } || !dragAdmin.GapInside)
                        Fail("Gruppen ziehen: keine Lücke für „als Untergruppe“");
                    await Settle();
                    if (host.Window.Content is FrameworkElement intoRoot) Screenshot(intoRoot, "panel-gruppe-ziehen-untergruppe");
                    var before = vm.PreviewGroupDrop(dragAdmin, GroupDropZone.Before, 0);
                    if (before is not { IsValid: true } || !dragAdmin.GapBefore || dragAdmin.GapInside) Fail("Gruppen ziehen: Lücke wandert nicht mit");
                    await Settle();
                    if (host.Window.Content is FrameworkElement beforeRoot) Screenshot(beforeRoot, "panel-gruppe-ziehen-davor");
                    vm.PreviewGroupDrop(dragAdmin, GroupDropZone.Into, 0);
                    if (!vm.CommitGroupDrop()) Fail("Gruppen ziehen: Ablegen bewirkt nichts");
                    await Settle();
                    if (!GroupManager.Exists(Settings, "Admin/Leer")) Fail("Gruppen ziehen: „Leer“ ist nicht in „Admin“ gelandet");
                    if (vm.AllGroups.Any(g => g.IsDragSource || g.DropSlot != GroupDropSlot.None)) Fail("Gruppen ziehen: Lücke oder Quelle bleibt nach dem Ablegen");
                    if (!vm.TryUndo() || !GroupManager.Exists(Settings, "Leer")) Fail("Gruppen ziehen: Rückgängig stellt „Leer“ nicht wieder her");
                    await Settle();
                }
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
                // Fenster bleibt offen (cmd /k): Ende trotzdem über die Fertig-Markierung erkennen, danach „Erneut starten“
                {
                    var doneDir = Path.Combine(Path.GetTempPath(), "PowerExec-SelfTest-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(doneDir);
                    var doneCmd = Path.Combine(doneDir, "Fertig.cmd");
                    File.WriteAllText(doneCmd, "@echo off\r\nexit /b 4\r\n");
                    var doneItem = new PowerExec.Core.Models.LaunchItem { FullPath = doneCmd, RootFolder = doneDir, Type = PowerExec.Core.Models.ItemType.Batch };
                    var before = Activities.Items.Count;
                    System.Diagnostics.Process? cmdProc = null;
                    var started = Launcher.StartLocal(new ExecutionRequest { Item = doneItem, NoExit = true }, null,
                        p => { cmdProc = p; Activities.TrackProcess(doneItem, "Fertig-Test", p, "lokal", restart: () => { }); });
                    var act = Activities.Items.Count > before ? Activities.Items[0] : null;
                    if (!started || act == null) Fail("Aktivitäten: Batch mit offenem Fenster wird nicht verfolgt");
                    else
                    {
                        for (int wait = 0; wait < 100 && act.IsRunning; wait++) await Task.Delay(100);
                        if (act.IsRunning) Fail("Aktivitäten: Ende bei offenem Fenster nicht erkannt (bleibt „läuft“)");
                        else if (!act.IsError || !act.Detail.Contains("Fehler") || !act.WindowStillOpen) Fail($"Aktivitäten: Ergebnis bei offenem Fenster falsch ({act.State}, {act.Detail})");
                        if (!act.CanRestart) Fail("Aktivitäten: „Erneut starten“ fehlt nach dem Ende");
                    }
                    try { if (cmdProc is { HasExited: false }) cmdProc.Kill(); } catch { }
                    try { Directory.Delete(doneDir, true); } catch { }
                }
                var remoteDemo = Activities.Add(new ActivityViewModel("Get-DiskSpace", "28 / 40 Rechner · 26 ✓ · 2 Fehler", "PS", open: () => { }, cancel: () => { }));
                remoteDemo.Progress = 0.7;
                var host = WpfHost(0);
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

            Step("Hintergrund: Klarglas, Milchglas und Glas");
            {
                // Messung für den CI-Bericht: Fenster so groß wie das Panel? Ist hinter dem Panel weichgezeichnet?
                // Dafür liegt ein Streifenmuster hinter dem Panel; ohne Weichzeichner bleibt der Kontrast benachbarter Pixel hoch.
                var cfg = Settings.Panels[0];
                var (beforeBackdrop, beforeOpacity, beforeFrost, beforeWidth) = (cfg.Backdrop, cfg.PanelOpacity, cfg.Frost, cfg.PanelWidth);
                var transparency = ThemeService.TransparencyEffects();
                var report = new System.Text.StringBuilder();
                report.AppendLine("### Hintergrund hinter dem Panel");
                report.AppendLine();
                report.AppendLine($"Windows-Build {Environment.OSVersion.Version.Build} · Transparenzeffekte {(transparency ? "an" : "aus")} · Deckkraft 40 %, Breite 560");
                report.AppendLine();
                report.AppendLine("| Variante | Läuft als | Weichzeichner | Helligkeitsschicht (WPF) | Fenster (px) | Panelrahmen (px) | Kontrast hinter dem Panel (0–255) | Kontrast nach 1,5 s | Gleiten: Bilder · längstes Bild |");
                report.AppendLine("|---|---|---|---:|---|---|---:|---:|---|");
                cfg.PanelOpacity = 40;
                cfg.PanelWidth = 560;
                // Klarglas ohne und mit Milchglas; alte Varianten (Acrylic) laufen als Klarglas; Glas mit eigenem Weichzeichner
                foreach (var (b, frost, name) in new[] { (PanelBackdrop.Clear, 0, "klarglas"), (PanelBackdrop.Clear, 30, "milchglas"),
                             (PanelBackdrop.Acrylic, 60, "alt-acrylic"), (PanelBackdrop.Glass, 30, "glas") })
                {
                    cfg.Backdrop = b;
                    cfg.Frost = frost;
                    CreateHosts();
                    var host = WpfHost(0);
                    var expected = PanelBackdrops.Effective(b, Environment.OSVersion.Version.Build, ThemeService.IsHighContrast, transparency);
                    if (host.Window.EffectiveBackdrop != expected) Fail($"Hintergrund {b}: läuft als {host.Window.EffectiveBackdrop} statt {expected}");
                    if (expected == PanelBackdrop.Glass && !host.Window.GlassActive)
                        Fail($"Glas: Weichzeichner läuft nicht ({host.Window.GlassError ?? "ohne Fehlermeldung"})");
                    if (host.Window.GlassActive)
                    {
                        // Tönung muss der Panelfarbe folgen (samt Deckkraft)
                        var tint = host.Window.GlassTint;
                        var panelBrush = host.Window.TryFindResource("Bg.Panel") as System.Windows.Media.SolidColorBrush;
                        if (panelBrush == null || tint.R != panelBrush.Color.R || tint.G != panelBrush.Color.G || tint.B != panelBrush.Color.B)
                            Fail($"Glas: Tönung {tint} folgt nicht der Panelfarbe {panelBrush?.Color}");
                        if (Math.Abs(tint.A - 255 * 0.4) > 3) Fail($"Glas: Tönung mit Deckkraft {tint.A / 2.55:0} % statt 40 %");
                    }
                    // Glas: die Helligkeitsschicht liegt im Weichzeichner, nicht in WPF
                    var wantAlpha = ThemeService.IsHighContrast || host.Window.GlassActive ? 0 : PanelBackdrops.FrostBrightness(frost, host.ViewModel.IsDarkSurface);
                    if (Math.Abs(host.Window.FrostBrightnessAlpha - wantAlpha) > 0.01)
                        Fail($"Milchglas {frost}: Helligkeitsschicht {host.Window.FrostBrightnessAlpha:P0} statt {wantAlpha:P0}");
                    if (host.Window.AllowsTransparency == PanelBackdrops.NeedsSystemWindow(expected)) Fail($"Hintergrund {b}: falsche Fensterart");
                    // Körnung nur bei Glas
                    if (host.Window.HasGrain != host.Window.GlassActive)
                        Fail($"Hintergrund {b}: Körnung {(host.Window.HasGrain ? "sichtbar" : "fehlt")}");
                    var monitor = ScreenService.TriggerMonitors(host.Config)[0];
                    var rect = ScreenService.PanelRect(monitor, host.Config);
                    var stripes = StripeWindow(rect);
                    stripes.Show();
                    await Settle();
                    // Gleiten messen: Abstände zwischen gezeichneten Bildern während des Einfahrens
                    var frames = new List<double>();
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var last = 0.0;
                    EventHandler onFrame = (_, _) => { var t = clock.Elapsed.TotalMilliseconds; frames.Add(t - last); last = t; };
                    System.Windows.Media.CompositionTarget.Rendering += onFrame;
                    host.Window.ShowPanel(monitor, viaHotkey: true);
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(host.Window).Handle;
                    if (PanelBackdrops.NeedsSystemWindow(expected) && Settings.Animation != AnimationMode.Off)
                    {
                        // Weichzeichner-Varianten gleiten als Ganzes herein: schon während des Einfahrens volle Panelgröße
                        await Task.Delay(60);
                        Interop.NativeMethods.GetWindowRect(hwnd, out var mid);
                        if (Math.Abs(mid.Right - mid.Left - rect.Width) > 1 || Math.Abs(mid.Bottom - mid.Top - rect.Height) > 1)
                            Fail($"Hintergrund {b}: Fenster ändert beim Einfahren die Größe ({mid.Right - mid.Left}×{mid.Bottom - mid.Top} statt {rect.Width}×{rect.Height})");
                    }
                    await Settle();
                    System.Windows.Media.CompositionTarget.Rendering -= onFrame;
                    var slide = frames.Count > 1 ? $"{frames.Count} · {frames.Skip(1).Max():0} ms" : "–";
                    await Task.Delay(400);
                    Interop.NativeMethods.GetWindowRect(hwnd, out var wr);
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(host.Window);
                    var bw = (int)Math.Round(host.Window.PanelBorder.ActualWidth * dpi.DpiScaleX);
                    var bh = (int)Math.Round(host.Window.PanelBorder.ActualHeight * dpi.DpiScaleY);
                    var c1 = StripeContrast(wr.Left + (wr.Right - wr.Left) / 2 - 60, wr.Top + 200);
                    await Task.Delay(1500);
                    var c2 = StripeContrast(wr.Left + (wr.Right - wr.Left) / 2 - 60, wr.Top + 200);
                    var glassState = host.Window.GlassActive ? "aktiv" : host.Window.GlassError is { } ge ? "Fehler: " + ge.Replace("|", "/") : "–";
                    report.AppendLine($"| {name} (Milchglas {frost}) | {expected} | {glassState} | {host.Window.FrostBrightnessAlpha:P0} | {wr.Right - wr.Left}×{wr.Bottom - wr.Top} | {bw}×{bh} | {c1:0.0} | {c2:0.0} | {slide} |");
                    if (Math.Abs(wr.Right - wr.Left - bw) > 3 || Math.Abs(wr.Bottom - wr.Top - bh) > 3)
                        Fail($"Hintergrund {b}: Fenster ({wr.Right - wr.Left}×{wr.Bottom - wr.Top}) größer als das Panel ({bw}×{bh})");
                    if (host.Window.Content is FrameworkElement bgRoot) Screenshot(bgRoot, "panel-hintergrund-" + name);
                    // Bildschirmaufnahme: zeigt auch, was Windows hinter dem WPF-Inhalt zeichnet (Weichzeichner)
                    ScreenCapture(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top, "panel-bildschirm-" + name);
                    if (host.Window.GlassActive)
                    {
                        // Inhalt muss über dem Weichzeichner liegen: im durchsichtigen Kindfenster (Ebene über dem Composition-Baum)
                        if (!host.Window.ContentInChildWindow)
                            Fail("Glas: Panelinhalt liegt nicht im Kindfenster über dem Weichzeichner");
                        if (!host.Window.SearchBox.IsVisible || host.Window.SearchBox.ActualWidth < 10)
                            Fail("Glas: Suchfeld im Kindfenster nicht sichtbar");
                        // Leere Flächen müssen Klicks und Ablegen annehmen (Deckkraft 0 fiele im Kindfenster durch)
                        if (host.Window.PanelBorder.Background is not System.Windows.Media.SolidColorBrush { Color.A: > 0 })
                            Fail("Glas: Panelfläche ohne Deckkraft – Klicks auf leere Flächen fielen durch");
                        // Bindungen an die Panel-Wurzel (früher ans Fenster) müssen auch im Kindfenster greifen
                        if (host.Window.PeekCard.IsVisible != host.ViewModel.ShowPeek)
                            Fail("Glas: Vorschau-Karte sichtbar, obwohl keine Vorschau offen ist (Bindung an die Panel-Wurzel greift nicht)");
                        // Inaktiv: ein anderes Fenster bekommt den Fokus, der Weichzeichner muss bleiben (angeheftet, damit es nicht einfährt)
                        var pinned = host.Config.Pinned;
                        host.Config.Pinned = true;
                        stripes.Activate();
                        await Settle();
                        host.Config.Pinned = pinned;
                        var c3 = StripeContrast(wr.Left + (wr.Right - wr.Left) / 2 - 60, wr.Top + 200);
                        ScreenCapture(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top, "panel-bildschirm-glas-inaktiv");
                        report.AppendLine($"| glas, Panel inaktiv | Glass | aktiv | – | – | – | {c3:0.0} | – | – |");
                    }
                    host.Window.HidePanel();
                    stripes.Close();
                    await Settle();
                }
                report.AppendLine();
                report.AppendLine("Kontrast: mittlerer Helligkeitsunterschied benachbarter Pixel über einem Schwarz-Weiß-Streifenmuster hinter dem Panel. Hoch = nicht weichgezeichnet, niedrig = weichgezeichnet.");
                var reportDir = _progressFile != null ? Path.GetDirectoryName(Path.GetFullPath(_progressFile))! : Path.GetTempPath();
                File.WriteAllText(Path.Combine(reportDir, "selftest-backdrop.md"), report.ToString(), new System.Text.UTF8Encoding(false));
                (cfg.Backdrop, cfg.PanelOpacity, cfg.Frost, cfg.PanelWidth) = (beforeBackdrop, beforeOpacity, beforeFrost, beforeWidth);
                CreateHosts();
            }

            Step("Angeheftet: Klick auf die Hover-Zone fährt ein und aus");
            {
                var host = WpfHost(0);
                var pinned = host.Config.Pinned;
                host.Config.Pinned = true;
                var monitor = ScreenService.TriggerMonitors(host.Config)[0];
                host.ShowFromTrigger(monitor);
                await Settle();
                host.ToggleFromTrigger(monitor); // direkt nach dem Ausfahren: gehört noch zum Öffnen
                await Settle();
                if (!host.Window.IsOpen || host.Window.IsHiding) Fail("Angeheftet: Klick direkt nach dem Ausfahren hat das Panel wieder eingefahren");
                await Task.Delay(700);
                host.ToggleFromTrigger(monitor);
                await Settle();
                if (host.Window.IsOpen) Fail("Angeheftet: Klick auf die Hover-Zone fährt das Panel nicht ein");
                host.ToggleFromTrigger(monitor);
                await Settle();
                if (!host.Window.IsOpen) Fail("Angeheftet: Klick auf die Hover-Zone fährt das Panel nicht aus");
                host.Config.Pinned = pinned;
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

                var host = WpfHost(0);
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
                var host = WpfHost(0);
                host.Config.ShowDock = true;
                host.CreateTriggers(this);
                host.ViewModel.Rebuild();
                await Settle();
                var dock = host.Docks.FirstOrDefault();
                if (dock == null) Fail("Mini-Dock: wird trotz Einstellung nicht angezeigt");
                else
                {
                    if (dock.SlotCount != host.ViewModel.DockItems.Count + 1) Fail($"Mini-Dock: {dock.SlotCount} Plätze statt {host.ViewModel.DockItems.Count + 1}");
                    if (dock.Content is FrameworkElement dockRoot) Screenshot(dockRoot, "mini-dock");
                    if (dock.TryFindResource("DefaultUiButtonStyle") is null)
                        Fail("Fluent: Mini-Dock ohne Fluent-Stile");
                    if (host.Triggers.Count > 0) Fail("Mini-Dock: Hover-Zone bleibt zusätzlich aktiv");
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
                var host = WpfHost(0);
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
                var host = WpfHost(0);
                host.Config.HotkeyOpensSpotlight = true;
                SpotlightStaysOpen = true;
                ShowPanelFromHotkey(host.Panel);
                await Settle();
                var spotlight = (Spotlight as WpfAppUi.WindowHandle)?.Window as SpotlightWindow;
                if (spotlight == null) Fail("Spotlight: Tastenkürzel öffnet kein Suchfenster");
                else
                {
                    if (host.Window.IsOpen) Fail("Spotlight: Panel öffnet zusätzlich");
                    if (spotlight.ViewModel.IsEmpty) Fail("Spotlight: ohne Suchtext keine Favoriten");
                    if (spotlight.SearchBox.Template?.FindName("Placeholder", spotlight.SearchBox) is not System.Windows.Controls.TextBlock { IsVisible: true })
                        Fail("Fluent: Spotlight-Suchfeld ohne Hinweistext");
                    spotlight.ViewModel.SearchText = "clnup";
                    await Settle();
                    if (spotlight.ViewModel.Selected?.Item.FileName != "Cleanup.cmd") Fail("Spotlight: „clnup“ findet Cleanup.cmd nicht als besten Treffer");
                    if (spotlight.Content is FrameworkElement spotRoot) Screenshot(spotRoot, "spotlight");
                    ShowPanelFromHotkey(host.Panel); // zweites Mal schließt
                    await Settle();
                    if (Spotlight != null) Fail("Spotlight: Tastenkürzel schließt das Suchfenster nicht");
                }
                host.Config.HotkeyOpensSpotlight = false;
                SpotlightStaysOpen = false;
            }

            Step("Parameter im Panel und Parameter-Sets");
            {
                var host = WpfHost(0);
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
                var host = WpfHost(0);
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
                var host = WpfHost(0);
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

            Step("Team-Katalog");
            {
                var host = WpfHost(0);
                var publisher = new AppSettings();
                publisher.CustomEntries.Add(new CustomEntry { Id = "kat1", Kind = CustomEntryKind.Url, Name = "Team-Wiki", Target = "https://wiki.example.com" });
                var catalogPath = Path.Combine(dir, "team.pxentries.json");
                File.WriteAllText(catalogPath, EntryShare.Export(publisher, publisher.CustomEntries));
                var error = await SubscribeCatalogAsync(catalogPath, "IT-Team");
                if (error != null) Fail("Team-Katalog: Abonnieren fehlgeschlagen – " + error);
                var sub = Settings.Catalogs.FirstOrDefault();
                if (sub == null || TeamCatalogs.EntriesOf(Settings, sub).Count() != 1) Fail("Team-Katalog: Eintrag wird nicht übernommen");
                else
                {
                    var catItem = host.ViewModel.AllItems.FirstOrDefault(i => i.IsFromCatalog);
                    if (catItem == null) Fail("Team-Katalog: Eintrag erscheint nicht im Panel");
                    else if (catItem.RemoveCustomCommand.CanExecute(null)) Fail("Team-Katalog: Eintrag ist nicht schreibgeschützt");
                    sub.AutoApply = false;
                    publisher.CustomEntries.Add(new CustomEntry { Id = "kat2", Kind = CustomEntryKind.Folder, Name = "Ablage", Target = dir });
                    File.WriteAllText(catalogPath, EntryShare.Export(publisher, publisher.CustomEntries));
                    await CheckCatalogsAsync();
                    if (host.ViewModel.CatalogPending == null) Fail("Team-Katalog: Änderung wird nicht gemeldet");
                    host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                    await Settle();
                    if (host.Window.Content is FrameworkElement catRoot) Screenshot(catRoot, "panel-team-katalog");
                    await ApplyPendingCatalogsAsync();
                    if (TeamCatalogs.EntriesOf(Settings, sub).Count() != 2) Fail("Team-Katalog: „Übernehmen“ übernimmt nicht");
                    host.Window.HidePanel();
                    await Settle();
                    UnsubscribeCatalog(sub);
                    if (Settings.CustomEntries.Any(c => c.IsFromCatalog)) Fail("Team-Katalog: Abbestellen entfernt die Einträge nicht");
                }
            }

            Step("Suchvorschläge, Farbwähler, Sprungleiste");
            {
                var host = WpfHost(0);
                var vm = host.ViewModel;
                vm.UpdateSuggestions("typ:", 4);
                if (!vm.HasSuggestions) Fail("Suchvorschläge: „typ:“ schlägt nichts vor");
                else if (vm.Suggestions.Any(x => x.Count <= 0)) Fail("Suchvorschläge: Typ ohne Einträge wird angeboten");
                host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                await Settle();
                if (host.Window.Content is FrameworkElement sugRoot) Screenshot(sugRoot, "panel-suchvorschlaege");
                vm.CloseSuggestions();
                // Klick ins Suchfeld: erst die Filterarten, nach der Auswahl deren Werte
                vm.ShowFilterCategories(string.Empty, 0);
                if (!vm.HasSuggestions || !vm.Suggestions.All(x => x.IsCategory)) Fail("Filter: Klick ins Suchfeld bietet keine Filterarten an");
                else
                {
                    await Settle();
                    if (host.Window.Content is FrameworkElement catRoot) Screenshot(catRoot, "panel-filterarten");
                    var typ = vm.Suggestions.FirstOrDefault(x => x.Insert == "typ:");
                    if (typ == null) Fail("Filter: Filterart „Typ“ fehlt");
                    else
                    {
                        var (t, c) = Core.Search.SearchSuggestions.Apply(string.Empty, 0, typ);
                        vm.UpdateSuggestions(t, c);
                        if (!vm.HasSuggestions || vm.Suggestions.Any(x => x.IsCategory)) Fail("Filter: nach „Typ“ erscheinen keine Werte");
                        await Settle();
                        if (host.Window.Content is FrameworkElement valRoot) Screenshot(valRoot, "panel-filterwerte");
                    }
                }
                vm.CloseSuggestions();
                // Voreingestellter Filter: als Chip sichtbar und aufhebbar
                vm.Filter = TypeFilter.Scripts;
                if (!vm.SearchChips.Any(x => x.Label == "Scripte")) Fail("Filter: voreingestellter Filter erscheint nicht als Chip");
                else vm.SearchChips.First(x => x.Label == "Scripte").RemoveCommand.Execute(null);
                if (vm.Filter != TypeFilter.All) Fail("Filter: ✕ am Chip hebt den voreingestellten Filter nicht auf");
                host.Window.HidePanel();
                await Settle();
                var colors = vm.AllGroups.FirstOrDefault(g => g.GroupPath != null)?.ColorChoices;
                if (colors == null || colors.Where(c => c.SwatchColor != null).Select(c => c.SwatchColor!.Value.ToString()).Distinct().Count() < 2) Fail("Farbmenü: Farben ohne eigene Vorschau");
                if (colors != null && !colors.Any(c => c.Name.StartsWith("Eigene Farbe"))) Fail("Farbmenü: „Eigene Farbe …“ fehlt im Untermenü");
                var uncolored = vm.AllGroups.FirstOrDefault(g => g.GroupPath != null && g.OwnColorHex.Length == 0);
                if (uncolored != null && uncolored.ColorChoices.Any(c => c.Name == "Farbe entfernen")) Fail("Farbmenü: „Farbe entfernen“ ohne eigene Farbe");
                await ShowAndClose(ColorPickerWindow.ForTest());
                var railGroup = vm.Groups.FirstOrDefault(g => g.GroupPath != null);
                if (railGroup != null)
                {
                    railGroup.IsExpanded = false;
                    vm.RailClickCommand.Execute(railGroup);
                    if (!railGroup.IsExpanded) Fail("Sprungleiste: Klick klappt die Gruppe nicht auf");
                    vm.RailClickCommand.Execute(railGroup);
                    if (railGroup.IsExpanded) Fail("Sprungleiste: zweiter Klick klappt die Gruppe nicht zu");
                    railGroup.IsExpanded = true;
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
                    if (WpfHost(0).Window.Content is FrameworkElement undoRoot) { await Settle(); Screenshot(undoRoot, "panel-rueckgaengig"); }
                    Undo();
                    if (!IsFavorite(cleanupPath)) Fail("Rückgängig: Favorit wurde nicht wiederhergestellt");
                    if (UndoToast != null) Fail("Rückgängig: Hinweis bleibt nach dem Zurücknehmen stehen");
                }
            }

            Step("Feste Gruppen: Reihenfolge und Arbeitsbereich");
            {
                var vm0 = _hosts[0].ViewModel;
                vm0.Rebuild();
                if (!vm0.Groups.Any(g => g.Kind == "fav")) Fail("Feste Gruppen: Favoriten fehlen im Panel");
                var ws = WorkspaceManager.Active(Settings);
                WorkspaceManager.SetGroupHidden(ws, FixedGroups.Favorites, true);
                vm0.Rebuild();
                if (vm0.Groups.Any(g => g.Kind == "fav")) Fail("Feste Gruppen: im Arbeitsbereich ausgeblendete Favoriten erscheinen trotzdem");
                WorkspaceManager.SetGroupHidden(ws, FixedGroups.Favorites, false);
                // Favoriten hinter die erste eigene Gruppe
                var firstTop = vm0.Groups.First(g => g.GroupPath != null).GroupPath!;
                var tops = FixedGroups.Compose(Settings, vm0.Groups.Where(g => g.GroupPath != null).Select(g => g.GroupPath!).ToList());
                tops.Remove(FixedGroups.Favorites);
                tops.Insert(tops.IndexOf(firstTop) + 1, FixedGroups.Favorites);
                FixedGroups.SetPositions(Settings, tops);
                vm0.Rebuild();
                var kinds = vm0.Groups.Select(g => g.GroupPath ?? g.Kind).ToList();
                if (kinds.IndexOf("fav") != kinds.IndexOf(firstTop) + 1) Fail($"Feste Gruppen: Reihenfolge im Panel falsch ({string.Join(", ", kinds)})");
                Settings.FixedGroupPositions.Clear();
                vm0.Rebuild();
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
            if (wsRow == null || wsRow.Groups.Count(g => !g.IsFixed) != settingsVm.Categories.Count) Fail("Arbeitsbereiche: Gruppenauswahl unvollständig");
            if (wsRow != null && wsRow.Groups.Count(g => g.IsFixed) != FixedGroups.Keys.Count) Fail("Arbeitsbereiche: feste Gruppen fehlen in der Auswahl");
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
            Step("Einstellungen: Gruppen ziehen");
            {
                settingsVm.Section = "groups";
                await Settle();
                var cats = settingsVm.Categories;
                var leerCat = cats.FirstOrDefault(c => c.Name == "Leer");
                var adminCat = cats.FirstOrDefault(c => c.Name == "Admin");
                var adCat = cats.FirstOrDefault(c => c.Name == "Admin/AD");
                if (leerCat == null || adminCat == null || adCat == null) Fail("Einstellungen › Gruppen ziehen: Testgruppen fehlen");
                else
                {
                    // Verschachtelte Darstellung: „AD“ liegt in der Karte von „Admin“
                    if (!adminCat.Children.Contains(adCat) || settingsVm.CategoryRoots.Contains(adCat))
                        Fail("Einstellungen › Gruppen: Untergruppe liegt nicht im Rahmen der übergeordneten Gruppe");

                    settingsVm.BeginCategoryDrag(leerCat);
                    var ok = settingsVm.PreviewCategoryDrop(adminCat, GroupDropZone.Before, 0);
                    if (ok is not { IsValid: true } || !adminCat.GapBefore) Fail("Einstellungen › Gruppen ziehen: Lücke vor der Zielkarte fehlt");
                    var into = settingsVm.PreviewCategoryDrop(adminCat, GroupDropZone.Into, 0);
                    if (into is not { IsValid: true, Position: GroupDropPosition.Into } || !adminCat.GapInside || adminCat.GapBefore)
                        Fail("Einstellungen › Gruppen ziehen: „hinein“ öffnet keine Lücke in der Zielgruppe");
                    await Settle();
                    if (settingsWindow.Content is FrameworkElement gapRoot) Screenshot(gapRoot, "settings-gruppe-ziehen-hinein");
                    settingsVm.EndCategoryDrag();
                    if (cats.Any(c => c.IsDragSource || c.DropSlot != GroupDropSlot.None)) Fail("Einstellungen › Gruppen ziehen: Zustand bleibt nach dem Abbrechen");

                    // Verschachteln und zurück: „AD“ nach „Leer“ und wieder nach „Admin“
                    settingsVm.BeginCategoryDrag(adCat);
                    settingsVm.PreviewCategoryDrop(leerCat, GroupDropZone.Into, 0);
                    if (!settingsVm.CommitCategoryDrop() || adCat.Name != "Leer/AD" || !leerCat.Children.Contains(adCat))
                        Fail($"Einstellungen › Gruppen ziehen: Verschachteln klappt nicht („{adCat.Name}“)");
                    settingsVm.BeginCategoryDrag(adCat);
                    settingsVm.PreviewCategoryDrop(adminCat, GroupDropZone.Into, 0);
                    if (!settingsVm.CommitCategoryDrop() || adCat.Name != "Admin/AD" || !adminCat.Children.Contains(adCat))
                        Fail($"Einstellungen › Gruppen ziehen: Zurückschieben klappt nicht („{adCat.Name}“)");

                    settingsVm.BeginCategoryDrag(leerCat);
                    settingsVm.PreviewCategoryDrop(adminCat, GroupDropZone.Before, 0);
                    if (!settingsVm.CommitCategoryDrop()) Fail("Einstellungen › Gruppen ziehen: Ablegen bewirkt nichts");
                    else if (cats.IndexOf(leerCat) > cats.IndexOf(adminCat)) Fail("Einstellungen › Gruppen ziehen: Reihenfolge nicht geändert");
                    await Settle();
                    if (settingsWindow.Content is FrameworkElement treeRoot) Screenshot(treeRoot, "settings-gruppen-verschachtelt");

                    // Feste Gruppen: mit Schloss in der Liste, verschiebbar, nicht hinein
                    var fixedRows = settingsVm.CategoryRoots.Where(r => r.IsFixed).ToList();
                    if (fixedRows.Count != 3) Fail($"Einstellungen › Gruppen: feste Gruppen fehlen ({fixedRows.Count} statt 3)");
                    var favRow = fixedRows.FirstOrDefault(r => r.Name == FixedGroups.Favorites);
                    if (favRow != null)
                    {
                        settingsVm.BeginCategoryDrag(favRow);
                        var noInto = settingsVm.PreviewCategoryDrop(adminCat, GroupDropZone.Into, 0);
                        if (noInto is { IsValid: true }) Fail("Einstellungen › Gruppen: feste Gruppe lässt sich in eine Gruppe ziehen");
                        settingsVm.PreviewCategoryDrop(adminCat, GroupDropZone.After, 0);
                        if (!adminCat.GapAfter) Fail("Einstellungen › Gruppen: Lücke für die feste Gruppe fehlt");
                        if (!settingsVm.CommitCategoryDrop()) Fail("Einstellungen › Gruppen: feste Gruppe lässt sich nicht verschieben");
                        var roots = settingsVm.CategoryRoots.Select(r => r.Name).ToList();
                        if (roots.IndexOf(FixedGroups.Favorites) != roots.IndexOf("Admin") + 1) Fail($"Einstellungen › Gruppen: Favoriten nicht hinter „Admin“ ({string.Join(", ", roots)})");
                        await Settle();
                        if (settingsWindow.Content is FrameworkElement fixedRoot) Screenshot(fixedRoot, "settings-gruppen-fest");
                    }
                }
            }

            settingsVm.FocusGroup(Settings.Folders[0].Group);
            if (settingsVm.Section != "groups") Fail("Einstellungen: Gruppe fokussieren wechselt nicht zur Gruppen-Seite");

            // Rechnergruppen: dynamische Gruppe mit Regelblöcken
            foreach (var mg in settingsVm.MachineGroups) mg.IsExpanded = true;
            if (settingsVm.MachineGroups.FirstOrDefault(m => m.IsDynamic) is not { } dyn) Fail("Rechnergruppen: dynamische Gruppe fehlt in den Einstellungen");
            else
            {
                if (dyn.Blocks.Count != 2 || dyn.HasRulesError) Fail($"Rechnergruppen: Regelblöcke falsch geladen ({dyn.Blocks.Count}, {dyn.RulesError})");
                if (!dyn.FilterPreview.Contains("(|")) Fail("Rechnergruppen: Blöcke werden nicht mit ODER verknüpft");
                var back = dyn.ToGroup();
                if (back.AdQuery == null || back.Machines.Count != 2) Fail("Rechnergruppen: dynamische Gruppe geht beim Speichern verloren");
                dyn.Blocks[0].Rules[0].AttributeText = "Betriebssystem";
                if (dyn.Blocks[0].Rules[0].Attribute != "operatingSystem") Fail("Rechnergruppen: Attribut-Auswahl wird nicht übernommen");
                dyn.Blocks[0].Rules[0].AttributeText = "name";
            }
            foreach (var section in new[] { "general", "groups", "custom", "tags", "workspaces", "look", "behavior", "exec", "remote", "machines", "diag" })
            {
                settingsVm.Section = section;
                Step("Einstellungen " + section);
                await Settle();
                if (settingsWindow.Content is FrameworkElement root) Screenshot(root, "settings-" + section);
                if (settingsWindow.NavigationSectionForTest != section)
                    Fail($"Navigation: Abschnitt „{section}“ ist in der Leiste nicht ausgewählt ({settingsWindow.NavigationSectionForTest ?? "nichts"})");
                await Settle();
            }

            Step("Einstellungen: Zurück und einklappbarer Rand");
            {
                var nav = settingsWindow.NavigationForTest;
                settingsVm.Section = "look";
                await Settle();
                settingsVm.Section = "behavior";
                await Settle();
                if (!nav.CanGoBack) Fail("Navigation: Zurück ist nach zwei Abschnitten nicht möglich");
                nav.GoBack();
                await Settle();
                if (settingsVm.Section != "look") Fail($"Navigation: Zurück führt zu „{settingsVm.Section}“ statt „look“");
                nav.IsPaneOpen = false;
                await Settle();
                if (settingsWindow.Content is FrameworkElement compactRoot) Screenshot(compactRoot, "settings-navigation-eingeklappt");
                nav.IsPaneOpen = true;
                await Settle();
                settingsVm.Section = "custom";
                await Settle();
                // Auswahlspalte der eigenen Einträge: Kästchen ohne Beschriftung ohne Innenabstand (sonst halb verdeckt)
                var listChecks = VisualDescendants(settingsWindow).OfType<System.Windows.Controls.CheckBox>()
                    .Where(c => c.IsVisible && c.Content == null).ToList();
                if (listChecks.Count == 0) Fail("Eigene Einträge: kein Auswahl-Kästchen gefunden");
                else if (listChecks.Any(c => c.Padding.Left > 0)) Fail("Eigene Einträge: Auswahl-Kästchen hat Innenabstand und wird abgeschnitten");
            }

            Step("Fluent-Bedienelemente (WPF UI) im Einstellungsfenster");
            {
                settingsVm.Section = "general";
                await Settle();
                static IEnumerable<DependencyObject> Tree(DependencyObject d)
                {
                    for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
                    {
                        var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                        yield return c;
                        foreach (var cc in Tree(c)) yield return cc;
                    }
                }
                var visuals = Tree(settingsWindow).ToList();
                if (!visuals.OfType<Wpf.Ui.Controls.ToggleSwitch>().Any()) Fail("Fluent: keine Fluent-Schalter im Abschnitt „Allgemein“");
                if (!visuals.OfType<Wpf.Ui.Controls.Button>().Any()) Fail("Fluent: keine Fluent-Schaltflächen im Abschnitt „Allgemein“");
                if (settingsWindow.TryFindResource("AccentFillColorDefaultBrush") is not System.Windows.Media.SolidColorBrush fluentAccent ||
                    Application.Current.TryFindResource("Accent") is not System.Windows.Media.SolidColorBrush ownAccent || fluentAccent.Color != ownAccent.Color)
                    Fail("Fluent: Akzentfarbe weicht vom Programm-Akzent ab");
            }

            Step("Design bei offenem Fenster wechseln");
            {
                var themeBefore = Settings.Theme;
                settingsVm.Section = "look";
                Settings.Theme = ThemeMode.Light;
                ThemeService.Apply(Settings);
                await Settle();
                static bool IsLightBrush(object? b) => b is System.Windows.Media.SolidColorBrush sb && PanelColors.IsLight(new Rgb(sb.Color.R, sb.Color.G, sb.Color.B));
                if (!IsLightBrush(Application.Current.Resources[Views.WindowHelper.MicaTintKey])) Fail("Design: Mica-Tönung bleibt nach dem Wechsel auf Hell dunkel");
                if (settingsWindow.Background is System.Windows.Media.SolidColorBrush { Color.A: 255 } && !IsLightBrush(settingsWindow.Background))
                    Fail("Design: Einstellungsfenster bleibt nach dem Wechsel auf Hell dunkel");
                if (settingsWindow.TryFindResource("TextFillColorPrimaryBrush") is not System.Windows.Media.SolidColorBrush fluentText || IsLightBrush(fluentText))
                    Fail("Design: Fluent-Texte bleiben nach dem Wechsel auf Hell hell");
                // Tatsächliche Farben an den Bedienelementen (nicht nur im Wörterbuch): Schrift muss zum Design passen
                CheckFluentReadable(settingsWindow, light: true, "Einstellungen nach Wechsel auf Hell");
                {
                    var fresh = ChoiceWindow.ForTest();
                    fresh.Show();
                    await Settle();
                    CheckFluentReadable(fresh, light: true, "neuer Dialog im hellen Design");
                    fresh.Close();
                }
                if (settingsWindow.Content is FrameworkElement lightRoot) Screenshot(lightRoot, "settings-hell-live");
                Settings.Theme = ThemeMode.Dark;
                ThemeService.Apply(Settings);
                await Settle();
                CheckFluentReadable(settingsWindow, light: false, "Einstellungen nach Wechsel Hell → Dunkel");
                if (settingsWindow.Content is FrameworkElement darkRoot) Screenshot(darkRoot, "settings-dunkel-live");
                Settings.Theme = themeBefore;
                ThemeService.Apply(Settings);
                await Settle();
                // Zurück auf die Ausgangseinstellung (z. B. „wie Windows“ – auf dem Prüfrechner kann das hell sein)
                CheckFluentReadable(settingsWindow, light: !ThemeService.IsDark, $"Einstellungen nach Wechsel zurück ({(ThemeService.IsDark ? "dunkel" : "hell")})");
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
                tilesVm.RefreshViews();
                var firstTile = tilesVm.VisibleTiles.First();
                if (firstTile.Status != RemoteStatus.Error) Fail("Kachelkarte: Fehler stehen nicht vorne");
                tilesVm.SelectedTile = firstTile;
                await ShowAndClose(new ResultsWindow(tilesVm));
            }
            await ShowAndClose(IconPickerWindow.ForTest());
            await ShowAndClose(CatalogWindow.ForTest());
            await ShowAndClose(NewGroupWindow.ForTest());
            await ShowAndClose(ChoiceWindow.ForTest());
            await ShowAndClose(PlaceholderWindow.ForTestWithProfiles());
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

            // Zuletzt: Leistungsmessung mit 500 Einträgen (verändert Einstellungen und Ordner)
            await RunPerformanceAsync(dir);
        }
        catch (Exception ex)
        {
            _selfTestErrors.Add(ex.ToString());
        }
        finally
        {
            DisposePanels();
        }
        lock (bindingErrors.Errors)
            foreach (var e in bindingErrors.Errors.Take(25)) Fail("Bindungsfehler: " + e);
        return _selfTestErrors.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, _selfTestErrors);
    }

    private void Fail(string message) => _selfTestErrors.Add(message);

    /// <summary>WPF-Seite der Panels (Fenster, Hover-Zonen, Docks) für die Prüfungen.</summary>
    private IReadOnlyList<PanelHost> WpfHosts => _hosts.Select(h => (PanelHost)h.View).ToList();

    private PanelHost WpfHost(int index) => (PanelHost)_hosts[index].View;

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

    /// <summary>Fenster mit feinem Schwarz-Weiß-Streifenmuster an der Stelle des Panels (Messung des Weichzeichners).</summary>
    private static Window StripeWindow(System.Drawing.Rectangle px)
    {
        var brush = new System.Windows.Media.DrawingBrush
        {
            TileMode = System.Windows.Media.TileMode.Tile, Viewport = new Rect(0, 0, 4, 4), ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute,
            Drawing = new System.Windows.Media.GeometryDrawing(System.Windows.Media.Brushes.Black, null, new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 2, 4)))
        };
        var scale = Interop.NativeMethods.ScaleForPoint(px.Left + 1, px.Top + 1);
        return new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Background = System.Windows.Media.Brushes.White, Content = new System.Windows.Controls.Border { Background = brush },
            Left = px.Left / scale - 40, Top = px.Top / scale - 40, Width = px.Width / scale + 80, Height = px.Height / scale + 80, Title = "Streifen"
        };
    }

    /// <summary>Bildschirmbereich als PNG speichern (mit allem, was Windows dort zusammensetzt).</summary>
    private void ScreenCapture(int x, int y, int width, int height, string name)
    {
        if (_progressFile == null || width < 1 || height < 1) return;
        try
        {
            var dir = Path.Combine(Path.GetDirectoryName(_progressFile)!, "screens");
            Directory.CreateDirectory(dir);
            using var bmp = new System.Drawing.Bitmap(width, height);
            using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, bmp.Size);
            bmp.Save(Path.Combine(dir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex)
        {
            Step("Bildschirmaufnahme fehlgeschlagen: " + ex.Message);
        }
    }

    /// <summary>Mittlerer Helligkeitsunterschied benachbarter Pixel (120×40 px ab x, y auf dem Bildschirm).</summary>
    private static double StripeContrast(int x, int y)
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(120, 40);
            using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, bmp.Size);
            double sum = 0; int n = 0;
            for (int yy = 0; yy < bmp.Height; yy += 4)
            for (int xx = 0; xx < bmp.Width - 1; xx++)
            {
                var a = bmp.GetPixel(xx, yy); var c = bmp.GetPixel(xx + 1, yy);
                sum += Math.Abs((a.R + a.G + a.B) / 3.0 - (c.R + c.G + c.B) / 3.0);
                n++;
            }
            return n == 0 ? -1 : sum / n;
        }
        catch { return -1; }
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
        // Alle Dialoge nutzen Fluent-Bedienelemente (Themes/Fluent.xaml)
        if (!VisualDescendants(w).OfType<Wpf.Ui.Controls.Button>().Any())
            Fail($"Fluent: {w.GetType().Name} zeigt keine Fluent-Schaltflächen");
        if (w.Content is FrameworkElement root) Screenshot(root, "window-" + w.GetType().Name + "-" + (++_shotCounter));
        w.Close();
    }

    /// <summary>
    /// Fluent-Schaltflächen (Standard) und Auswahllisten: Schrift dunkel im hellen, hell im dunklen Design – sonst ist
    /// sie auf dem Hintergrund nicht lesbar (z. B. wenn ein Wörterbuch beim Designwechsel nicht nachgezogen wurde).
    /// </summary>
    private void CheckFluentReadable(Window w, bool light, string where)
    {
        static bool IsLight(System.Windows.Media.Brush? b) =>
            b is System.Windows.Media.SolidColorBrush s && PanelColors.IsLight(new Rgb(s.Color.R, s.Color.G, s.Color.B));
        var visuals = VisualDescendants(w).ToList();
        var buttons = visuals.OfType<Wpf.Ui.Controls.Button>()
            .Where(b => b.IsVisible && b.Appearance == Wpf.Ui.Controls.ControlAppearance.Secondary).ToList();
        var combos = visuals.OfType<System.Windows.Controls.ComboBox>().Where(c => c.IsVisible).ToList();
        if (buttons.Count == 0) Fail($"Fluent ({where}): keine sichtbare Standard-Schaltfläche zum Prüfen");
        // Hinweistexte („Tag“) leerer Eingabefelder müssen sichtbar sein
        foreach (var t in visuals.OfType<System.Windows.Controls.TextBox>().Where(t => t.IsVisible && t.Text.Length == 0 && t.Tag is string { Length: > 0 }).Take(5))
            if (t.Template?.FindName("Placeholder", t) is not System.Windows.Controls.TextBlock { IsVisible: true })
            { Fail($"Fluent ({where}): Hinweistext „{t.Tag}“ im leeren Eingabefeld fehlt"); break; }
        foreach (var b in buttons.Take(5))
            if (IsLight(b.Foreground) == light) { Fail($"Fluent ({where}): Schaltfläche „{b.Content}“ mit unlesbarer Schrift"); break; }
        foreach (var c in combos.Take(5))
            if (IsLight(c.Foreground) == light) { Fail($"Fluent ({where}): Auswahlliste mit unlesbarer Schrift"); break; }
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject d)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
            yield return c;
            foreach (var cc in VisualDescendants(c)) yield return cc;
        }
    }
}
