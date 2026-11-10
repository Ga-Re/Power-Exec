using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Search;
using PowerExec.Core.Updates;
using PowerExec.Infrastructure;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec;

/// <summary>
/// Zentrale Steuerung (App-Kern): Einstellungen, Panels, Aktionen, Updates. Ohne Oberflächen-Typen:
/// Fenster, Tray, Tastenkürzel und Design erreicht er über <see cref="IAppUi"/> (umgesetzt von der WPF-App, in Tests durch Attrappen).
/// </summary>
public partial class AppController : IDisposable
{
    /// <param name="createUi">Erzeugt die Oberfläche zu diesem Kern.</param>
    public AppController(Func<AppController, IAppUi> createUi)
    {
        Ui = createUi(this);
        // {{$Variablen}} gleich auflösbar – auch wenn Teile der Initialisierung übersprungen werden (Selbsttest)
        Placeholders.Resolver = name => VariableResolver.Resolve(name);
    }

    protected readonly List<AppPanel> _hosts = new();
    private ISettingsWindow? _settingsWindow;

    /// <summary>Die Oberfläche: Fenster, Panels, Tray, Tastenkürzel, Design.</summary>
    public IAppUi Ui { get; }

    public SettingsStore Store { get; protected set; } = new();
    public AppSettings Settings { get; protected set; } = new();
    public ItemRepository Repository { get; protected set; } = null!;
    public Launcher Launcher { get; protected set; } = null!;
    public UpdateService Updates { get; } = new();
    public HealthService Health { get; protected set; } = null!;
    /// <summary>Update-Hinweis für alle Panels und das Einstellungsfenster.</summary>
    public UpdateState Update { get; } = new();

    public IReadOnlyList<AppPanel> Hosts => _hosts;

    public void Start()
    {
        var firstStart = !Store.Exists;
        Settings = Store.Load();
        PowerExec.Core.Localization.L.Use(Settings.Language);
        if (firstStart)
        {
            // Der Installer legt den Autostart-Eintrag an; beim ersten Start übernehmen.
            Settings.StartWithWindows = AutostartService.IsEnabled();
            SaveSettings();
        }
        Ui.Initialize();
        Ui.ApplyTheme(Settings);
        Ui.SystemThemeChanged += OnSystemThemeChanged;
        SetupSnippetFolder();
        CustomEntry.TerminalAvailable = () => ProgramResolver.Resolve("wt.exe") != null;
        CustomEntry.RdpFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec", "Rdp");
        Placeholders.ClipboardText = () => Shell.GetClipboardText();
        // {{$Variablen}}: eingebaute und eigene; PowerShell-Variablen mit Zwischenspeicher im Hintergrund vorwärmen
        WarmVariables();

        Health = new HealthService(UiDispatcher);
        Health.Changed += RebuildPanels;
        Repository = new ItemRepository(Settings, UiDispatcher);
        Launcher = new Launcher(() => Settings, Dialogs, Ui.AskCredential);
        Repository.Changed += (_, _) =>
        {
            UpdateKnownFiles();
            _ = MaintainPathsAsync();
            RebuildPanels();
        };

        Ui.Hotkeys.Pressed += id => ShowPanelFromHotkey(_hosts.FirstOrDefault(h => h.HotkeyId == id));
        var hotkeyError = CreateHosts();
        StartFullscreenWatch();
        StartScheduler();
        StartCatalogs();
        StartMachineSync();

        Ui.CreateTray();

        AppLog.Info($"Power-Exec {UpdateService.CurrentVersionText} gestartet ({Settings.Folders.Count} Ordner)");
        Repository.LoadCache();
        Repository.SetupWatchers();
        _ = Repository.ScanAsync();
        AutostartService.Apply(Settings.StartWithWindows);

        // Neu in dieser Version: nach einem Update einmal im Panel zeigen
        var current = UpdateService.CurrentVersion;
        var lastSeen = Core.Updates.ReleaseNotes.Parse(Settings.LastSeenVersion);
        if (firstStart || current.Major + current.Minor + current.Build == 0) Settings.LastSeenVersion = current.ToString(3);
        else if (lastSeen == null || lastSeen < current) WhatsNew = Core.Updates.ReleaseNotes.Since(lastSeen, current).FirstOrDefault();
        if (WhatsNew == null && Settings.LastSeenVersion != current.ToString(3)) { Settings.LastSeenVersion = current.ToString(3); SaveSettings(); }

        if (Settings.Folders.Count == 0 && !Settings.WelcomeDone)
        {
            ShowWelcome();
        }
        else if (Settings.Folders.Count == 0)
        {
            Ui.Notify("Power-Exec läuft", "Lege in den Einstellungen fest, welche Ordner durchsucht werden.");
            ShowSettings(section: "groups");
        }
        else if (hotkeyError != null)
        {
            Ui.Notify("Tastenkürzel nicht verfügbar", hotkeyError);
        }

        if (Settings.CheckUpdatesOnStart)
            _ = CheckUpdatesAsync(manual: false, includePrereleases: Settings.IncludePrereleases);
    }

