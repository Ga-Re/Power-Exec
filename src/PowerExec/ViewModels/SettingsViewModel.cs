using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Scanning;
using PowerExec.Core.Updates;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

public sealed class ExclusionViewModel
{
    public ExclusionViewModel(string value, Action<ExclusionViewModel> remove)
    {
        Value = value;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Value { get; }
    public string Kind => ExclusionMatcher.KindOf(Value) switch
    {
        ExclusionKind.File => "Datei",
        ExclusionKind.Type => "Typ",
        _ => "Muster"
    };
    public ICommand RemoveCommand { get; }
}

public sealed class FolderViewModel : ObservableObject
{
    private string _path;
    private bool _recursive;
    private string _extensions;
    private string _newExclusion = string.Empty;
    private bool _isHighlighted;

    public FolderViewModel(FolderConfig f, string status, Action<FolderViewModel> remove)
    {
        _path = f.Path;
        _recursive = f.Recursive;
        _extensions = string.Join(" ", f.Extensions);
        Status = status;
        foreach (var e in f.Excludes) Exclusions.Add(new ExclusionViewModel(e, RemoveExclusion));
        RemoveCommand = new RelayCommand(() => remove(this));
        BrowseCommand = new RelayCommand(Browse);
        AddExclusionCommand = new RelayCommand(AddExclusion, () => !string.IsNullOrWhiteSpace(NewExclusion));
    }

    public string Path { get => _path; set => Set(ref _path, value?.Trim() ?? string.Empty); }
    public bool Recursive { get => _recursive; set => Set(ref _recursive, value); }

    /// <summary>Leer = Standard-Dateiendungen.</summary>
    public string Extensions { get => _extensions; set => Set(ref _extensions, value ?? string.Empty); }
    public string NewExclusion { get => _newExclusion; set => Set(ref _newExclusion, value ?? string.Empty); }
    public string Status { get; }
    public bool IsHighlighted { get => _isHighlighted; set => Set(ref _isHighlighted, value); }
    public ObservableCollection<ExclusionViewModel> Exclusions { get; } = new();

    public ICommand RemoveCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand AddExclusionCommand { get; }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Ordner auswählen" };
        if (Directory.Exists(Path)) dlg.InitialDirectory = Path;
        if (dlg.ShowDialog() == true) Path = dlg.FolderName;
    }

    private void AddExclusion()
    {
        foreach (var part in NewExclusion.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!Exclusions.Any(e => e.Value.Equals(part, StringComparison.OrdinalIgnoreCase)))
                Exclusions.Add(new ExclusionViewModel(part, RemoveExclusion));
        NewExclusion = string.Empty;
    }

    private void RemoveExclusion(ExclusionViewModel e) => Exclusions.Remove(e);

    public FolderConfig ToConfig() => new()
    {
        Path = Path,
        Recursive = Recursive,
        Extensions = SettingsViewModel.ParseExtensions(Extensions),
        Excludes = Exclusions.Select(e => e.Value).ToList()
    };
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly AppSettings _s;
    private string _section = "folders";
    private string _defaultExtensions;
    private string _globalExcludes;
    private string _updateStatus = string.Empty;
    private string? _hotkeyError;
    private bool _machinesCleared;

