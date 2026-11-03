using System.Windows;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Search;
using PowerExec.Core.Updates;
using PowerExec.Services;
using PowerExec.ViewModels;
using PowerExec.Views;

namespace PowerExec;

/// <summary>Zentrale Steuerung: Einstellungen, Panel, Aktionen, Updates.</summary>
public sealed partial class AppController : IDisposable
{
    private readonly List<PanelHost> _hosts = new();
    private HotkeyService? _hotkey;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;

    public SettingsStore Store { get; private set; } = new();
    public AppSettings Settings { get; private set; } = new();
    public ItemRepository Repository { get; private set; } = null!;
    public Launcher Launcher { get; private set; } = null!;
    public UpdateService Updates { get; } = new();
    public IconService Icons { get; private set; } = null!;
    public HealthService Health { get; private set; } = null!;
    /// <summary>Update-Hinweis für alle Panels und das Einstellungsfenster.</summary>
    public UpdateState Update { get; } = new();

    public IReadOnlyList<PanelHost> Hosts => _hosts;

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
        SetupSnippetFolder();
        Controls.MachineBox.GroupSource = () => Settings.MachineGroups;
        Infrastructure.TagBrushConverter.Lookup = t => Tags.ColorOf(Settings, t);
        CustomEntry.TerminalAvailable = () => IconService.ResolveProgram("wt.exe") != null;
        CustomEntry.RdpFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec", "Rdp");
        Placeholders.ClipboardText = () => { try { return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty; } catch { return string.Empty; } };

        Icons = new IconService(Application.Current.Dispatcher);
        Health = new HealthService(Application.Current.Dispatcher);
        Health.Changed += RebuildPanels;
        Repository = new ItemRepository(Settings, Application.Current.Dispatcher);
        Launcher = new Launcher(() => Settings);
        Repository.Changed += (_, _) =>
        {
            UpdateKnownFiles();
            _ = MaintainPathsAsync();
            RebuildPanels();
        };

        _hotkey = new HotkeyService();
        _hotkey.Pressed += id => ShowPanelFromHotkey(_hosts.FirstOrDefault(h => h.HotkeyId == id));
        var hotkeyError = CreateHosts();
        StartFullscreenWatch();

        _tray = new TrayIcon(this);

        AppLog.Info($"Power-Exec {UpdateService.CurrentVersionText} gestartet ({Settings.Folders.Count} Ordner)");
        Repository.LoadCache();
        Repository.SetupWatchers();
        _ = Repository.ScanAsync();
        AutostartService.Apply(Settings.StartWithWindows);

