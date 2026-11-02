using System.Windows;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Updates;
using PowerExec.Services;
using PowerExec.ViewModels;
using PowerExec.Views;

namespace PowerExec;

/// <summary>Zentrale Steuerung: Einstellungen, Panel, Aktionen, Updates.</summary>
public sealed class AppController : IDisposable
{
    private PanelWindow? _panelWindow;
    private readonly List<EdgeTriggerWindow> _triggers = new();
    private HotkeyService? _hotkey;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;

    public SettingsStore Store { get; } = new();
    public AppSettings Settings { get; private set; } = new();
    public ItemRepository Repository { get; private set; } = null!;
    public Launcher Launcher { get; private set; } = null!;
    public UpdateService Updates { get; } = new();
    public PanelViewModel Panel { get; private set; } = null!;

    public void Start()
    {
        Settings = Store.Load();
        ThemeService.Apply(Settings.Theme);

        Repository = new ItemRepository(Settings, Application.Current.Dispatcher);
        Launcher = new Launcher(() => Settings);
        Panel = new PanelViewModel(this);
        Repository.Changed += (_, _) => Panel.Rebuild();

        _panelWindow = new PanelWindow(this);
        CreateTriggers();

        _hotkey = new HotkeyService();
        _hotkey.Pressed += (_, _) => ShowPanelFromHotkey();
        var hotkeyError = _hotkey.Register(Settings.Hotkey);

        _tray = new TrayIcon(this);

        Repository.SetupWatchers();
        _ = Repository.ScanAsync();
        AutostartService.Apply(Settings.StartWithWindows);

        if (Settings.Folders.Count == 0)
        {
            _tray.ShowBalloon("Power-Exec läuft", "Lege in den Einstellungen fest, welche Ordner durchsucht werden.");
            ShowSettings();
        }
        else if (hotkeyError != null)
        {
            _tray.ShowBalloon("Tastenkürzel nicht verfügbar", hotkeyError);
        }

        if (Settings.CheckUpdatesOnStart)
            _ = CheckUpdatesAsync(manual: false, includePrereleases: Settings.IncludePrereleases);
    }