    public SettingsViewModel(AppController app, string? focusFolder)
    {
        _app = app;
        _s = SettingsStore.Clone(app.Settings);
        _defaultExtensions = string.Join(" ", _s.DefaultExtensions);
        _globalExcludes = string.Join("; ", _s.GlobalExcludes);

        foreach (var f in _s.Folders)
        {
            var vm = new FolderViewModel(f, FolderStatusText(f.Path), RemoveFolder);
            if (focusFolder != null && f.Path.Equals(focusFolder, StringComparison.OrdinalIgnoreCase)) vm.IsHighlighted = true;
            Folders.Add(vm);
        }
        Monitors = new[] { new KeyValuePair<int, string>(-1, "Hauptmonitor"), new KeyValuePair<int, string>(-2, "Alle Monitore") }
            .Concat(ScreenService.GetMonitors().Select(m => new KeyValuePair<int, string>(m.Index, m.DisplayName)))
            .ToList();

        AddFolderCommand = new RelayCommand(AddFolder);
        ClearMachinesCommand = new RelayCommand(() => { MachineList.ClearRecent(_s); _machinesCleared = true; OnPropertyChanged(nameof(MachinesText)); });
        CheckUpdatesCommand = new RelayCommand(() => _ = CheckUpdatesAsync());
        OpenLogFolderCommand = new RelayCommand(() => Launcher.OpenLocation(_s.EffectiveLogFolder, isFolder: true));
        OpenRepoCommand = new RelayCommand(() => Launcher.OpenUrl($"https://github.com/{UpdateChecker.Owner}/{UpdateChecker.Repo}"));
        BrowseEditorCommand = new RelayCommand(BrowseEditor);
        BrowseLogFolderCommand = new RelayCommand(BrowseLogFolder);
        SelectSectionCommand = new RelayCommand(p => Section = p?.ToString() ?? "folders");
        UpdateStatus = app.Updates.LastCheck is { } t
            ? (app.Updates.Available is { } r ? $"Version {r.Version.ToString(3)} ist verfügbar (geprüft {t:HH:mm})." : $"Aktuell (geprüft {t:HH:mm}).")
            : "Noch nicht geprüft.";
        if (focusFolder != null) _section = "folders";
    }

    public string Section { get => _section; set => Set(ref _section, value); }

    // Ordner
    public ObservableCollection<FolderViewModel> Folders { get; } = new();
    public string DefaultExtensions { get => _defaultExtensions; set => Set(ref _defaultExtensions, value ?? string.Empty); }
    public string GlobalExcludes { get => _globalExcludes; set => Set(ref _globalExcludes, value ?? string.Empty); }
    public bool AutoRescan { get => _s.AutoRescan; set { _s.AutoRescan = value; OnPropertyChanged(); } }

