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
            ThemeService.Apply(Settings.Theme);

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
