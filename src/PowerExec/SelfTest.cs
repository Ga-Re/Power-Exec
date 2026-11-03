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

    public async Task<string?> RunSelfTestAsync()
    {
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
            Settings.CustomEntries.Add(new CustomEntry { Name = "Intranet", Kind = CustomEntryKind.Url, Target = "https://example.com" });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Freigabe", Kind = CustomEntryKind.Folder, Target = dir });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Server", Kind = CustomEntryKind.Rdp, Target = "SRV-01" });
            Settings.CustomEntries.Add(new CustomEntry { Name = "Terminal", Kind = CustomEntryKind.Command, Target = "cmd.exe" });
            Settings.CustomEntries.Add(new CustomEntry { Kind = CustomEntryKind.File, Target = Path.Combine(dir, "Sub", "Info.ps1") });
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
            await Repository.ScanAsync();
            CreateHosts();
            if (Repository.Items.Count < 3) Fail($"Scan fand nur {Repository.Items.Count} Einträge");

            foreach (var layout in new[] { ItemLayout.List, ItemLayout.Compact, ItemLayout.Grid })
            {
                foreach (var host in _hosts)
                {
                    host.Config.Layout = layout;
                    host.ViewModel.Rebuild();
                    host.Window.ShowPanel(ScreenService.TriggerMonitors(host.Config)[0], viaHotkey: true);
                    await Settle();
                    var first = host.ViewModel.VisibleItems.FirstOrDefault();
                    if (first == null && host.Config.Name == "Hauptpanel") Fail($"{layout}: keine Einträge im Panel");
                    first?.SelectCommand.Execute(null);
                    await Settle();
                    if (host.Config.Name == "Hauptpanel" && !host.ViewModel.AllGroups.Any(g => g.GroupPath == "Admin/AD" && g.Items.Count == 1))
                        Fail($"{layout}: Untergruppe Admin/AD fehlt oder ist leer");
                    host.Window.HidePanel();
                    await Settle();
                }
            }

            // Einstellungen: alle Bereiche
            var settingsVm = new SettingsViewModel(this, null);
            var settingsWindow = new SettingsWindow(settingsVm, this);
            settingsWindow.Show();
            foreach (var section in new[] { "folders", "custom", "panels", "panel", "workspaces", "machines", "exec", "diag", "updates" })
            {
                settingsVm.Section = section;
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

    private static async Task ShowAndClose(Window w)
    {
        w.Show();
        await Settle();
        w.Close();
    }
}