    // Panel
    public ScreenEdge Edge { get => _s.Edge; set { _s.Edge = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<ScreenEdge, string>> Edges { get; } = new[]
    {
        new KeyValuePair<ScreenEdge, string>(ScreenEdge.Right, "Rechts"),
        new KeyValuePair<ScreenEdge, string>(ScreenEdge.Left, "Links"),
        new KeyValuePair<ScreenEdge, string>(ScreenEdge.Top, "Oben"),
        new KeyValuePair<ScreenEdge, string>(ScreenEdge.Bottom, "Unten")
    };

    public IReadOnlyList<KeyValuePair<int, string>> Monitors { get; }
    public int MonitorChoice
    {
        get => _s.MonitorMode switch { MonitorMode.All => -2, MonitorMode.Specific => _s.MonitorIndex, _ => -1 };
        set
        {
            if (value == -2) _s.MonitorMode = MonitorMode.All;
            else if (value == -1) _s.MonitorMode = MonitorMode.Primary;
            else { _s.MonitorMode = MonitorMode.Specific; _s.MonitorIndex = value; }
            OnPropertyChanged();
        }
    }

    public HotZoneMode HotZone { get => _s.HotZone; set { _s.HotZone = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<HotZoneMode, string>> HotZones { get; } = new[]
    {
        new KeyValuePair<HotZoneMode, string>(HotZoneMode.Middle, "Mitte · 200 px"),
        new KeyValuePair<HotZoneMode, string>(HotZoneMode.FirstHalf, "Erste Hälfte (oben bzw. links)"),
        new KeyValuePair<HotZoneMode, string>(HotZoneMode.Full, "Ganze Kante")
    };

    public int HoverDelayMs { get => _s.HoverDelayMs; set { _s.HoverDelayMs = value; OnPropertyChanged(); } }
    public int CollapseDelayMs { get => _s.CollapseDelayMs; set { _s.CollapseDelayMs = value; OnPropertyChanged(); } }
    public int PanelWidth { get => _s.PanelWidth; set { _s.PanelWidth = value; OnPropertyChanged(); } }

    public AnimationMode Animation { get => _s.Animation; set { _s.Animation = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<AnimationMode, string>> Animations { get; } = new[]
    {
        new KeyValuePair<AnimationMode, string>(AnimationMode.Smooth, "Sanft (320 ms)"),
        new KeyValuePair<AnimationMode, string>(AnimationMode.Fast, "Schnell (150 ms)"),
        new KeyValuePair<AnimationMode, string>(AnimationMode.Off, "Aus")
    };

    public ThemeMode Theme { get => _s.Theme; set { _s.Theme = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<ThemeMode, string>> Themes { get; } = new[]
    {
        new KeyValuePair<ThemeMode, string>(ThemeMode.System, "Wie Windows"),
        new KeyValuePair<ThemeMode, string>(ThemeMode.Dark, "Dunkel"),
        new KeyValuePair<ThemeMode, string>(ThemeMode.Light, "Hell")
    };

    public string Hotkey { get => _s.Hotkey; set { _s.Hotkey = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(HotkeyDisplay)); } }
    public string HotkeyDisplay => string.IsNullOrWhiteSpace(_s.Hotkey) ? "Kein Tastenkürzel" : HotkeyService.Display(_s.Hotkey);
    public string? HotkeyError { get => _hotkeyError; set => Set(ref _hotkeyError, value); }
    public bool StartWithWindows { get => _s.StartWithWindows; set { _s.StartWithWindows = value; OnPropertyChanged(); } }

    // Ausführung
    public PsEngine DefaultEngine { get => _s.DefaultEngine; set { _s.DefaultEngine = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<PsEngine, string>> EnginesList { get; } = new[]
    {
        new KeyValuePair<PsEngine, string>(PsEngine.PowerShell7, "PowerShell 7 (pwsh.exe)"),
        new KeyValuePair<PsEngine, string>(PsEngine.WindowsPowerShell, "Windows PowerShell 5.1")
    };

    public RemoteEngineMode RemoteEngine { get => _s.RemoteEngine; set { _s.RemoteEngine = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<RemoteEngineMode, string>> RemoteEngines { get; } = new[]
    {
        new KeyValuePair<RemoteEngineMode, string>(RemoteEngineMode.WindowsPowerShell, "Windows PowerShell 5.1"),
        new KeyValuePair<RemoteEngineMode, string>(RemoteEngineMode.PreferPowerShell7, "PowerShell 7, falls vorhanden, sonst 5.1")
    };

    public string ExecutionPolicy { get => _s.ExecutionPolicy; set { _s.ExecutionPolicy = value ?? string.Empty; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<string, string>> Policies { get; } = new[]
    {
        new KeyValuePair<string, string>("Bypass", "Bypass (nur für gestartete Scripte)"),
        new KeyValuePair<string, string>("RemoteSigned", "RemoteSigned"),
        new KeyValuePair<string, string>("", "Systemstandard")
    };

    public bool NoProfile { get => _s.NoProfile; set { _s.NoProfile = value; OnPropertyChanged(); } }
    public bool NoExit { get => _s.NoExit; set { _s.NoExit = value; OnPropertyChanged(); } }
    public int ThrottleLimit { get => _s.ThrottleLimit; set { _s.ThrottleLimit = value; OnPropertyChanged(); } }
    public int RemoteTimeoutSeconds { get => _s.RemoteTimeoutSeconds; set { _s.RemoteTimeoutSeconds = value; OnPropertyChanged(); } }
    public string LogFolder { get => _s.LogFolder; set { _s.LogFolder = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(LogFolderDisplay)); } }
    public string LogFolderDisplay => _s.EffectiveLogFolder;

    public EditorKind Editor { get => _s.Editor; set { _s.Editor = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsCustomEditor)); } }
    public bool IsCustomEditor => _s.Editor == EditorKind.Custom;
    public string EditorPath { get => _s.EditorPath; set { _s.EditorPath = value ?? string.Empty; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<EditorKind, string>> Editors { get; } = new[]
    {
        new KeyValuePair<EditorKind, string>(EditorKind.VSCode, "Visual Studio Code"),
        new KeyValuePair<EditorKind, string>(EditorKind.Ise, "PowerShell ISE"),
        new KeyValuePair<EditorKind, string>(EditorKind.Notepad, "Editor (notepad.exe)"),
        new KeyValuePair<EditorKind, string>(EditorKind.Custom, "Eigener Pfad …")
    };

    public string MachinesText => $"{_s.Machines.Count(m => !m.Favorite)} zuletzt verwendet · {_s.Machines.Count(m => m.Favorite)} Favoriten";

    // Updates
    public string CurrentVersion => UpdateService.CurrentVersionText;
    public string RepoText => $"github.com/{UpdateChecker.Owner}/{UpdateChecker.Repo}";
    public bool CheckUpdatesOnStart { get => _s.CheckUpdatesOnStart; set { _s.CheckUpdatesOnStart = value; OnPropertyChanged(); } }
    public bool IncludePrereleases { get => _s.IncludePrereleases; set { _s.IncludePrereleases = value; OnPropertyChanged(); } }
    public string UpdateStatus { get => _updateStatus; set => Set(ref _updateStatus, value); }
    public string SettingsFile => _app.Store.FilePath;

    public ICommand AddFolderCommand { get; }
    public ICommand ClearMachinesCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand OpenRepoCommand { get; }
    public ICommand BrowseEditorCommand { get; }
    public ICommand BrowseLogFolderCommand { get; }
    public ICommand SelectSectionCommand { get; }

    private string FolderStatusText(string path)
    {
        var st = _app.Repository.FolderStatuses.FirstOrDefault(s => s.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (st == null) return "Noch nicht eingelesen";
        if (st.Error != null) return st.Error;
        return st.Excluded > 0 ? $"{st.Count} Dateien, {st.Excluded} ausgeblendet" : $"{st.Count} Dateien";
    }

    private void AddFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Ordner mit Scripten oder Programmen auswählen" };
        if (dlg.ShowDialog() != true) return;
        if (Folders.Any(f => f.Path.Equals(dlg.FolderName, StringComparison.OrdinalIgnoreCase))) return;
        Folders.Add(new FolderViewModel(new FolderConfig { Path = dlg.FolderName }, "Wird nach dem Speichern eingelesen", RemoveFolder));
    }

    private void RemoveFolder(FolderViewModel f) => Folders.Remove(f);

    private void BrowseEditor()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programme (*.exe)|*.exe" };
        if (dlg.ShowDialog() == true) EditorPath = dlg.FileName;
    }

    private void BrowseLogFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog();
        if (dlg.ShowDialog() == true) LogFolder = dlg.FolderName;
    }

    private async Task CheckUpdatesAsync()
    {
        UpdateStatus = "Suche nach Updates …";
        var r = await _app.CheckUpdatesAsync(manual: true, includePrereleases: _s.IncludePrereleases);
        UpdateStatus = _app.Updates.LastError != null
            ? "Prüfung fehlgeschlagen: " + _app.Updates.LastError
            : r != null ? $"Version {r.Version.ToString(3)} ist verfügbar. Der Hinweis erscheint im Panel." : "Du verwendest die aktuelle Version.";
    }

    internal static List<string> ParseExtensions(string text) =>
        text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .Distinct()
            .ToList();

    /// <summary>Erstellt die neuen Einstellungen aus dem Formular.</summary>
    public AppSettings Build()
    {
        _s.Folders = Folders.Where(f => f.Path.Length > 0).Select(f => f.ToConfig()).ToList();
        var exts = ParseExtensions(DefaultExtensions);
        _s.DefaultExtensions = exts.Count > 0 ? exts : new List<string>(AppSettings.StandardExtensions);
        _s.GlobalExcludes = GlobalExcludes.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        // Laufzeitzustand aus den aktuellen Einstellungen übernehmen (kann sich währenddessen geändert haben)
        _s.Favorites = new List<string>(_app.Settings.Favorites);
        _s.CollapsedGroups = new List<string>(_app.Settings.CollapsedGroups);
        _s.Pinned = _app.Settings.Pinned;
        _s.Machines = _app.Settings.Machines
            .Where(m => !_machinesCleared || m.Favorite)
            .Select(m => new MachineEntry { Name = m.Name, Favorite = m.Favorite, LastUsedUtc = m.LastUsedUtc })
            .ToList();
        return _s;
    }
}