    public void SaveSettings()
    {
        try { Store.Save(Settings); }
        catch (Exception ex)
        {
            Dialogs.Warn("Die Einstellungen konnten nicht gespeichert werden:\n" + ex.Message, "Power-Exec");
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
        ClearUndo();
        var foldersChanged = !FoldersEqual(Settings, updated);
        var panelsChanged = PanelKey(Settings) != PanelKey(updated);
        var previousPanels = Settings.Panels;
        var themeChanged = AppearanceKey(Settings) != AppearanceKey(updated);
        var autostartChanged = Settings.StartWithWindows != updated.StartWithWindows;
        Settings = updated;
        WarmVariables();
        SaveSettings();
        if (themeChanged) Ui.ApplyTheme(Settings);
        if (autostartChanged) AutostartService.Apply(Settings.StartWithWindows);
        string? hotkeyError = null;
        // Panels möglichst im bestehenden Fenster anpassen (kein Neuaufbau, kein Flackern); neu erzeugt wird nur, wenn sich
        // das Design oder die Fensterart (Klarglas ↔ Glas) ändert oder Panels hinzukommen bzw. wegfallen
        if (themeChanged || _hosts.Count == 0 || (panelsChanged && !CanUpdateInPlace(previousPanels))) hotkeyError = CreateHosts();
        else if (panelsChanged) hotkeyError = UpdateHostsInPlace(previousPanels);
        if (foldersChanged) Repository.UpdateSettings(Settings);
        else
        {
            Repository.SetupWatchers();
            RebuildPanels();
        }
        return hotkeyError;
    }

    /// <summary>Anzeigewert einer {{$Variable}} (Vorschau im Panel).</summary>
    public string VariablePreview(string name) => VariableResolver.Preview(name);

    /// <summary>Eigene Variablen für den Eintrags-Editor (Einfügen, Warnung bei unbekannten).</summary>
    public IReadOnlyList<UserVariable> UserVariables => Settings.Variables;

    /// <summary>Löst {{$Variablen}} auf (eingebaute und eigene, mit Zwischenspeicher für PowerShell-Befehle).</summary>
    public VariableResolver VariableResolver => _variableResolver ??= new VariableResolver(() => Settings,
        cmd => VariableCommandRunner.Run(cmd, Settings.DefaultEngine), SecretProtector.Unprotect, () => Shell.GetClipboardText());
    private VariableResolver? _variableResolver;

    /// <summary>PowerShell-Variablen mit Zwischenspeicher bzw. „je Sitzung“ im Hintergrund ermitteln.</summary>
    public void WarmVariables()
    {
        if (!Settings.Variables.Any(v => v.Kind == VariableKind.PowerShell && v.Refresh != VariableRefresh.EveryUse)) return;
        var resolver = VariableResolver;
        _ = Task.Run(resolver.Warm);
    }

    /// <summary>Design, Akzent, Fensterhintergrund und Größe: Änderung erzeugt Panels und Farben neu.</summary>
    private static string AppearanceKey(AppSettings s) => $"{s.Theme}|{s.UseWindowsAccent}|{s.UiScalePercent}";

    /// <summary>Windows-Design, Akzentfarbe oder Kontrastdesign geändert: Farben neu anwenden.</summary>
    private void OnSystemThemeChanged()
    {
        var before = Ui.ThemeStateKey;
        Ui.ApplyTheme(Settings);
        if (before != Ui.ThemeStateKey) CreateHosts();
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
    protected string? CreateHosts()
    {
        // Offene Panels bleiben nach dem Neuaufbau offen – man soll sehen, was die geänderte Einstellung bewirkt.
        // Die alten Fenster schließen erst, wenn die neuen stehen: so verschwindet das Panel nicht kurz.
        var wasOpen = _hosts.Select(h => h.View.OpenState).ToList();
        var selected = _hosts.Select(h => h.ViewModel.SelectedKey).ToList();
        var old = _hosts.ToList();
        _hosts.Clear();
        Ui.Hotkeys.UnregisterAll();
        if (Settings.Panels.Count == 0) Settings.Panels.Add(new PanelConfig { Hotkey = PanelConfig.DefaultHotkey });
        var errors = new List<string>();
        int id = 0x5045; // "PE"
        foreach (var config in Settings.Panels)
        {
            var host = new AppPanel(config, id++);
            host.ViewModel = new PanelViewModel(this, host);
            host.View = Ui.CreatePanelView(host);
            _hosts.Add(host);
            var err = Ui.Hotkeys.Register(host.HotkeyId, config.Hotkey);
            if (err != null) errors.Add($"{config.Name}: {err}");
        }
        for (var i = 0; i < Math.Min(selected.Count, _hosts.Count); i++) _hosts[i].ViewModel.PreselectKey(selected[i]);
        foreach (var h in _hosts) h.ViewModel.Rebuild();
        var restored = false;
        for (var i = 0; i < Math.Min(wasOpen.Count, _hosts.Count); i++)
            if (wasOpen[i] is { } state) { _hosts[i].View.RestoreOpenState(state); restored = true; }
        if (!restored) DisposePanels(old);
        else
        {
            // Die alten Fenster bleiben kurz darunter liegen, bis die neuen gezeichnet sind – sonst blitzt das Panel auf
            IUiTimer? later = null;
            later = Ui.Dispatcher.CreateTimer(TimeSpan.FromMilliseconds(250), () => { later!.Stop(); DisposePanels(old); });
            later.Start();
        }
        return errors.Count == 0 ? null : string.Join("\n", errors);
    }

    private bool CanUpdateInPlace(IReadOnlyList<PanelConfig> previous) =>
        _hosts.Count == Settings.Panels.Count && previous.Count == Settings.Panels.Count &&
        _hosts.Select((h, i) => PanelConfig.FitsSameWindow(h.Config, Settings.Panels[i])).All(ok => ok);

    /// <summary>Neue Panel-Einstellungen an die bestehenden Panels geben; Tastenkürzel neu anmelden.</summary>
    /// <returns>Fehlermeldungen zu Tastenkürzeln oder null.</returns>
    private string? UpdateHostsInPlace(IReadOnlyList<PanelConfig> previous)
    {
        Ui.Hotkeys.UnregisterAll();
        var errors = new List<string>();
        for (var i = 0; i < _hosts.Count; i++)
        {
            var host = _hosts[i];
            var before = previous.Count > i ? previous[i] : host.Config;
            host.ReplaceConfig(Settings.Panels[i]);
            host.View.ApplyConfig(before);
            host.ViewModel.ConfigReplaced();
            var err = Ui.Hotkeys.Register(host.HotkeyId, host.Config.Hotkey);
            if (err != null) errors.Add($"{host.Config.Name}: {err}");
        }
        return errors.Count == 0 ? null : string.Join("\n", errors);
    }

    protected void DisposePanels()
    {
        DisposePanels(_hosts);
        _hosts.Clear();
    }

    private static void DisposePanels(IEnumerable<AppPanel> panels)
    {
        foreach (var h in panels)
        {
            h.View.Dispose();
            h.ViewModel.Detach();
        }
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
        foreach (var h in _hosts) h.View.HideAfterAction(force);
    }

    private IUiTimer? _fullscreenTimer;

    /// <summary>Prüft jede Sekunde, ob das Vordergrundfenster einen Monitor vollständig bedeckt.</summary>
    private void StartFullscreenWatch()
    {
        _fullscreenTimer ??= UiDispatcher.CreateTimer(TimeSpan.FromSeconds(1), OnFullscreenTick);
        _fullscreenTimer.Start();
    }

    private void OnFullscreenTick()
    {
        try
        {
            var probe = Settings.HideHotZoneInFullscreen ? FullscreenProbe.Current() : null;
            foreach (var h in _hosts) h.View.ApplyFullscreen(probe);
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
        ClearUndo();
        SaveSettings();
        AppLog.Info($"Arbeitsbereich gewechselt: {Settings.ActiveWorkspace}");
        Repository.UpdateSettings(Settings);
        RebuildPanels();
        _settingsWindow?.Close(); // Einstellungen zeigen sonst noch den alten Arbeitsbereich
    }

    /// <summary>Tastenkürzel oder Tray: Panel öffnen bzw. wieder schließen.</summary>
    public void ShowPanelFromHotkey(AppPanel? host = null, bool forcePanel = false)
    {
        host ??= _hosts.FirstOrDefault();
        if (host == null) return;
        if (host.Config.HotkeyOpensSpotlight && !forcePanel)
        {
            ToggleSpotlight(host);
            return;
        }
        var w = host.View;
        if (w.IsOpen && w.OpenedViaHotkey)
        {
            w.Hide();
            return;
        }
        w.ShowViaHotkey();
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
    private LaunchItem? ResolvePlaceholders(LaunchItem item, IReadOnlyDictionary<string, string>? preset = null)
    {
        if (item.PlaceholdersResolved || item.Source is not { } entry || !entry.NeedsResolving) return item;
        // {{$Variablen}} müssen angelegt sein (normale {{Platzhalter}} werden abgefragt)
        var unknown = Variables.Unknown(Settings, entry.Target, entry.Arguments, entry.Code, entry.User, entry.RemoteCommand, entry.WorkingDirectory);
        if (unknown.Count > 0)
        {
            Dialogs.Warn($"„{item.DisplayName}“ verwendet {string.Join(", ", unknown.Select(n => "{{" + n + "}}"))} – diese Variable gibt es nicht. " +
                         "Bitte unter Einstellungen › Inhalte › Variablen anlegen oder den Eintrag korrigieren.", "Variable fehlt");
            return null;
        }
        IReadOnlyDictionary<string, string>? values = preset ?? new Dictionary<string, string>();
        if (preset == null && entry.Placeholders.Count > 0) // eingebaute Variablen ({{$Date}} …) brauchen keine Abfrage
            values = WithDialog(() => Dialogs.AskPlaceholders(item.DisplayName, entry.Placeholders,
                name => ParameterHistory.Get(Settings, entry.Key, name)));
        if (values == null) return null;
        foreach (var kv in values) ParameterHistory.Add(Settings, entry.Key, kv.Key, kv.Value);
        SaveSettings();
        CustomEntry resolved;
        try { resolved = entry.WithValues(values); }
        catch (Exception ex)
        {
            Dialogs.Warn($"Eine Variable von „{item.DisplayName}“ ließ sich nicht ermitteln: {ex.Message}", "Variable");
            return null;
        }
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
            Shell.CopyToClipboard(resolved.Source.Code, resolved.Source.CopyAsHtml);
            if (record) RecordRun(item, ProfileResolver.GetDefault(Settings, item));
            HidePanels();
            if (resolved.Source.PasteAfterCopy) _ = PasteAsync(item.DisplayName);
            else Ui.Notify("In die Zwischenablage kopiert", item.DisplayName);
        }
        catch (Exception ex)
        {
            Dialogs.Warn("Die Zwischenablage ist gerade belegt: " + ex.Message, "Power-Exec");
        }
    }

    private async Task PasteAsync(string name)
    {
        if (!await Ui.PasteIntoPreviousAsync())
            Ui.Notify("In die Zwischenablage kopiert", $"{name}: Kein Zielfenster gefunden – mit Strg+V einfügen.");
    }

    // ---- Eigene Gruppen ---------------------------------------------------

    private int _dialogs;

    /// <summary>Solange ein Dialog aus dem Panel offen ist, fährt das Panel nicht ein.</summary>
    public bool IsDialogOpen => _dialogs > 0;

    /// <summary>Ja/Nein-Frage über dem offenen Panel (das Panel liegt sonst darüber).</summary>
    private bool Confirm(string text, string title)
    {
        var owner = _hosts.FirstOrDefault(h => h.View.IsOpen)?.View.Owner;
        return WithDialog(() => Dialogs.Confirm(text, title, owner: owner, defaultNo: true));
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
    /// <summary>
    /// Dialog „Neue Gruppe“ (Name, Symbol/Bild, Ordner, Farbe, Darstellung). Mit <paramref name="parent"/> entsteht eine
    /// Untergruppe (Rechtsklick auf eine Gruppe), sonst eine Gruppe auf oberster Ebene am Ende. Mit Ordner: smarte Gruppe.
    /// </summary>
    public string? CreateGroup(string? parent = null)
    {
        var heading = parent == null ? "Neue Gruppe" : $"Neue Untergruppe in „{GroupManager.LeafOf(parent)}“";
        var spec = WithDialog(() => Dialogs.AskNewGroup(heading, parent == null ? "Neue Gruppe" : "Neue Untergruppe",
            n => GroupNameError(n, parent), f => NewGroup.FolderError(Settings.Folders, f)));
        if (spec == null) return null;
        var path = NewGroup.Create(Settings, spec, parent);
        if (parent != null)
            Settings.CollapsedGroups.RemoveAll(k => k.Equals(GroupManager.GroupKey(parent), StringComparison.OrdinalIgnoreCase));
        CommitUserChange();
        if (spec.IsSmart) Repository.UpdateSettings(Settings); // Ordner einlesen und überwachen
        return path;
    }

    public void RenameGroup(string path)
    {
        var parent = GroupManager.ParentOf(path);
        var newName = WithDialog(() => Dialogs.Ask("Gruppe umbenennen", string.Empty, GroupManager.LeafOf(path), "Umbenennen", n => GroupNameError(n, parent, path)));
        if (newName == null || newName == GroupManager.LeafOf(path)) return;
        var before = SettingsStore.Clone(Settings);
        if (GroupManager.Rename(Settings, path, newName))
        {
            _undo.Push(before, "Gruppe umbenannt");
            CommitUserChange();
            ShowUndoToast($"Gruppe in „{GroupManager.CleanName(newName)}“ umbenannt");
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
        // Mit Rückgängig statt Rückfrage; nur bei Untergruppen oder Ordnern zusätzlich nachfragen
        if ((subs > 0 || folders.Count > 0) && !Confirm(text, "Gruppe löschen")) return;
        RememberForUndo($"Gruppe „{name}“ gelöscht");
        GroupManager.Delete(Settings, path);
        CommitUserChange();
        ShowUndoToast($"Gruppe „{name}“ gelöscht");
    }

    public void MoveGroup(string path, int delta)
    {
        RememberForUndo("Gruppe verschoben");
        GroupManager.Move(Settings, path, delta);
        CommitUserChange();
    }

    /// <summary>Drag &amp; Drop einer Gruppe auf eine andere: davor, dahinter oder hinein (Untergruppe).</summary>
    public void MoveGroupTo(string path, string target, GroupDropPosition position)
    {
        var before = SettingsStore.Clone(Settings);
        var result = GroupManager.MoveGroup(Settings, path, target, position);
        if (result == null)
        {
            Ui.Notify("Verschieben nicht möglich", "Am Ziel gibt es bereits eine Gruppe mit diesem Namen, oder die Gruppe würde in ihre eigene Untergruppe verschoben.");
            return;
        }
        if (position == GroupDropPosition.Into)
            Settings.CollapsedGroups.RemoveAll(k => k.Equals(GroupManager.GroupKey(target), StringComparison.OrdinalIgnoreCase));
        _undo.Push(before, "Gruppe verschoben");
        CommitUserChange();
        ShowUndoToast(position == GroupDropPosition.Into
            ? $"„{GroupManager.LeafOf(path)}“ nach „{GroupManager.LeafOf(target)}“ verschoben"
            : $"„{GroupManager.LeafOf(path)}“ verschoben");
    }

    /// <summary>Oberste Ebene umordnen – auch feste Gruppen (Zuletzt ausgeführt, Favoriten, Eigene Einträge).</summary>
    public bool MoveTopLevel(string key, string anchor, bool after)
    {
        var before = SettingsStore.Clone(Settings);
        if (FixedGroups.MoveTopLevel(Settings, key, anchor, after) is not { } moved) return false;
        _undo.Push(before, "Gruppe verschoben");
        CommitUserChange();
        ShowUndoToast($"„{(FixedGroups.IsFixed(moved) ? FixedGroups.Title(moved) : GroupManager.LeafOf(moved))}“ verschoben");
        return true;
    }

    /// <summary>Gruppe im aktiven Arbeitsbereich ausblenden (wieder einblenden: Einstellungen › Arbeitsbereiche).</summary>
    public void HideGroupInWorkspace(string groupPath)
    {
        RememberForUndo("Gruppe ausgeblendet");
        WorkspaceManager.SetGroupHidden(WorkspaceManager.Active(Settings), groupPath, true);
        CommitUserChange();
        ShowUndoToast($"„{GroupManager.LeafOf(groupPath)}“ im Arbeitsbereich „{Settings.ActiveWorkspace}“ ausgeblendet");
    }

    /// <summary>Eintrag in eine Gruppe schieben (leer = aus der Gruppe nehmen), optional vor/hinter einen anderen Eintrag.</summary>
    public void AssignGroup(string path, string? group, string? targetKey = null, string? beforeOrAfterPath = null, bool after = false, IReadOnlyList<string>? targetOrder = null)
    {
        RememberForUndo("Eintrag verschoben");
        GroupManager.Assign(Settings, path, group);
        if (targetKey != null && beforeOrAfterPath != null && targetOrder != null)
        {
            var order = targetOrder.Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)).Append(path).ToList();
            Settings.CustomOrder[targetKey] = ItemOrdering.Move(order, path, beforeOrAfterPath, after);
        }
        CommitUserChange();
        var label = FindItem(path) is { } moved ? DisplayNameOf(moved) : "Eintrag";
        ShowUndoToast(group == null ? $"„{label}“ aus der Gruppe genommen" : $"„{label}“ nach „{GroupManager.LeafOf(group)}“ verschoben");
    }

    /// <summary>Aus dem Explorer oder Browser gezogene Ziele übernehmen.</summary>
    public void AddDropped(IReadOnlyList<DroppedTarget> targets, string? group, bool askForFolders = true)
    {
        if (targets.Count == 0) return;
        // Ordner: als smarte Gruppe (Inhalt einlesen) oder als Link (öffnet den Ordner)?
        var folders = askForFolders ? targets.Where(t => t.Kind == CustomEntryKind.Folder && Directory.Exists(t.Target)).ToList() : new List<DroppedTarget>();
        if (folders.Count > 0)
        {
            var asGroups = new List<DroppedTarget>();
            int? all = null;
            foreach (var f in folders)
            {
                var forAll = false;
                var choice = all ?? AskFolderDrop(f, folders.Count > 1, out forAll);
                if (choice < 0) return; // Abbruch: gar nichts hinzufügen
                if (all == null && forAll) all = choice;
                if (choice == 0) asGroups.Add(f);
            }
            foreach (var f in asGroups) AddFolderAsGroup(f.Target, group);
            targets = targets.Except(asGroups).ToList();
            if (targets.Count == 0) return;
        }
        var scanned = new HashSet<string>(Repository.Items.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
        var keys = DropClassifier.AddToSettings(Settings, targets, group, scanned.Contains);
        CommitUserChange();
        AppLog.Info($"Per Drag & Drop hinzugefügt{(group != null ? " (Gruppe " + group + ")" : string.Empty)}: {string.Join(", ", targets.Select(t => t.Target))}");
        if (group == null && keys.Count > 0 && Hosts.Any(h => !h.Config.ShowsFolder(PanelConfig.CustomEntriesKey)))
            Ui.Notify("Hinzugefügt", "Die Einträge stehen unter „Eigene Einträge“. Panels mit eigener Inhaltsauswahl zeigen sie nur, wenn „Eigene Einträge“ angehakt ist.");
    }

    private int AskFolderDrop(DroppedTarget folder, bool several, out bool forAll)
    {
        var (choice, all) = WithDialog(() => Dialogs.Choose($"Ordner „{folder.Name}“ hinzufügen", "Wie soll der Ordner im Panel erscheinen?",
            ChoiceOption.FolderDrop, several ? "Für alle gezogenen Ordner übernehmen" : null));
        forAll = all;
        return choice;
    }

    /// <summary>
    /// Gezogenen Ordner als smarte Gruppe anlegen (in der Gruppe, auf die er gezogen wurde): über den Dialog
    /// „Neue Gruppe“ mit vorausgefülltem Ordner und Namen – so lassen sich gleich Farbe, Symbol und Darstellung wählen.
    /// </summary>
    private void AddFolderAsGroup(string folderPath, string? parent)
    {
        if (NewGroup.FolderError(Settings.Folders, folderPath) is { } error)
        {
            Ui.Notify("Ordner schon vorhanden", error, warning: true);
            return;
        }
        parent = string.IsNullOrEmpty(parent) ? null : parent;
        var name = NewGroup.NameFromFolder(folderPath);
        for (int i = 2; GroupManager.Exists(Settings, parent == null ? name : parent + GroupManager.Separator + name); i++) name = $"{NewGroup.NameFromFolder(folderPath)} {i}";
        var heading = parent == null ? "Ordner als Gruppe hinzufügen" : $"Ordner als Untergruppe in „{GroupManager.LeafOf(parent)}“";
        var spec = WithDialog(() => Dialogs.AskNewGroup(heading, name, n => GroupNameError(n, parent),
            f => NewGroup.FolderError(Settings.Folders, f), folderPath));
        if (spec == null) return;
        RememberForUndo("Ordner als Gruppe hinzugefügt");
        var path = NewGroup.Create(Settings, spec, parent);
        SaveSettings();
        Repository.UpdateSettings(Settings);
        CommitUserChange();
        ShowUndoToast($"Ordner als Gruppe „{GroupManager.LeafOf(path)}“ hinzugefügt");
        AppLog.Info($"Ordner per Drag & Drop als smarte Gruppe: {folderPath} → {path}");
    }

    /// <summary>Neuer Eintrag aus einem Menü: Art-Schlüssel („Url“, „TextSnippet“ …) oder null = Art im Dialog wählen.</summary>
    public void NewCustomEntry(string? kindKey, string? group)
    {
        var template = CustomEntry.FromKindKey(kindKey);
        var result = WithDialog(() => Ui.EditEntry(Settings, template, isNew: true, group, null, null));
        HandleEditorResult(result, group);
    }

    /// <summary>Eintrag vorbelegt anlegen (Zwischenablage, Vorlage, Duplikat) – der Eintragsdialog zeigt ihn zum Bestätigen.</summary>
    public bool NewCustomEntry(CustomEntry template, string? group, IEnumerable<string>? tags = null)
    {
        var result = WithDialog(() => Ui.EditEntry(Settings, template, isNew: true, group, tags, null));
        return HandleEditorResult(result, group);
    }

    /// <summary>Ergebnis des Eintragsdialogs: gespeichert, oder Vorlagen / Dateien / smarte Gruppe gewählt.</summary>
    private bool HandleEditorResult(EntryEditorResult result, string? group)
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
        var files = WithDialog(() => Dialogs.OpenFiles(
            "Scripte und Programme (*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc)|*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc|Alle Dateien (*.*)|*.*",
            group != null ? $"Dateien zur Gruppe „{GroupManager.LeafOf(group)}“ hinzufügen" : "Dateien hinzufügen"));
        if (files.Count > 0)
            AddDropped(files.Select(f => new DroppedTarget(CustomEntryKind.File, f, Path.GetFileName(f))).ToList(), group);
    }

    /// <summary>Ordner als smarte Gruppe anlegen (aus dem Eintragsdialog).</summary>
    public void CreateSmartGroupFor(string folderPath, string? parent)
    {
        if (Settings.Folders.FirstOrDefault(f => f.Path.Equals(folderPath, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            Dialogs.Inform($"Dieser Ordner gehört bereits zur Gruppe „{existing.Group}“.", "Smarte Gruppe");
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
    public void EditCustomEntry(LaunchItem item)
    {
        if (item.Source is { Kind: CustomEntryKind.Workflow } wf) EditWorkflow(wf);
        else ShowProfiles(item);
    }

    /// <summary>Kopie eines eigenen Eintrags anlegen (gleiche Gruppe, Schlagworte, Profile), Dialog zum Anpassen.</summary>
    public void DuplicateCustomEntry(LaunchItem item)
    {
        var entry = Settings.CustomEntries.FirstOrDefault(c => c.Key.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        if (entry == null || entry.Kind == CustomEntryKind.File) return;
        var copy = entry.Duplicate();
        var key = WithDialog(() => Ui.EditEntry(Settings, copy, isNew: true,
            GroupManager.GroupOf(Settings, entry.Key), Tags.Of(Settings, entry.Key), null).SavedKey);
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
            var files = Shell.GetClipboardFiles();
            if (files.Count > 0)
            {
                AddDropped(files.Select(f => DropClassifier.Classify(f, Directory.Exists, p => { try { return File.ReadAllText(p); } catch { return null; } })).ToList(), group);
                return;
            }
            text = Shell.GetClipboardText();
        }
        catch { }
        if (string.IsNullOrWhiteSpace(text))
        {
            Ui.Notify("Zwischenablage leer", "Kopiere eine Adresse, einen Pfad, einen Rechnernamen oder Code und versuche es erneut.");
            return;
        }
        NewCustomEntry(ClipboardClassifier.Suggest(text, Directory.Exists, File.Exists), group);
    }

    /// <summary>Vorlagen-Katalog: ausgewählte Vorlagen in die Gruppe übernehmen.</summary>
    public void AddFromCatalog(string? group)
    {
        var chosen = WithDialog(() => Dialogs.PickCatalogTemplates());
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
        foreach (var h in Hosts) h.ViewModel.GroupLayoutsChanged();
        SettingsChangedOutside?.Invoke();
    }

    public void SetGroupItemSize(string groupPath, ItemSize? size)
    {
        GroupManager.SetItemSize(Settings, groupPath, size);
        SaveSettings();
        foreach (var h in Hosts) h.ViewModel.GroupLayoutsChanged();
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
        var text = WithDialog(() => Dialogs.PickColor($"Farbe für „{GroupManager.LeafOf(groupPath)}“", current));
        if (text != null) SetGroupColor(groupPath, text);
    }

    public void ChooseGroupIcon(string groupPath)
    {
        var current = GroupManager.IconOf(Settings, groupPath);
        var picked = WithDialog(() => Dialogs.PickIcon(current, $"Symbol für „{GroupManager.LeafOf(groupPath)}“"));
        if (picked == null) return;
        GroupManager.SetIcon(Settings, groupPath, picked.Value);
        CommitUserChange();
    }

    /// <summary>Eigene Einträge als Datei speichern (z. B. eine Gruppe für Kollegen).</summary>
    public void ExportEntries(IReadOnlyList<CustomEntry> entries, string? rootGroup, object? owner = null, AppSettings? source = null)
    {
        if (entries.Count == 0)
        {
            Dialogs.Inform("Es sind keine eigenen Einträge ausgewählt.", "Exportieren", owner);
            return;
        }
        var fileName = WithDialog(() => Dialogs.SaveFile(EntryShare.FileFilter,
            (rootGroup != null ? GroupManager.LeafOf(rootGroup) : "Eigene Einträge") + ".pxentries.json", owner: owner));
        if (fileName == null) return;
        try
        {
            File.WriteAllText(fileName, EntryShare.Export(source ?? Settings, entries, rootGroup));
            AppLog.Info($"{entries.Count} eigene Einträge exportiert: {fileName}");
            Ui.Notify("Exportiert", $"{entries.Count} {(entries.Count == 1 ? "Eintrag" : "Einträge")} gespeichert.");
        }
        catch (Exception ex)
        {
            Dialogs.Warn(ex.Message, "Exportieren", owner);
        }
    }

    public void ExportGroup(string groupPath) =>
        ExportEntries(Settings.CustomEntries.Where(c => GroupManager.GroupOf(Settings, c.Key) is { Length: > 0 } g && GroupManager.IsSameOrBelow(g, groupPath)).ToList(), groupPath);

    /// <returns>true, wenn etwas importiert wurde.</returns>
    public bool ImportEntries(string? intoGroup = null, object? owner = null)
    {
        var fileName = WithDialog(() => Dialogs.OpenFile(EntryShare.FileFilter + "|Alle Dateien (*.*)|*.*", owner: owner));
        if (fileName == null) return false;
        try
        {
            var file = EntryShare.Parse(File.ReadAllText(fileName));
            var (added, updated) = EntryShare.Import(Settings, file, intoGroup);
            CommitUserChange();
            AppLog.Info($"Eigene Einträge importiert ({added} neu, {updated} aktualisiert): {fileName}");
            Dialogs.Inform($"{added} neue und {updated} aktualisierte Einträge übernommen.", "Importieren", owner);
            return added + updated > 0;
        }
        catch (Exception ex)
        {
            Dialogs.Warn("Die Datei konnte nicht gelesen werden:\n" + ex.Message, "Importieren", owner);
            return false;
        }
    }

    public void RemoveCustomEntry(LaunchItem item)
    {
        var entry = Settings.CustomEntries.FirstOrDefault(c => c.Key.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return;
        var name = Settings.FindEntry(item.FullPath)?.DisplayName is { Length: > 0 } dn ? dn : item.DisplayName;
        // Keine Rückfrage mehr: der Hinweis bietet „Rückgängig“
        RememberForUndo($"„{name}“ entfernt");
        Settings.CustomEntries.Remove(entry);
        Settings.Entries.RemoveAll(e => e.Path.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
        Settings.Favorites.RemoveAll(p => p.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
        Settings.RecentRuns.RemoveAll(r => r.Path.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
        RunStats.Remove(Settings, entry.Key);
        CommitUserChange();
        ShowUndoToast($"„{name}“ entfernt" + (entry.Kind is CustomEntryKind.File or CustomEntryKind.Folder ? " (Datei bzw. Ordner bleibt erhalten)" : string.Empty));
    }

    public bool IsFavorite(string path) => Settings.Favorites.Contains(path, StringComparer.OrdinalIgnoreCase);

    public void ToggleFavorite(LaunchItem item)
    {
        var wasFavorite = IsFavorite(item.FullPath);
        if (wasFavorite)
        {
            RememberForUndo("Favorit entfernt");
            Settings.Favorites.RemoveAll(p => p.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        }
        else
            Settings.Favorites.Add(item.FullPath);
        CommitUserChange();
        if (wasFavorite) ShowUndoToast($"„{DisplayNameOf(item)}“ ist kein Favorit mehr");
    }

    public FolderConfig? FolderOf(LaunchItem item) =>
        Settings.Folders.FirstOrDefault(f => f.Path.Equals(item.RootFolder, StringComparison.OrdinalIgnoreCase));

    /// <summary>Blendet einen Eintrag aus (entfernt den Haken in den Ordner-Einstellungen).</summary>
    public void HideItem(LaunchItem item)
    {
        var folder = FolderOf(item);
        if (folder == null) return;
        RememberForUndo($"„{item.FileName}“ ausgeblendet");
        FolderVisibility.SetVisible(folder, FolderVisibility.RelativePath(folder, item.FullPath), false);
        Settings.Favorites.RemoveAll(p => p.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        CommitUserChange();
        ShowUndoToast($"„{DisplayNameOf(item)}“ ausgeblendet");
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
                History.Rename(m.OldPath, m.NewPath);
                AppLog.Info($"Umbenannt/verschoben erkannt: {m.OldPath} → {m.NewPath} (Profile und Favorit übernommen)");
            }
            Settings.Fingerprints = snapshot.Fingerprints;
            SaveSettings();
            if (moves.Count > 0)
            {
                RebuildPanels();
                Ui.Notify("Datei wiedergefunden", moves.Count == 1
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
        if (item.Source is { Kind: CustomEntryKind.Workflow } workflow) { RunWorkflowEntry(item, workflow); return; }
        // „Nach Profil fragen“: Profil (und Platzhalter-Werte) in einem Fenster, nur beim normalen Start mit mehreren Profilen
        IReadOnlyDictionary<string, string>? presetValues = null;
        if (profile == null && record && ProfileResolver.AsksProfile(Settings, item))
        {
            var current = Refresh(item);
            var placeholders = !current.PlaceholdersResolved && current.Source is { NeedsResolving: true, CopiesToClipboard: false } src
                ? src.Placeholders : (IReadOnlyList<Placeholder>)Array.Empty<Placeholder>();
            var key = current.Source?.Key ?? item.FullPath;
            var answer = WithDialog(() => Dialogs.AskProfile(item.DisplayName, placeholders,
                name => ParameterHistory.Get(Settings, key, name), ProfileResolver.GetProfiles(Settings, item), ProfileResolver.GetDefault(Settings, item)));
            if (answer is not { } a) return;
            profile = a.Profile;
            if (placeholders.Count > 0) presetValues = a.Values;
        }
        profile ??= ProfileResolver.GetDefault(Settings, item);
        item = WithProfileOptions(record ? Refresh(item) : item, profile); // Parameter könnten sich geändert haben; Browser, Anzeige … des Profils
        if (item.Source?.CopiesToClipboard == true)
        {
            CopySnippet(item, record);
            return;
        }
        if (ResolvePlaceholders(item, presetValues) is not { } resolvedItem) return;
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
            var raw = ProfileResolver.InitialValues(profile, item);
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
                }, null, raw, profile);
            }
            else
            {
                var origin = HistoryOrigin;
                StartLocalTracked(item, request, () => BeginHistory(item, profile, mode, request.Engine, null, raw, origin), null);
            }
            return;
        }

        if (!ConfirmRun(item, profile, 0, null)) return;
        HidePanels();
        try { item.Source?.PrepareLaunch(); } // z. B. .rdp-Datei mit Benutzer/Gateway (ohne Kennwort)
        catch (Exception ex) { AppLog.Error("Start vorbereiten fehlgeschlagen", ex); }
        var plainOrigin = HistoryOrigin;
        if (StartLocalTracked(item, BuildRequest(item, profile, mode, new Dictionary<string, object>()),
                () => BeginHistory(item, profile, mode, PsEngine.WindowsPowerShell, null, null, plainOrigin), null))
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
    public bool ConfirmRun(LaunchItem item, LaunchProfile profile, int targetCount, object? owner)
    {
        if (!profile.ConfirmBeforeRun) return true;
        var name = Settings.FindEntry(item.FullPath)?.DisplayName is { Length: > 0 } dn ? dn : item.DisplayName;
        var where = targetCount > 0 ? $" auf {targetCount} {(targetCount == 1 ? "Rechner" : "Rechnern")}" : string.Empty;
        var text = $"„{name}“ mit dem Profil „{profile.Name}“{where} wirklich ausführen?" +
                   (profile.RunAsAdmin ? "\n\nDer Start erfolgt mit Administratorrechten." : string.Empty);
        return Dialogs.Confirm(text, "Ausführen bestätigen", owner: owner, defaultNo: true);
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
            Dialogs.Warn($"{item.FileName} konnte nicht entsperrt werden (fehlende Schreibrechte?).", "Entsperren");
        }
    }

    /// <summary>Für „Zuletzt ausgeführt“ merken.</summary>
    public void RecordRun(LaunchItem item, LaunchProfile profile)
    {
        RecentRunList.Add(Settings, item.FullPath, profile.IsImplicit ? "Standard" : profile.Name);
        RunStats.Record(Settings, item.FullPath);
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
    public static ExecutionMode ProfileMode(LaunchProfile p) => ProfileResolver.ModeOf(p);

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
        Ui.ShowRun(new RunViewModel(this, item, mode, remote, profile));
    }

    /// <summary>Profil-Dialog aus dem Panel: arbeitet direkt auf den Einstellungen und speichert sofort.</summary>
    public void ShowProfiles(LaunchItem item)
    {
        if (item.Source is { Kind: CustomEntryKind.Workflow } wf) { EditWorkflow(wf); return; }
        HidePanels(force: true);
        item = Refresh(item);
        Ui.ShowProfiles(new ProfilesViewModel(this, item, Settings, saveImmediately: true));
    }

    /// <summary>Wird vom Profil-Dialog nach dem Speichern aufgerufen.</summary>
    public void OnProfilesSaved()
    {
        CommitUserChange();
    }

    /// <summary>Startet die Remote-/Massenausführung und öffnet die Ergebnisliste.</summary>
    /// <param name="raw">Parameterwerte als Text für den Verlauf (null = aus den typisierten Werten).</param>
    public bool StartRemote(ExecutionRequest request, object? owner, IReadOnlyDictionary<string, string?>? raw = null, LaunchProfile? profile = null)
    {
        RunCredential? cred = null;
        if (request.Mode == ExecutionMode.OtherUser)
        {
            cred = Ui.AskCredential(owner, request.Item.DisplayName, remote: true);
            if (cred == null) return false;
        }
        var account = cred?.Account ?? $"{Environment.UserDomainName}\\{Environment.UserName}";
        var vm = new ResultsViewModel(this, request, account);
        var history = BeginHistory(request.Item, profile, request.Mode, request.Engine, request.Targets,
            raw ?? RunHistory.RawFrom(request.Parameters), HistoryOrigin);
        IAppWindow? w = null;
        var activity = Activities.TrackRemote(vm, () =>
        {
            HidePanels();
            w?.BringToFront();
        });
        activity.Completed += () => CompleteHistory(history,
            vm.RunnerError != null ? HistoryResult.Error : vm.ErrorCount + vm.UnreachableCount > 0 ? HistoryResult.Warning : HistoryResult.Success,
            activity.Detail, null,
            vm.Rows.Where(r => r.Status is RemoteStatus.Error or RemoteStatus.Unreachable).Select(r => r.Computer), vm.LogFile);
        vm.Finished += () =>
        {
            if (!Settings.NotifyOnFinish || w?.IsActive == true) return;
            bool problems = vm.ErrorCount + vm.UnreachableCount > 0 || vm.RunnerError != null;
            Ui.Notify($"{request.Item.DisplayName} ist fertig",
                vm.RunnerError ?? $"{vm.SuccessCount} erfolgreich · {vm.ErrorCount} Fehler · {vm.UnreachableCount} nicht erreichbar",
                () =>
                {
                    if (w?.IsOpen == true) w.BringToFront();
                },
                warning: problems);
        };
        w = Ui.ShowResults(vm);
        vm.Start(cred);
        return true;
    }

    public void ShowSettings(string? folderPath = null, string? section = null)
    {
        HidePanels(force: true);
        if (_settingsWindow != null)
        {
            if (folderPath != null) _settingsWindow.ViewModel.FocusGroup(folderPath);
            if (section != null) _settingsWindow.ViewModel.Section = section;
            _settingsWindow.BringToFront();
            return;
        }
        var vm = new SettingsViewModel(this, folderPath);
        if (section != null) vm.Section = section;
        var w = Ui.ShowSettings(vm);
        _settingsWindow = w;
        w.Closed += () => { if (_settingsWindow == w) _settingsWindow = null; };
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
            if (!manual) Ui.Notify("Update verfügbar", $"Power-Exec {r.Version.ToString(3)} kann installiert werden. Öffne das Panel.");
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
        if (Dialogs.Confirm(error + "\n\nRelease-Seite im Browser öffnen?", "Update"))
            Shell.OpenUrl(r.HtmlUrl);
    }

    /// <summary>Power-Exec wird gerade beendet (z. B. für ein Update).</summary>
    public bool IsExiting { get; private set; }

    public void Exit()
    {
        IsExiting = true;
        // Offene Fenster (Einstellungen, Dialoge) zuerst schließen, solange die Panels noch existieren
        _settingsWindow?.Close();
        Dispose();
        Ui.Shutdown();
    }

    public void Dispose()
    {
        Repository?.Dispose();
        DisposePanels();
        Ui.Dispose();
    }
}