        if (Settings.Folders.Count == 0)
        {
            _tray.ShowBalloon("Power-Exec läuft", "Lege in den Einstellungen fest, welche Ordner durchsucht werden.");
            ShowSettings(section: "groups");
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
    /// <summary>
    /// Änderung aus dem Panel (Gruppe, Favorit, eigener Eintrag …): speichern, Panels neu aufbauen und
    /// ein offenes Einstellungsfenster informieren, damit es den neuen Stand zeigt.
    /// </summary>
    private void CommitUserChange()
    {
        SaveSettings();
        RebuildPanels();
        SettingsChangedOutside?.Invoke();
    }

    /// <summary>Einstellungen wurden außerhalb des Einstellungsfensters geändert (z. B. im Panel).</summary>
    public event Action? SettingsChangedOutside;

    /// <summary>
    /// Übernimmt neue Einstellungen (live aus dem Einstellungsfenster). Panels und Hover-Zonen werden nur neu erzeugt,
    /// wenn sich an ihnen etwas geändert hat; Ordner nur neu eingelesen, wenn Ordner oder Filter anders sind.
    /// </summary>
    /// <returns>Fehlermeldung zum Tastenkürzel oder null.</returns>
    public string? ApplySettings(AppSettings updated)
    {
        var foldersChanged = !FoldersEqual(Settings, updated);
        var panelsChanged = PanelKey(Settings) != PanelKey(updated);
        var themeChanged = Settings.Theme != updated.Theme;
        var autostartChanged = Settings.StartWithWindows != updated.StartWithWindows;
        Settings = updated;
        SaveSettings();
        if (themeChanged) ThemeService.Apply(Settings.Theme);
        if (autostartChanged) AutostartService.Apply(Settings.StartWithWindows);
        string? hotkeyError = null;
        if (panelsChanged || themeChanged || _hosts.Count == 0) hotkeyError = CreateHosts();
        if (foldersChanged) Repository.UpdateSettings(Settings);
        else
        {
            Repository.SetupWatchers();
            RebuildPanels();
        }
        return hotkeyError;
    }

    /// <summary>Alles, was Panelfenster und Hover-Zonen betrifft (ohne „angeheftet“).</summary>
    private static string PanelKey(AppSettings s) =>
        System.Text.Json.JsonSerializer.Serialize(s.Panels.Select(p => { var c = p.Clone(); c.Pinned = false; return c; })) +
        $"|{s.HoverDelayMs}|{s.HideHotZoneInFullscreen}";

    private static bool FoldersEqual(AppSettings a, AppSettings b)
    {
        string Key(AppSettings s) => string.Join("|", s.Folders.Select(f => $"{f.Path}*{f.Recursive}*{string.Join(",", f.Extensions)}*{string.Join(",", f.Excludes)}"))
                                     + "#" + string.Join(",", s.DefaultExtensions) + "#" + string.Join(",", s.GlobalExcludes);
        return Key(a) == Key(b);
    }

    // ---- Panel ----------------------------------------------------------

    /// <summary>Erzeugt alle Panels neu (nach dem Start und nach geänderten Einstellungen).</summary>
    /// <returns>Fehlermeldungen zu Tastenkürzeln oder null.</returns>
    private string? CreateHosts()
    {
        foreach (var h in _hosts) h.Dispose();
        _hosts.Clear();
        _hotkey?.UnregisterAll();
        if (Settings.Panels.Count == 0) Settings.Panels.Add(new PanelConfig());
        var errors = new List<string>();
        int id = 0x5045; // "PE"
        foreach (var config in Settings.Panels)
        {
            var host = new PanelHost(this, config, id++);
            host.CreateTriggers(this);
            _hosts.Add(host);
            var err = _hotkey?.Register(host.HotkeyId, config.Hotkey);
            if (err != null) errors.Add($"{config.Name}: {err}");
        }
        foreach (var h in _hosts) h.ViewModel.Rebuild();
        return errors.Count == 0 ? null : string.Join("\n", errors);
    }

    public void RebuildPanels()
    {
        foreach (var h in _hosts) h.ViewModel.Rebuild();
    }

    private void ForEachPanel(Action<PanelViewModel> action)
    {
        foreach (var h in _hosts) action(h.ViewModel);
    }

    /// <summary>Nach einer Aktion alle Panels einfahren (außer angeheftete, sofern nicht erzwungen).</summary>
    public void HidePanels(bool force = false)
    {
        foreach (var h in _hosts) h.Window.HideAfterAction(force);
    }

    internal IEnumerable<EdgeTriggerWindow> AllTriggers => _hosts.SelectMany(h => h.Triggers);

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
                foreach (var t in AllTriggers) t.SetSuspended(false);
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
            foreach (var h in _hosts)
                foreach (var t in h.Triggers)
                {
                    bool full = rect.HasValue && Core.Layout.Fullscreen.IsFullscreen(rect.Value, t.Monitor.Bounds, cls);
                    if (full && !h.Window.IsOpen) t.SetSuspended(true);
                    else if (!full) t.SetSuspended(false);
                }
        }
        catch
        {
            // Erkennung ist optional
        }
    }

    // ---- Arbeitsbereiche -----------------------------------------------

    /// <summary>Wechselt den Arbeitsbereich (gilt für alle Panels) und durchsucht die Ordner neu.</summary>
    public void SwitchWorkspace(string name)
    {
        if (name.Equals(Settings.ActiveWorkspace, StringComparison.CurrentCultureIgnoreCase)) return;
        if (!WorkspaceManager.Switch(Settings, name)) return;
        SaveSettings();
        AppLog.Info($"Arbeitsbereich gewechselt: {Settings.ActiveWorkspace}");
        Repository.UpdateSettings(Settings);
        RebuildPanels();
        _settingsWindow?.Close(); // Einstellungen zeigen sonst noch den alten Arbeitsbereich
    }

    public void ShowPanelFromTrigger(PanelHost host, MonitorInfo monitor) => host.Window.ShowPanel(monitor, viaHotkey: false);

    /// <summary>Tastenkürzel oder Tray: Panel öffnen bzw. wieder schließen.</summary>
    public void ShowPanelFromHotkey(PanelHost? host = null)
    {
        host ??= _hosts.FirstOrDefault();
        if (host == null) return;
        var w = host.Window;
        if (w.IsOpen && w.OpenedViaHotkey)
        {
            w.HidePanel();
            return;
        }
        var monitor = host.Config.MonitorMode == MonitorMode.All
            ? ScreenService.MonitorAtCursor()
            : ScreenService.TriggerMonitors(host.Config)[0];
        w.ShowPanel(monitor, viaHotkey: true);
    }

    // ---- Aktionen -------------------------------------------------------

    /// <summary>Eintrag zu einem Pfad (Datei aus einem Ordner, eigener Eintrag „custom:…“ oder eigene Datei).</summary>
    public LaunchItem? FindItem(string path)
    {
        var scanned = Repository.Items.FirstOrDefault(i => i.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (scanned != null) return scanned;
        var custom = Settings.CustomEntries.FirstOrDefault(c => c.Key.Equals(path, StringComparison.OrdinalIgnoreCase));
        return custom == null ? null : DescribeCustom(custom);
    }

    /// <summary>Eigener Eintrag als <see cref="LaunchItem"/>; bei Dateien und PowerShell-Snippets mit Metadaten (Synopsis, Parameter).</summary>
    public LaunchItem DescribeCustom(CustomEntry entry)
    {
        var item = entry.ToLaunchItem();
        if (entry.Kind == CustomEntryKind.File) return Repository.DescribeSingle(item);
        if (entry.Kind == CustomEntryKind.Snippet && entry.Language == SnippetLanguage.PowerShell) return DescribeSnippet(entry, item);
        return item;
    }

    private readonly Dictionary<string, (string Code, LaunchItem Item)> _snippetCache = new();

    /// <summary>Legt den Snippet-Code als .ps1 ab (UTF-8 mit BOM für Windows PowerShell) und liest param() und Hilfe daraus.</summary>
    private LaunchItem DescribeSnippet(CustomEntry entry, LaunchItem item)
    {
        try
        {
            if (!_snippetCache.TryGetValue(entry.Id, out var cached) || cached.Code != entry.Code || !File.Exists(entry.SnippetPath))
            {
                Directory.CreateDirectory(CustomEntry.SnippetFolder);
                File.WriteAllText(entry.SnippetPath, entry.Code, new System.Text.UTF8Encoding(true));
                var loaded = Repository.LoadFile(entry.SnippetPath, PanelConfig.CustomEntriesKey).Rebased(entry.Key, PanelConfig.CustomEntriesKey);
                cached = (entry.Code, loaded);
                _snippetCache[entry.Id] = cached;
            }
            var result = cached.Item.Rebased(entry.Key, PanelConfig.CustomEntriesKey);
            result.DisplayName = item.DisplayName;
            result.Synopsis = entry.Description.Trim().Length > 0 ? entry.Description.Trim() : (result.Synopsis.Length > 0 ? result.Synopsis : item.Synopsis);
            result.LaunchTarget = entry.SnippetPath;
            result.CustomKind = CustomEntryKind.Snippet;
            result.Source = entry;
            return result;
        }
        catch (Exception ex)
        {
            AppLog.Error("Snippet konnte nicht vorbereitet werden", ex);
            return item;
        }
    }

    /// <summary>Snippet-Ordner: unter ProgramData, damit auch andere Konten („Als …“) die Datei lesen können.</summary>
    private static void SetupSnippetFolder()
    {
        foreach (var root in new[] { Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(root), "Power-Exec", "Snippets");
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".probe");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                CustomEntry.SnippetFolder = dir;
                foreach (var f in Directory.EnumerateFiles(dir, "*-run-*.ps1"))
                    try { if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromDays(1)) File.Delete(f); } catch { }
                return;
            }
            catch { }
        }
    }

    /// <summary>Aktuellen Stand eines Eintrags holen (Parameter eines Scripts können sich geändert haben).</summary>
    private LaunchItem Refresh(LaunchItem item)
    {
        if (item.PlaceholdersResolved) return item;
        if (item.Source != null)
        {
            var entry = Settings.CustomEntries.FirstOrDefault(c => c.Id == item.Source.Id) ?? item.Source;
            return DescribeCustom(entry);
        }
        return item.IsPowerShell ? Repository.Reload(item) : item;
    }

    /// <summary>
    /// Platzhalter ({{Name}}) abfragen und einsetzen. Liefert null bei Abbruch.
    /// Snippets bekommen dafür eine eigene .ps1 für genau diesen Start.
    /// </summary>
    private LaunchItem? ResolvePlaceholders(LaunchItem item)
    {
        if (item.PlaceholdersResolved || item.Source is not { } entry || !entry.NeedsResolving) return item;
        IReadOnlyDictionary<string, string>? values = new Dictionary<string, string>();
        if (entry.Placeholders.Count > 0) // eingebaute Variablen ({{$Datum}} …) brauchen keine Abfrage
            values = WithDialog(() => PlaceholderWindow.Ask(item.DisplayName, entry.Placeholders,
                name => ParameterHistory.Get(Settings, entry.Key, name)));
        if (values == null) return null;
        foreach (var kv in values) ParameterHistory.Add(Settings, entry.Key, kv.Key, kv.Value);
        SaveSettings();
        var resolved = entry.WithValues(values);
        LaunchItem result;
        if (resolved.Kind == CustomEntryKind.Snippet && resolved.Language == SnippetLanguage.PowerShell)
        {
            var runPath = Path.Combine(CustomEntry.SnippetFolder, $"{entry.Id}-run-{DateTime.Now:yyyyMMddHHmmssfff}.ps1");
            File.WriteAllText(runPath, resolved.Code, new System.Text.UTF8Encoding(true));
            result = Repository.LoadFile(runPath, PanelConfig.CustomEntriesKey).Rebased(entry.Key, PanelConfig.CustomEntriesKey);
            result.DisplayName = item.DisplayName;
            result.LaunchTarget = runPath;
            result.CustomKind = CustomEntryKind.Snippet;
        }
        else
        {
            result = resolved.ToLaunchItem();
            result.DisplayName = item.DisplayName;
        }
        result.Source = resolved;
        result.PlaceholdersResolved = true;
        return result;
    }

    /// <summary>Snippet in die Zwischenablage (Platzhalter vorher abfragen).</summary>
    public void CopySnippet(LaunchItem item, bool record = true)
    {
        if (item.Source is not { Kind: CustomEntryKind.Snippet }) return;
        var resolved = ResolvePlaceholders(item);
        if (resolved?.Source == null) return;
        try
        {
            ClipboardText.Set(resolved.Source.Code, resolved.Source.CopyAsHtml);
            if (record) RecordRun(item, ProfileResolver.GetDefault(Settings, item));
            HidePanels();
            if (resolved.Source.PasteAfterCopy) _ = PasteAsync(item.DisplayName);
            else _tray?.ShowBalloon("In die Zwischenablage kopiert", item.DisplayName);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Die Zwischenablage ist gerade belegt: " + ex.Message, "Power-Exec", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task PasteAsync(string name)
    {
        if (!await ForegroundTracker.PasteIntoPreviousAsync())
            _tray?.ShowBalloon("In die Zwischenablage kopiert", $"{name}: Kein Zielfenster gefunden – mit Strg+V einfügen.");
    }

    // ---- Eigene Gruppen ---------------------------------------------------

    private int _dialogs;

    /// <summary>Solange ein Dialog aus dem Panel offen ist, fährt das Panel nicht ein.</summary>
    public bool IsDialogOpen => _dialogs > 0;

    /// <summary>Ja/Nein-Frage über dem offenen Panel (das Panel liegt sonst darüber).</summary>
    private bool Confirm(string text, string title)
    {
        var owner = _hosts.FirstOrDefault(h => h.Window.IsOpen)?.Window;
        return WithDialog(() => owner != null
            ? MessageBox.Show(owner, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)) == MessageBoxResult.Yes;
    }

    public T WithDialog<T>(Func<T> show)
    {
        _dialogs++;
        try { return show(); }
        finally { _dialogs--; }
    }

    private string? GroupNameError(string name, string? parent, string? exceptPath = null)
    {
        var clean = GroupManager.CleanName(name);
        var path = parent == null ? clean : parent + GroupManager.Separator + clean;
        return GroupManager.Exists(Settings, path) && !path.Equals(exceptPath, StringComparison.CurrentCultureIgnoreCase)
            ? "Eine Gruppe mit diesem Namen gibt es hier schon." : null;
    }

    /// <param name="parent">Pfad der übergeordneten Gruppe (Untergruppe) oder null.</param>
    /// <returns>Pfad der neuen Gruppe oder null bei Abbruch.</returns>
    public string? CreateGroup(string? parent = null)
    {
        var heading = parent == null ? "Neue Gruppe" : $"Neue Untergruppe in „{GroupManager.LeafOf(parent)}“";
        var name = WithDialog(() => InputWindow.Ask(heading, "Einträge ziehst du anschließend per Drag & Drop hinein – auch Dateien, Ordner und Links aus dem Explorer oder Browser. Gruppen lassen sich ebenfalls per Drag & Drop verschieben und ineinander ziehen.",
            parent == null ? "Neue Gruppe" : "Neue Untergruppe", "Anlegen", n => GroupNameError(n, parent)));
        if (name == null) return null;
        var path = GroupManager.Create(Settings, name, parent);
        if (parent != null)
            Settings.CollapsedGroups.RemoveAll(k => k.Equals(GroupManager.GroupKey(parent), StringComparison.OrdinalIgnoreCase));
        CommitUserChange();
        return path;
    }

    public void RenameGroup(string path)
    {
        var parent = GroupManager.ParentOf(path);
        var newName = WithDialog(() => InputWindow.Ask("Gruppe umbenennen", string.Empty, GroupManager.LeafOf(path), "Umbenennen", n => GroupNameError(n, parent, path)));
        if (newName == null || newName == GroupManager.LeafOf(path)) return;
        if (GroupManager.Rename(Settings, path, newName))
        {
            CommitUserChange();
        }
    }

    public void DeleteGroup(string path, int count)
    {
        var name = GroupManager.LeafOf(path);
        var subs = GroupManager.DescendantCount(Settings, path);
        var folders = Settings.Folders.Where(f => f.Group.Length > 0 && GroupManager.IsSameOrBelow(f.Group, path)).Select(f => f.Path).ToList();
        var text = $"Gruppe „{name}“" + (subs > 0 ? $" mit {subs} {(subs == 1 ? "Untergruppe" : "Untergruppen")}" : string.Empty) + " löschen?" +
                   (folders.Count > 0 ? $"\n\nDer Ordner {string.Join(", ", folders)} wird dann nicht mehr eingelesen. Die Dateien selbst bleiben unverändert." : string.Empty) +
                   (count > 0 && folders.Count == 0 ? $"\n\nDie {count} Einträge bleiben erhalten und erscheinen wieder in ihrer smarten Gruppe bzw. unter „Eigene Einträge“." : string.Empty);
        if (!Confirm(text, "Gruppe löschen")) return;
        GroupManager.Delete(Settings, path);
        CommitUserChange();
    }

    public void MoveGroup(string path, int delta)
    {
        GroupManager.Move(Settings, path, delta);
        CommitUserChange();
    }

    /// <summary>Drag &amp; Drop einer Gruppe auf eine andere: davor, dahinter oder hinein (Untergruppe).</summary>
    public void MoveGroupTo(string path, string target, GroupDropPosition position)
    {
        var result = GroupManager.MoveGroup(Settings, path, target, position);
        if (result == null)
        {
            _tray?.ShowBalloon("Verschieben nicht möglich", "Am Ziel gibt es bereits eine Gruppe mit diesem Namen, oder die Gruppe würde in ihre eigene Untergruppe verschoben.");
            return;
        }
        if (position == GroupDropPosition.Into)
            Settings.CollapsedGroups.RemoveAll(k => k.Equals(GroupManager.GroupKey(target), StringComparison.OrdinalIgnoreCase));
        CommitUserChange();
    }

    /// <summary>Gruppe im aktiven Arbeitsbereich ausblenden (wieder einblenden: Einstellungen › Arbeitsbereiche).</summary>
    public void HideGroupInWorkspace(string groupPath)
    {
        WorkspaceManager.SetGroupHidden(WorkspaceManager.Active(Settings), groupPath, true);
        CommitUserChange();
        _tray?.ShowBalloon("Gruppe ausgeblendet", $"„{GroupManager.LeafOf(groupPath)}“ ist im Arbeitsbereich „{Settings.ActiveWorkspace}“ ausgeblendet. Wieder einblenden: Einstellungen › Arbeitsbereiche.");
    }

    /// <summary>Smarte Gruppe für einen Ordner anlegen (Ordnerauswahl).</summary>
    public string? CreateSmartGroup(string? parent = null)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Ordner für die smarte Gruppe auswählen" };
        if (WithDialog(() => dlg.ShowDialog()) != true) return null;
        if (Settings.Folders.FirstOrDefault(f => f.Path.Equals(dlg.FolderName, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            MessageBox.Show($"Dieser Ordner gehört bereits zur Gruppe „{existing.Group}“.", "Smarte Gruppe", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        var folder = SmartGroups.Add(Settings, dlg.FolderName, null, parent);
        SaveSettings();
        Repository.UpdateSettings(Settings);
        CommitUserChange();
        return folder.Group;
    }

    /// <summary>Eintrag in eine Gruppe schieben (leer = aus der Gruppe nehmen), optional vor/hinter einen anderen Eintrag.</summary>
    public void AssignGroup(string path, string? group, string? targetKey = null, string? beforeOrAfterPath = null, bool after = false, IReadOnlyList<string>? targetOrder = null)
    {
        GroupManager.Assign(Settings, path, group);
        if (targetKey != null && beforeOrAfterPath != null && targetOrder != null)
        {
            var order = targetOrder.Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)).Append(path).ToList();
            Settings.CustomOrder[targetKey] = ItemOrdering.Move(order, path, beforeOrAfterPath, after);
        }
        CommitUserChange();
    }

    /// <summary>Aus dem Explorer oder Browser gezogene Ziele übernehmen.</summary>
    public void AddDropped(IReadOnlyList<DroppedTarget> targets, string? group)
    {
        if (targets.Count == 0) return;
        var scanned = new HashSet<string>(Repository.Items.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
        var keys = DropClassifier.AddToSettings(Settings, targets, group, scanned.Contains);
        CommitUserChange();
        AppLog.Info($"Per Drag & Drop hinzugefügt{(group != null ? " (Gruppe " + group + ")" : string.Empty)}: {string.Join(", ", targets.Select(t => t.Target))}");
        if (group == null && keys.Count > 0 && Hosts.Any(h => !h.Config.ShowsFolder(PanelConfig.CustomEntriesKey)))
            _tray?.ShowBalloon("Hinzugefügt", "Die Einträge stehen unter „Eigene Einträge“. Panels mit eigener Inhaltsauswahl zeigen sie nur, wenn „Eigene Einträge“ angehakt ist.");
    }

    /// <summary>Neuen eigenen Eintrag in einer Gruppe anlegen (Dialog).</summary>
    /// <summary>Neuer Eintrag aus einem Menü: Art-Schlüssel („Url“, „TextSnippet“ …) oder null = Art im Dialog wählen.</summary>
    public void NewCustomEntry(string? kindKey, string? group)
    {
        var template = CustomEntry.FromKindKey(kindKey);
        var result = WithDialog(() => ProfilesWindow.EditEntryEx(this, Settings, template, isNew: true, saveImmediately: false, group, null));
        HandleEditorResult(result, group);
    }

    /// <summary>Eintrag vorbelegt anlegen (Zwischenablage, Vorlage, Duplikat) – der Eintragsdialog zeigt ihn zum Bestätigen.</summary>
    public bool NewCustomEntry(CustomEntry template, string? group, IEnumerable<string>? tags = null)
    {
        var result = WithDialog(() => ProfilesWindow.EditEntryEx(this, Settings, template, isNew: true, saveImmediately: false, group, tags));
        return HandleEditorResult(result, group);
    }

    /// <summary>Ergebnis des Eintragsdialogs: gespeichert, oder Vorlagen / Dateien / smarte Gruppe gewählt.</summary>
    private bool HandleEditorResult(ProfilesWindow.EditorResult result, string? group)
    {
        if (result.SavedKey != null) { CommitUserChange(); return true; }
        switch (result.Alternative)
        {
            case "catalog": AddFromCatalog(group); break;
            case "files": AddFilesDialog(group); break;
        }
        if (result.SmartGroupPath is { } path) CreateSmartGroupFor(path, group);
        return false;
    }

    /// <summary>Dateien auswählen und als Einträge anlegen.</summary>
    public void AddFilesDialog(string? group)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = group != null ? $"Dateien zur Gruppe „{GroupManager.LeafOf(group)}“ hinzufügen" : "Dateien hinzufügen",
            Multiselect = true,
            Filter = "Scripte und Programme (*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc)|*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc|Alle Dateien (*.*)|*.*"
        };
        if (WithDialog(() => dlg.ShowDialog()) == true)
            AddDropped(dlg.FileNames.Select(f => new DroppedTarget(CustomEntryKind.File, f, Path.GetFileName(f))).ToList(), group);
    }

    /// <summary>Ordner als smarte Gruppe anlegen (aus dem Eintragsdialog).</summary>
    public void CreateSmartGroupFor(string folderPath, string? parent)
    {
        if (Settings.Folders.FirstOrDefault(f => f.Path.Equals(folderPath, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            MessageBox.Show($"Dieser Ordner gehört bereits zur Gruppe „{existing.Group}“.", "Smarte Gruppe", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SmartGroups.Add(Settings, folderPath, null, string.IsNullOrEmpty(parent) ? null : parent);
        SaveSettings();
        Repository.UpdateSettings(Settings);
        CommitUserChange();
    }

    /// <summary>Eintrag aus dem Dialog testen – mit dem eingestellten Standardverhalten, ohne „Zuletzt ausgeführt“.</summary>
    public void TestRun(CustomEntry entry, LaunchProfile profile)
    {
        var item = DescribeCustom(entry);
        RunProfile(item, profile, null, record: false);
    }

    /// <summary>Eigenen Eintrag bearbeiten – im selben Dialog wie die Startprofile.</summary>
    public void EditCustomEntry(LaunchItem item) => ShowProfiles(item);

    /// <summary>Kopie eines eigenen Eintrags anlegen (gleiche Gruppe, Schlagworte, Profile), Dialog zum Anpassen.</summary>
    public void DuplicateCustomEntry(LaunchItem item)
    {
        var entry = Settings.CustomEntries.FirstOrDefault(c => c.Key.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        if (entry == null || entry.Kind == CustomEntryKind.File) return;
        var copy = entry.Duplicate();
        var key = WithDialog(() => ProfilesWindow.EditEntry(this, Settings, copy, isNew: true, saveImmediately: false,
            GroupManager.GroupOf(Settings, entry.Key), Tags.Of(Settings, entry.Key)));
        if (key == null) return;
        var source = Settings.FindEntry(entry.Key);
        if (source != null && source.Profiles.Count > 0 && Settings.FindEntry(key) is { Profiles.Count: 0 } target)
            target.Profiles = source.Profiles.Select(p => p.Clone()).ToList();
        CommitUserChange();
    }

    private EntryConfig AddEntry(string key)
    {
        var e = new EntryConfig { Path = key };
        Settings.Entries.Add(e);
        return e;
    }

    public void ToggleSingleClick(LaunchItem item)
    {
        var entry = Settings.FindEntry(item.FullPath) ?? AddEntry(item.FullPath);
        entry.SingleClick = !entry.SingleClick;
        CommitUserChange();
    }

    /// <summary>Strg+V bzw. „Aus Zwischenablage“: Inhalt erkennen und als Eintrag vorschlagen.</summary>
    public void AddFromClipboard(string? group)
    {
        string? text = null;
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                AddDropped(files.Select(f => DropClassifier.Classify(f, Directory.Exists, p => { try { return File.ReadAllText(p); } catch { return null; } })).ToList(), group);
                return;
            }
            if (Clipboard.ContainsText()) text = Clipboard.GetText();
        }
        catch { }
        if (string.IsNullOrWhiteSpace(text))
        {
            _tray?.ShowBalloon("Zwischenablage leer", "Kopiere eine Adresse, einen Pfad, einen Rechnernamen oder Code und versuche es erneut.");
            return;
        }
        NewCustomEntry(ClipboardClassifier.Suggest(text, Directory.Exists, File.Exists), group);
    }

    /// <summary>Vorlagen-Katalog: ausgewählte Vorlagen in die Gruppe übernehmen.</summary>
    public void AddFromCatalog(string? group)
    {
        var chosen = WithDialog(() => CatalogWindow.Pick());
        if (chosen == null || chosen.Count == 0) return;
        foreach (var t in chosen)
        {
            var e = ToolCatalog.Create(t);
            if (Settings.CustomEntries.Any(c => c.Kind == e.Kind && c.Target == e.Target && c.Arguments == e.Arguments && c.Code == e.Code)) continue;
            Settings.CustomEntries.Add(e);
            if (!string.IsNullOrEmpty(group)) GroupManager.Assign(Settings, e.Key, group);
        }
        CommitUserChange();
    }

    /// <summary>Symbol einer eigenen Gruppe wählen.</summary>
    /// <summary>Darstellung einer Gruppe: null = wie das Panel.</summary>
    public void SetGroupLayout(string groupPath, ItemLayout? layout)
    {
        GroupManager.SetLayout(Settings, groupPath, layout);
        SaveSettings();
        foreach (var h in Hosts) foreach (var g in h.ViewModel.AllGroups) g.RefreshLayout();
        SettingsChangedOutside?.Invoke();
    }

    /// <summary>Gruppenfarbe setzen (leer = keine bzw. von der übergeordneten Gruppe).</summary>
    public void SetGroupColor(string groupPath, string? color)
    {
        GroupManager.SetColor(Settings, groupPath, color);
        CommitUserChange();
    }

    /// <summary>Eigene Gruppenfarbe als #RRGGBB.</summary>
    public void PickGroupColor(string groupPath)
    {
        var current = GroupManager.OwnColorOf(Settings, groupPath);
        var text = WithDialog(() => InputWindow.Ask("Eigene Farbe", $"Farbe für „{GroupManager.LeafOf(groupPath)}“ als #RRGGBB, z. B. #3B82F6.", current, "Übernehmen",
            t => PanelColors.TryParse(t, out _) ? null : "Bitte als #RRGGBB angeben."));
        if (text != null) SetGroupColor(groupPath, text);
    }

    public void ChooseGroupIcon(string groupPath)
    {
        var current = GroupManager.IconOf(Settings, groupPath);
        var picked = WithDialog(() => IconPickerWindow.Pick(current, $"Symbol für „{GroupManager.LeafOf(groupPath)}“"));
        if (picked == null) return;
        GroupManager.SetIcon(Settings, groupPath, picked.Value);
        CommitUserChange();
    }

    /// <summary>Eigene Einträge als Datei speichern (z. B. eine Gruppe für Kollegen).</summary>
    public void ExportEntries(IReadOnlyList<CustomEntry> entries, string? rootGroup, Window? owner = null, AppSettings? source = null)
    {
        if (entries.Count == 0)
        {
            MessageBox.Show("Es sind keine eigenen Einträge ausgewählt.", "Exportieren", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = EntryShare.FileFilter,
            FileName = (rootGroup != null ? GroupManager.LeafOf(rootGroup) : "Eigene Einträge") + ".pxentries.json"
        };
        if (WithDialog(() => dlg.ShowDialog(owner)) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, EntryShare.Export(source ?? Settings, entries, rootGroup));
            AppLog.Info($"{entries.Count} eigene Einträge exportiert: {dlg.FileName}");
            _tray?.ShowBalloon("Exportiert", $"{entries.Count} {(entries.Count == 1 ? "Eintrag" : "Einträge")} gespeichert.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Exportieren", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void ExportGroup(string groupPath) =>
        ExportEntries(Settings.CustomEntries.Where(c => GroupManager.GroupOf(Settings, c.Key) is { Length: > 0 } g && GroupManager.IsSameOrBelow(g, groupPath)).ToList(), groupPath);

    /// <returns>true, wenn etwas importiert wurde.</returns>
    public bool ImportEntries(string? intoGroup = null, Window? owner = null)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = EntryShare.FileFilter + "|Alle Dateien (*.*)|*.*" };
        if (WithDialog(() => dlg.ShowDialog(owner)) != true) return false;
        try
        {
            var file = EntryShare.Parse(File.ReadAllText(dlg.FileName));
            var (added, updated) = EntryShare.Import(Settings, file, intoGroup);
            CommitUserChange();
            AppLog.Info($"Eigene Einträge importiert ({added} neu, {updated} aktualisiert): {dlg.FileName}");
            MessageBox.Show($"{added} neue und {updated} aktualisierte Einträge übernommen.", "Importieren", MessageBoxButton.OK, MessageBoxImage.Information);
            return added + updated > 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Die Datei konnte nicht gelesen werden:\n" + ex.Message, "Importieren", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    public void RemoveCustomEntry(LaunchItem item)
    {
        var entry = Settings.CustomEntries.FirstOrDefault(c => c.Key.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return;
        var name = Settings.FindEntry(item.FullPath)?.DisplayName is { Length: > 0 } dn ? dn : item.DisplayName;
        if (!Confirm($"„{name}“ aus Power-Exec entfernen?" + (entry.Kind is CustomEntryKind.File or CustomEntryKind.Folder ? "\n\nDie Datei bzw. der Ordner selbst bleibt unverändert." : string.Empty),
                "Eintrag entfernen")) return;
        Settings.CustomEntries.Remove(entry);
        Settings.Entries.RemoveAll(e => e.Path.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
        Settings.Favorites.RemoveAll(p => p.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
        Settings.RecentRuns.RemoveAll(r => r.Path.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
        CommitUserChange();
    }

    public bool IsFavorite(string path) => Settings.Favorites.Contains(path, StringComparer.OrdinalIgnoreCase);

    public void ToggleFavorite(LaunchItem item)
    {
        if (IsFavorite(item.FullPath))
            Settings.Favorites.RemoveAll(p => p.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        else
            Settings.Favorites.Add(item.FullPath);
        CommitUserChange();
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
        CommitUserChange();
        _tray?.ShowBalloon("Ausgeblendet", $"{item.FileName} wird nicht mehr angezeigt. Wieder einblenden: Einstellungen › Gruppen › smarte Gruppe aufklappen (Haken setzen).");
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
                RebuildPanels();
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
    /// <param name="record">false = Test aus dem Eintragsdialog: nicht in „Zuletzt ausgeführt“, ungespeicherten Stand verwenden.</param>
    public void RunProfile(LaunchItem item, LaunchProfile? profile, ExecutionMode? overrideMode = null, bool record = true)
    {
        profile ??= ProfileResolver.GetDefault(Settings, item);
        item = WithProfileOptions(record ? Refresh(item) : item, profile); // Parameter könnten sich geändert haben; Browser, Anzeige … des Profils
        if (item.Source?.CopiesToClipboard == true)
        {
            CopySnippet(item, record);
            return;
        }
        if (ResolvePlaceholders(item) is not { } resolvedItem) return;
        item = resolvedItem;
        bool remote = item.SupportsRemote && profile.Target != ProfileTarget.Local;
        var mode = Requirements.ModeFor(item, overrideMode ?? ProfileMode(profile), remote);
        var targets = remote ? MachineList.Resolve(Settings, profile.Machines) : Array.Empty<string>();
        if (remote && profile.Target == ProfileTarget.Fixed && targets.Count == 0)
        {
            OpenRunDialog(item, mode, remote, profile); // z. B. leere Rechnergruppe
            return;
        }

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
            if (!ConfirmRun(item, profile, targets.Count, null)) return;
            HidePanels();
            if (record) RecordRun(item, profile);
            AppLog.Info($"Start: {item.FullPath} · Profil {profile.Name} · {(remote ? targets.Count + " Rechner" : "lokal")}{(mode == ExecutionMode.Administrator ? " · Admin" : string.Empty)}");
            if (remote)
            {
                // Gruppenmitglieder nicht in „Zuletzt verwendet“ übernehmen, nur direkt eingetragene Rechner.
                MachineList.MarkUsed(Settings, profile.Machines.Where(n => !n.TrimStart().StartsWith('@')));
                SaveSettings();
                StartRemote(new ExecutionRequest
                {
                    Item = request.Item, Mode = request.Mode, Engine = Requirements.EngineFor(item, profile.Engine), NoExit = request.NoExit, NoProfile = request.NoProfile,
                    ExecutionPolicy = request.ExecutionPolicy, Parameters = request.Parameters, ResolvedPath = request.ResolvedPath,
                    Targets = targets.ToList(), CopyFolder = item.CopyFolderByDefault
                }, null);
            }
            else
            {
                Launcher.StartLocal(request, null);
            }
            return;
        }

        if (!ConfirmRun(item, profile, 0, null)) return;
        HidePanels();
        try { item.Source?.PrepareLaunch(); } // z. B. .rdp-Datei mit Benutzer/Gateway (ohne Kennwort)
        catch (Exception ex) { AppLog.Error("Start vorbereiten fehlgeschlagen", ex); }
        if (Launcher.StartLocal(BuildRequest(item, profile, mode, new Dictionary<string, object>()), null))
        {
            if (record) RecordRun(item, profile);
            AppLog.Info($"Start: {item.FullPath} · Profil {profile.Name}{(mode == ExecutionMode.Administrator ? " · Admin" : string.Empty)}");
        }
    }

    /// <summary>Text-Parameterwerte für die Vorschläge im Ausführen-Dialog merken (nie geheime Werte).</summary>
    public void RecordParameters(LaunchItem item, IReadOnlyDictionary<string, string?> raw)
    {
        foreach (var p in item.Parameters)
        {
            if (p.Kind is ParameterKind.Secure or ParameterKind.Switch or ParameterKind.Boolean or ParameterKind.Choice) continue;
            if (p.IsSecure) continue;
            if (raw.TryGetValue(p.Name, out var v) && !string.IsNullOrWhiteSpace(v) && v != p.DefaultValueLiteral)
                ParameterHistory.Add(Settings, item.FullPath, p.Name, v);
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
        CommitUserChange();
    }

    /// <summary>Eigener Eintrag mit den Startoptionen des Profils (Browser und Browserprofil, RDP-Anzeige, SSH-Optionen …).</summary>
    private LaunchItem WithProfileOptions(LaunchItem item, LaunchProfile profile)
    {
        if (profile.Options == null || item.Source == null || item.PlaceholdersResolved) return item;
        var result = DescribeCustom(item.Source.WithProfile(profile));
        return result;
    }

    /// <summary>Startmodus eines Profils: Administrator, anderer Benutzer (Anmeldung beim Start) oder aktuelles Konto.</summary>
    public static ExecutionMode ProfileMode(LaunchProfile p) =>
        p.RunAsAdmin ? ExecutionMode.Administrator : p.RunAsOtherUser ? ExecutionMode.OtherUser : ExecutionMode.CurrentUser;

    private ExecutionRequest BuildRequest(LaunchItem item, LaunchProfile profile, ExecutionMode mode, Dictionary<string, object> values) => new()
    {
        Item = item,
        Mode = mode,
        Engine = Launcher.EffectiveEngine(Requirements.EngineFor(item, profile.Engine)),
        NoExit = profile.NoExit,
        NoProfile = profile.NoProfile,
        ExecutionPolicy = Settings.ExecutionPolicy,
        Parameters = values,
        ExtraArguments = (item.LaunchArguments + " " + profile.Arguments).Trim(),
        ResolvedPath = item.LaunchTarget ?? PathResolver.ToUnc(item.FullPath)
    };

    public void OpenRunDialog(LaunchItem item, ExecutionMode mode, bool remote, LaunchProfile? profile = null)
    {
        item = Refresh(item);
        if (ResolvePlaceholders(item) is not { } resolvedItem) return;
        item = resolvedItem;
        HidePanels();
        var vm = new RunViewModel(this, item, mode, remote, profile);
        var w = new RunWindow(vm);
        w.Show();
        w.Activate();
    }

    /// <summary>Profil-Dialog aus dem Panel: arbeitet direkt auf den Einstellungen und speichert sofort.</summary>
    public void ShowProfiles(LaunchItem item)
    {
        HidePanels(force: true);
        item = Refresh(item);
        var vm = new ProfilesViewModel(this, item, Settings, saveImmediately: true);
        var w = new ProfilesWindow(vm, this);
        w.Show();
        w.Activate();
    }

    /// <summary>Wird vom Profil-Dialog nach dem Speichern aufgerufen.</summary>
    public void OnProfilesSaved()
    {
        CommitUserChange();
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

    public void ShowSettings(string? folderPath = null, string? section = null)
    {
        HidePanels(force: true);
        if (_settingsWindow != null)
        {
            if (folderPath != null) _settingsWindow.CurrentViewModel.FocusGroup(folderPath);
            if (section != null) _settingsWindow.CurrentViewModel.Section = section;
            _settingsWindow.Activate();
            return;
        }
        var vm = new SettingsViewModel(this, folderPath);
        if (section != null) vm.Section = section;
        _settingsWindow = new SettingsWindow(vm, this);
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
            Update.Title = $"Version {r.Version.ToString(3)} ist verfügbar";
            Update.Subtitle = $"Installiert: {UpdateService.CurrentVersionText} · aus GitHub Releases";
            Update.Available = true;
            if (!manual) _tray?.ShowBalloon("Update verfügbar", $"Power-Exec {r.Version.ToString(3)} kann installiert werden. Öffne das Panel.");
        }
        return r;
    }

    public async Task InstallUpdateAsync()
    {
        var r = Updates.Available;
        if (r == null) return;
        Update.Busy = true;
        Update.Subtitle = "Wird heruntergeladen …";
        string? error;
        try
        {
            error = await Updates.DownloadAndPrepareAsync(r, new Progress<double>(q => Update.Subtitle = $"Wird heruntergeladen … {q:P0}"));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        Update.Busy = false;
        if (error == null)
        {
            Exit();
            return;
        }
        Update.Subtitle = "Update fehlgeschlagen";
        if (MessageBox.Show(error + "\n\nRelease-Seite im Browser öffnen?", "Update", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            Launcher.OpenUrl(r.HtmlUrl);
    }

    /// <summary>Power-Exec wird gerade beendet (z. B. für ein Update).</summary>
    public bool IsExiting { get; private set; }

    public void Exit()
    {
        IsExiting = true;
        // Offene Fenster (Einstellungen, Dialoge) zuerst schließen, solange die Panels noch existieren
        _settingsWindow?.Close();
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
        foreach (var h in _hosts) h.Dispose();
        _hosts.Clear();
    }
}
