using System.Windows;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
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
    public IconService Icons { get; private set; } = null!;
    public PanelViewModel Panel { get; private set; } = null!;

    public void Start()
    {
        var firstStart = !Store.Exists;
        Settings = Store.Load();
        if (firstStart)
        {
            // Der Installer legt den Autostart-Eintrag an; beim ersten Start übernehmen.
            Settings.StartWithWindows = AutostartService.IsEnabled();
            SaveSettings();
        }
        ThemeService.Apply(Settings.Theme);

        Icons = new IconService(Application.Current.Dispatcher);
        Repository = new ItemRepository(Settings, Application.Current.Dispatcher);
        Launcher = new Launcher(() => Settings);
        Panel = new PanelViewModel(this);
        Repository.Changed += (_, _) =>
        {
            UpdateKnownFiles();
            _ = MaintainPathsAsync();
            Panel.Rebuild();
        };

        _panelWindow = new PanelWindow(this);
        CreateTriggers();

        StartFullscreenWatch();
        _hotkey = new HotkeyService();
        _hotkey.Pressed += (_, _) => ShowPanelFromHotkey();
        var hotkeyError = _hotkey.Register(Settings.Hotkey);

        _tray = new TrayIcon(this);

        AppLog.Info($"Power-Exec {UpdateService.CurrentVersionText} gestartet ({Settings.Folders.Count} Ordner)");
        Repository.LoadCache();
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

    private System.Windows.Threading.DispatcherTimer? _fullscreenTimer;

    /// <summary>Prüft jede Sekunde, ob das Vordergrundfenster einen Monitor vollständig bedeckt.</summary>
    private void StartFullscreenWatch()
    {
        _fullscreenTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _fullscreenTimer.Tick -= OnFullscreenTick;
        _fullscreenTimer.Tick += OnFullscreenTick;
        _fullscreenTimer.Start();
    }

    private void OnFullscreenTick(object? sender, EventArgs e)
    {
        try
        {
            if (!Settings.HideHotZoneInFullscreen)
            {
                foreach (var t in _triggers) t.SetSuspended(false);
                return;
            }
            var hwnd = Interop.NativeMethods.GetForegroundWindow();
            System.Drawing.Rectangle? rect = null;
            string cls = string.Empty;
            if (hwnd != IntPtr.Zero && Interop.NativeMethods.IsWindowVisible(hwnd) &&
                Interop.NativeMethods.GetWindowThreadProcessId(hwnd, out var pid) != 0 && pid != Environment.ProcessId &&
                Interop.NativeMethods.GetWindowRect(hwnd, out var r))
            {
                rect = System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                cls = Interop.NativeMethods.ClassNameOf(hwnd);
            }
            foreach (var t in _triggers)
            {
                bool full = rect.HasValue && Core.Layout.Fullscreen.IsFullscreen(rect.Value, t.Monitor.Bounds, cls);
                if (full && !_panelWindowIsOpen) t.SetSuspended(true);
                else if (!full) t.SetSuspended(false);
            }
        }
        catch
        {
            // Erkennung ist optional
        }
    }

    private bool _panelWindowIsOpen => _panelWindow?.IsOpen == true;

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

    public FolderConfig? FolderOf(LaunchItem item) =>
        Settings.Folders.FirstOrDefault(f => f.Path.Equals(item.RootFolder, StringComparison.OrdinalIgnoreCase));

    /// <summary>Blendet einen Eintrag aus (entfernt den Haken in den Ordner-Einstellungen).</summary>
    public void HideItem(LaunchItem item)
    {
        var folder = FolderOf(item);
        if (folder == null) return;
        FolderVisibility.SetVisible(folder, FolderVisibility.RelativePath(folder, item.FullPath), false);
        Settings.Favorites.RemoveAll(p => p.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        SaveSettings();
        Panel.Rebuild();
        _tray?.ShowBalloon("Ausgeblendet", $"{item.FileName} wird nicht mehr angezeigt. Wieder einblenden: Einstellungen › Ordner (Haken setzen).");
    }

    /// <summary>
    /// Nach jedem Scan: im Modus „Automatisch anzeigen“ neue Dateien als bekannt merken,
    /// verschwundene vergessen. Ordner mit Fehler (z. B. Freigabe nicht erreichbar) bleiben unverändert.
    /// </summary>
    private void UpdateKnownFiles()
    {
        if (Repository.IsScanning) return;
        bool changed = false;
        foreach (var folder in Settings.Folders)
        {
            var status = Repository.FolderStatuses.FirstOrDefault(s => s.Path.Equals(folder.Path, StringComparison.OrdinalIgnoreCase));
            if (status == null || status.Error != null || status.FromCache) continue;
            var found = Repository.Items
                .Where(i => i.RootFolder.Equals(folder.Path, StringComparison.OrdinalIgnoreCase))
                .Select(i => FolderVisibility.RelativePath(folder, i.FullPath));
            changed |= FolderVisibility.UpdateKnown(folder, found);
        }
        if (changed) SaveSettings();
    }

    private static readonly Dictionary<string, (DateTime Written, string? Fingerprint)> FingerprintCache = new(StringComparer.OrdinalIgnoreCase);

    private static string? CachedFingerprint(string path)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(path);
            lock (FingerprintCache)
                if (FingerprintCache.TryGetValue(path, out var c) && c.Written == written) return c.Fingerprint;
            var fp = PathMigration.Fingerprint(path);
            lock (FingerprintCache) FingerprintCache[path] = (written, fp);
            return fp;
        }
        catch
        {
            return null;
        }
    }

    private bool _maintaining;

    /// <summary>
    /// Nach jedem Scan: umbenannte/verschobene Dateien wiederfinden (Profile, Favorit, Sortierung übernehmen)
    /// und Fingerabdrücke der gemerkten Dateien aktualisieren. Läuft im Hintergrund.
    /// </summary>
    private async Task MaintainPathsAsync()
    {
        if (Repository.IsScanning || _maintaining) return;
        _maintaining = true;
        try
        {
            var okRoots = Repository.FolderStatuses.Where(s => s.Error == null && !s.FromCache).Select(s => s.Path).ToList();
            if (okRoots.Count == 0) return;
            var existing = Repository.Items
                .Where(i => okRoots.Any(r => r.Equals(i.RootFolder, StringComparison.OrdinalIgnoreCase)))
                .Select(i => i.FullPath).ToList();
            var snapshot = SettingsStore.Clone(Settings);
            var (moves, fpChanged) = await Task.Run(() =>
            {
                var m = PathMigration.Migrate(snapshot, existing, okRoots, CachedFingerprint, p => { try { return new FileInfo(p).Length; } catch { return (long?)null; } });
                var c = PathMigration.UpdateFingerprints(snapshot, existing, CachedFingerprint);
                return (m, c);
            });
            if (moves.Count == 0 && !fpChanged) return;
            foreach (var m in moves)
            {
                PathMigration.Apply(Settings, m);
                AppLog.Info($"Umbenannt/verschoben erkannt: {m.OldPath} → {m.NewPath} (Profile und Favorit übernommen)");
            }
            Settings.Fingerprints = snapshot.Fingerprints;
            SaveSettings();
            if (moves.Count > 0)
            {
                Panel.Rebuild();
                _tray?.ShowBalloon("Datei wiedergefunden", moves.Count == 1
                    ? $"{Path.GetFileName(moves[0].OldPath)} heißt jetzt {Path.GetFileName(moves[0].NewPath)}. Profile und Favorit wurden übernommen."
                    : $"{moves.Count} umbenannte oder verschobene Dateien erkannt. Profile und Favoriten wurden übernommen.");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Abgleich umbenannter Dateien fehlgeschlagen", ex);
        }
        finally
        {
            _maintaining = false;
        }
    }

    /// <summary>Standardaktion im Panel: Standardprofil des Eintrags ausführen.</summary>
    public void Run(LaunchItem item, ExecutionMode mode) =>
        RunProfile(item, null, mode == ExecutionMode.CurrentUser ? null : mode);

    /// <summary>
    /// Startet ein Profil. <paramref name="overrideMode"/> erzwingt einmalig Admin oder anderes Konto
    /// (Knöpfe „Admin“ und „Als …“ im Panel).
    /// </summary>
    public void RunProfile(LaunchItem item, LaunchProfile? profile, ExecutionMode? overrideMode = null)
    {
        if (item.IsPowerShell) item = Repository.Reload(item); // Parameter könnten sich geändert haben
        profile ??= ProfileResolver.GetDefault(Settings, item);
        var mode = overrideMode ?? (profile.RunAsAdmin ? ExecutionMode.Administrator : ExecutionMode.CurrentUser);
        bool remote = item.SupportsRemote && profile.Target != ProfileTarget.Local;

        if (item.IsPowerShell)
        {
            Dictionary<string, object> values = new();
            var errors = ParameterValueConverter.TryConvert(item.Parameters, ProfileResolver.InitialValues(profile, item), out values);
            if (ProfileResolver.NeedsDialog(profile, item) || errors.Count > 0)
            {
                OpenRunDialog(item, mode, remote, profile);
                return;
            }
            var request = BuildRequest(item, profile, mode, values);
            if (!ConfirmRun(item, profile, remote ? profile.Machines.Count : 0, null)) return;
            _panelWindow?.HideAfterAction();
            RecordRun(item, profile);
            AppLog.Info($"Start: {item.FullPath} · Profil {profile.Name} · {(remote ? profile.Machines.Count + " Rechner" : "lokal")}{(mode == ExecutionMode.Administrator ? " · Admin" : string.Empty)}");
            if (remote)
            {
                MachineList.MarkUsed(Settings, profile.Machines);
                SaveSettings();
                StartRemote(new ExecutionRequest
                {
                    Item = request.Item, Mode = request.Mode, Engine = profile.Engine, NoExit = request.NoExit, NoProfile = request.NoProfile,
                    ExecutionPolicy = request.ExecutionPolicy, Parameters = request.Parameters, ResolvedPath = request.ResolvedPath,
                    Targets = profile.Machines.ToList(), CopyFolder = item.CopyFolderByDefault
                }, null);
            }
            else
            {
                Launcher.StartLocal(request, null);
            }
            return;
        }

        if (!ConfirmRun(item, profile, 0, null)) return;
        _panelWindow?.HideAfterAction();
        if (Launcher.StartLocal(BuildRequest(item, profile, mode, new Dictionary<string, object>()), null))
        {
            RecordRun(item, profile);
            AppLog.Info($"Start: {item.FullPath} · Profil {profile.Name}{(mode == ExecutionMode.Administrator ? " · Admin" : string.Empty)}");
        }
    }

    /// <summary>Sicherheitsabfrage, falls im Profil eingestellt.</summary>
    public bool ConfirmRun(LaunchItem item, LaunchProfile profile, int targetCount, Window? owner)
    {
        if (!profile.ConfirmBeforeRun) return true;
        var name = Settings.FindEntry(item.FullPath)?.DisplayName is { Length: > 0 } dn ? dn : item.DisplayName;
        var where = targetCount > 0 ? $" auf {targetCount} {(targetCount == 1 ? "Rechner" : "Rechnern")}" : string.Empty;
        var text = $"„{name}“ mit dem Profil „{profile.Name}“{where} wirklich ausführen?" +
                   (profile.RunAsAdmin ? "\n\nDer Start erfolgt mit Administratorrechten." : string.Empty);
        var result = owner != null
            ? MessageBox.Show(owner, text, "Ausführen bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(text, "Ausführen bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    /// <summary>Entfernt die Zonen-Kennung („aus dem Internet“) einer Datei.</summary>
    public void UnblockItem(LaunchItem item)
    {
        if (WindowsMetadata.Unblock(item.FullPath))
        {
            AppLog.Info($"Entsperrt: {item.FullPath}");
            Repository.Reload(item);
            _ = Repository.ScanAsync();
        }
        else
        {
            MessageBox.Show($"{item.FileName} konnte nicht entsperrt werden (fehlende Schreibrechte?).", "Entsperren",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Für „Zuletzt ausgeführt“ merken.</summary>
    public void RecordRun(LaunchItem item, LaunchProfile profile)
    {
        RecentRunList.Add(Settings, item.FullPath, profile.IsImplicit ? "Standard" : profile.Name);
        SaveSettings();
        Panel.Rebuild();
    }

    private ExecutionRequest BuildRequest(LaunchItem item, LaunchProfile profile, ExecutionMode mode, Dictionary<string, object> values) => new()
    {
        Item = item,
        Mode = mode,
        Engine = Launcher.EffectiveEngine(profile.Engine),
        NoExit = profile.NoExit,
        NoProfile = profile.NoProfile,
        ExecutionPolicy = Settings.ExecutionPolicy,
        Parameters = values,
        ExtraArguments = profile.Arguments,
        ResolvedPath = PathResolver.ToUnc(item.FullPath)
    };

    public void OpenRunDialog(LaunchItem item, ExecutionMode mode, bool remote, LaunchProfile? profile = null)
    {
        if (item.IsPowerShell) item = Repository.Reload(item);
        _panelWindow?.HideAfterAction();
        var vm = new RunViewModel(this, item, mode, remote, profile);
        var w = new RunWindow(vm);
        w.Show();
        w.Activate();
    }

    /// <summary>Profil-Dialog aus dem Panel: arbeitet direkt auf den Einstellungen und speichert sofort.</summary>
    public void ShowProfiles(LaunchItem item)
    {
        _panelWindow?.HideAfterAction(force: true);
        if (item.IsPowerShell) item = Repository.Reload(item);
        var vm = new ProfilesViewModel(this, item, Settings, saveImmediately: true);
        var w = new ProfilesWindow(vm);
        w.Show();
        w.Activate();
    }

    /// <summary>Wird vom Profil-Dialog nach dem Speichern aufgerufen.</summary>
    public void OnProfilesSaved()
    {
        SaveSettings();
        Panel.Rebuild();
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
        vm.Finished += () =>
        {
            if (!Settings.NotifyOnFinish || w.IsActive) return;
            bool problems = vm.ErrorCount + vm.UnreachableCount > 0 || vm.RunnerError != null;
            _tray?.ShowBalloon($"{request.Item.DisplayName} ist fertig",
                vm.RunnerError ?? $"{vm.SuccessCount} erfolgreich · {vm.ErrorCount} Fehler · {vm.UnreachableCount} nicht erreichbar",
                () =>
                {
                    if (!w.IsLoaded) return;
                    if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                    w.Activate();
                },
                warning: problems);
        };
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
        Icons?.Dispose();
        _tray = null;
        Repository?.Dispose();
        foreach (var t in _triggers) t.Close();
        _triggers.Clear();
    }
}