    public void SaveSettings()
    {
        try { Store.Save(Settings); }
        catch (Exception ex)
        {
            MessageBox.Show("Die Einstellungen konnten nicht gespeichert werden:\n" + ex.Message, "Power-Exec",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <returns>Fehlermeldung zum Tastenkürzel oder null.</returns>
    public string? ApplySettings(AppSettings updated)
    {
        var foldersChanged = !FoldersEqual(Settings, updated);
        Settings = updated;
        SaveSettings();
        ThemeService.Apply(Settings.Theme);
        AutostartService.Apply(Settings.StartWithWindows);
        var hotkeyError = _hotkey?.Register(Settings.Hotkey);
        CreateTriggers();
        _panelWindow?.ApplySettings();
        if (foldersChanged) Repository.UpdateSettings(Settings);
        else
        {
            Repository.SetupWatchers();
            Panel.Rebuild();
        }
        return hotkeyError;
    }

    private static bool FoldersEqual(AppSettings a, AppSettings b)
    {
        string Key(AppSettings s) => string.Join("|", s.Folders.Select(f => $"{f.Path}*{f.Recursive}*{string.Join(",", f.Extensions)}*{string.Join(",", f.Excludes)}"))
                                     + "#" + string.Join(",", s.DefaultExtensions) + "#" + string.Join(",", s.GlobalExcludes);
        return Key(a) == Key(b);
    }

    // ---- Panel ----------------------------------------------------------

    private void CreateTriggers()
    {
        foreach (var t in _triggers) t.Close();
        _triggers.Clear();
        foreach (var m in ScreenService.TriggerMonitors(Settings))
        {
            var t = new EdgeTriggerWindow(this, m);
            t.Show();
            _triggers.Add(t);
        }
    }

    internal IEnumerable<EdgeTriggerWindow> Triggers => _triggers;

    public void ShowPanelFromTrigger(MonitorInfo monitor) => _panelWindow?.ShowPanel(monitor, viaHotkey: false);

    public void ShowPanelFromHotkey()
    {
        if (_panelWindow == null) return;
        if (_panelWindow.IsOpen && _panelWindow.OpenedViaHotkey)
        {
            _panelWindow.HidePanel();
            return;
        }
        var monitor = Settings.MonitorMode == MonitorMode.All
            ? ScreenService.MonitorAtCursor()
            : ScreenService.TriggerMonitors(Settings)[0];
        _panelWindow.ShowPanel(monitor, viaHotkey: true);
    }

    // ---- Aktionen -------------------------------------------------------

    public bool IsFavorite(string path) => Settings.Favorites.Contains(path, StringComparer.OrdinalIgnoreCase);

    public void ToggleFavorite(LaunchItem item)
    {
        if (IsFavorite(item.FullPath))
            Settings.Favorites.RemoveAll(p => p.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        else
            Settings.Favorites.Add(item.FullPath);
        SaveSettings();
        Panel.RefreshFavorites();
    }

    /// <summary>Blendet einen Eintrag aus, indem er als Ausschluss beim Ordner gespeichert wird.</summary>
    public void HideItem(LaunchItem item)
    {
        var folder = Settings.Folders.FirstOrDefault(f => f.Path.Equals(item.RootFolder, StringComparison.OrdinalIgnoreCase));
        if (folder == null) return;
        var rel = Path.GetRelativePath(folder.Path, item.FullPath);
        if (!folder.Excludes.Contains(rel, StringComparer.OrdinalIgnoreCase)) folder.Excludes.Add(rel);
        Settings.Favorites.RemoveAll(p => p.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        SaveSettings();
        _ = Repository.ScanAsync();
        _tray?.ShowBalloon("Ausgeblendet", $"{item.FileName} wird nicht mehr angezeigt. Rückgängig in den Einstellungen unter „Ordner“.");
    }

    /// <summary>Standardaktion: ohne Parameter direkt starten, mit Parametern den Dialog öffnen.</summary>
    public void Run(LaunchItem item, ExecutionMode mode)
    {
        if (item.IsPowerShell)
        {
            item = Repository.Reload(item); // Parameter könnten sich geändert haben
            if (item.HasParameters)
            {
                OpenRunDialog(item, mode, remote: false);
                return;
            }
        }
        _panelWindow?.HideAfterAction();
        Launcher.StartLocal(Launcher.DefaultRequest(item, mode), null);
    }

    public void OpenRunDialog(LaunchItem item, ExecutionMode mode, bool remote)
    {
        if (item.IsPowerShell) item = Repository.Reload(item);
        _panelWindow?.HideAfterAction();
        var vm = new RunViewModel(this, item, mode, remote);
        var w = new RunWindow(vm);
        w.Show();
        w.Activate();
    }

    /// <summary>Startet die Remote-/Massenausführung und öffnet die Ergebnisliste.</summary>
    public bool StartRemote(ExecutionRequest request, Window? owner)
    {
        RunCredential? cred = null;
        if (request.Mode == ExecutionMode.OtherUser)
        {
            cred = CredentialWindow.Prompt(owner, request.Item.DisplayName, remote: true);
            if (cred == null) return false;
        }
        var account = cred?.Account ?? $"{Environment.UserDomainName}\\{Environment.UserName}";
        var vm = new ResultsViewModel(this, request, account);
        var w = new ResultsWindow(vm);
        w.Show();
        w.Activate();
        vm.Start(cred);
        return true;
    }

    public void ShowSettings(string? folderPath = null)
    {
        _panelWindow?.HideAfterAction(force: true);
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(new SettingsViewModel(this, folderPath), this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // ---- Updates --------------------------------------------------------

    public async Task<ReleaseInfo?> CheckUpdatesAsync(bool manual, bool includePrereleases)
    {
        var r = await Updates.CheckAsync(includePrereleases);
        if (r != null)
        {
            Panel.UpdateTitle = $"Version {r.Version.ToString(3)} ist verfügbar";
            Panel.UpdateSubtitle = $"Installiert: {UpdateService.CurrentVersionText} · aus GitHub Releases";
            Panel.UpdateVisible = true;
            if (!manual) _tray?.ShowBalloon("Update verfügbar", $"Power-Exec {r.Version.ToString(3)} kann installiert werden. Öffne das Panel.");
        }
        return r;
    }

    public async Task InstallUpdateAsync()
    {
        var r = Updates.Available;
        if (r == null) return;
        Panel.UpdateBusy = true;
        Panel.UpdateSubtitle = "Wird heruntergeladen …";
        string? error;
        try
        {
            error = await Updates.DownloadAndPrepareAsync(r, new Progress<double>(p => Panel.UpdateSubtitle = $"Wird heruntergeladen … {p:P0}"));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        Panel.UpdateBusy = false;
        if (error == null)
        {
            Exit();
            return;
        }
        Panel.UpdateSubtitle = "Update fehlgeschlagen";
        if (MessageBox.Show(error + "\n\nRelease-Seite im Browser öffnen?", "Update", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            Launcher.OpenUrl(r.HtmlUrl);
    }

    public void Exit()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        _tray?.Dispose();
        _tray = null;
        Repository?.Dispose();
        foreach (var t in _triggers) t.Close();
        _triggers.Clear();
    }
}
