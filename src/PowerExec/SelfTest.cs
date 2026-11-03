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

    /// <summary>Fortschritt in eine Datei schreiben – bei einem Hänger sieht man den letzten Schritt.</summary>
    private void Step(string name)
    {
        if (_progressFile == null) return;
        try { File.AppendAllText(_progressFile, $"{DateTime.Now:HH:mm:ss.fff} {name}{Environment.NewLine}"); } catch { }
    }

    public async Task<string?> RunSelfTestAsync(string? progressFile = null)
    {
        _progressFile = progressFile;
        Step("Start");
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
            Settings.CustomEntries.Add(new CustomEntry { Name = "Intranet", Kind = CustomEntryKind.Url, Target = "https://example.com" });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Freigabe", Kind = CustomEntryKind.Folder, Target = dir });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Server", Kind = CustomEntryKind.Rdp, Target = "SRV-01" });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Terminal", Kind = CustomEntryKind.Command, Target = "cmd.exe" });
            Settings.CustomEntries.Add(new CustomEntry { Kind = CustomEntryKind.File, Target = Path.Combine(dir, "Sub", "Info.ps1") });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Ping", Kind = CustomEntryKind.Snippet, Code = "param([string]$Ziel = 'localhost')\nTest-Connection $Ziel -Count 1", Icon = "hero:signal" });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Signatur", Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text, Code = "Viele Grüße\n{{Name}}" });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Linux", Kind = CustomEntryKind.Ssh, Target = "srv", User = "root", Port = 2222, Icon = "emoji:🐧" });
            Settings.CustomEntries.Add(new CustomEntry { Name = "AD", Kind = CustomEntryKind.Tool, Target = "dsa.msc", Icon = "fluent:people_team", SingleClick = true });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Ticket", Kind = CustomEntryKind.Url, Target = "https://example.com/{{Nr}}", Browser = BrowserChoice.Edge, Private = true, Icon = "glyph:E8EC" });
            Settings.Favorites.Add(Path.Combine(dir, "Cleanup.cmd"));
            Settings.Entries.Add(new EntryConfig { Path = Path.Combine(dir, "Cleanup.cmd"), Notes = "Nur nach Rücksprache" });
            Settings.MachineGroups.Add(new MachineGroup { Name = "Web", Machines = ["WEB1", "WEB2"] });
            Settings.Panels.Add(new PanelConfig { Name = "Hauptpanel" });
            Settings.Panels.Add(new PanelConfig { Name = "Zweites", Edge = ScreenEdge.Left, Folders = [PanelConfig.CustomEntriesKey] });
            WorkspaceManager.EnsureInitialized(Settings);
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
                    if (host.Config.Name == "Hauptpanel" && !host.ViewModel.AllGroups.Any(g => g.GroupPath == "Admin/AD" && g.Items.Count == 1))
                        Fail($"{layout}: Untergruppe Admin/AD fehlt oder ist leer");
                    host.Window.HidePanel();
                    await Settle();
                }
            }

            Step("Einstellungen");
            // Einstellungen: alle Bereiche
            var settingsVm = new SettingsViewModel(this, null);
            var settingsWindow = new SettingsWindow(settingsVm, this);
            settingsWindow.Show();
            foreach (var section in new[] { "folders", "custom", "panels", "panel", "workspaces", "machines", "exec", "diag", "updates" })
            {
                settingsVm.Section = section;
                Step("Einstellungen " + section);
                await Settle();
            }
            settingsWindow.Close();

            // Dialoge
            var item = Repository.Items.First(i => i.FullPath.Equals(script, StringComparison.OrdinalIgnoreCase));
            await ShowAndClose(new ProfilesWindow(new ProfilesViewModel(this, item, Settings, saveImmediately: false)));
            await ShowAndClose(new RunWindow(new RunViewModel(this, item, ExecutionMode.CurrentUser, remote: false)));
            await ShowAndClose(new RunWindow(new RunViewModel(this, item, ExecutionMode.CurrentUser, remote: true)));
            var request = new ExecutionRequest { Item = item, Targets = ["WEB1", "WEB2"] };
            await ShowAndClose(new ResultsWindow(new ResultsViewModel(this, request, "TEST\\user")));
            await ShowAndClose(IconPickerWindow.ForTest());
            await ShowAndClose(CatalogWindow.ForTest());
            await ShowAndClose(PlaceholderWindow.ForTest(new[] { new Placeholder("Rechner", "SRV-01"), new Placeholder("Ticket", "") }));
            foreach (var kind in Enum.GetValues<CustomEntryKind>())
                await ShowAndClose(CustomEntryWindow.ForTest(new CustomEntry { Kind = kind, Target = "x" }, GroupManager.All(Settings)));

            Step("Snippets");
            // Eigene Einträge: Snippet-Datei, Platzhalter, Ein-Klick
            var snippet = Settings.CustomEntries.First(c => c.Name == "Ping");
            var snippetItem = DescribeCustom(snippet);
            if (!File.Exists(snippet.SnippetPath) || snippetItem.Parameters.Count != 1) Fail("PowerShell-Snippet: Datei oder param() nicht erkannt");
            if (!_hosts[0].ViewModel.AllItems.Any(i => i.IsSingleClick)) Fail("Ein-Klick-Eintrag fehlt im Panel");

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
        return _selfTestErrors.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, _selfTestErrors);
    }

    private void Fail(string message) => _selfTestErrors.Add(message);

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
        w.Close();
    }
}
