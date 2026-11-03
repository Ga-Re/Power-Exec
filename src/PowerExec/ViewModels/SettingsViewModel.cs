using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PowerExec.Core.Config;
using PowerExec.Core.Search;
using PowerExec.Core.Profiles;
using PowerExec.Core.Models;
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

/// <summary>Eine Zeile der Eintragstabelle eines Ordners.</summary>
public sealed class EntryRowViewModel : ObservableObject
{
    private readonly FolderViewModel _folder;
    private readonly AppSettings _settings;
    private ImageSource? _icon;

    public EntryRowViewModel(FolderViewModel folder, AppController app, AppSettings settings, LaunchItem? item, string fullPath, string relativePath, string? pattern)
    {
        _folder = folder;
        _settings = settings;
        Item = item;
        FullPath = fullPath;
        RelativePath = relativePath;
        Pattern = pattern;
        Type = item?.Type ?? LaunchItem.TypeFromExtension(fullPath);
        ToggleCommand = new RelayCommand(() => IsVisibleInPanel = !IsVisibleInPanel, () => !IsLocked);
        ConfigureCommand = new RelayCommand(() => _folder.Configure(this), () => Item != null && !IsLocked);
        if (item != null && settings.ShowProgramIcons && Type is ItemType.Executable or ItemType.Shortcut or ItemType.ConsoleSnapIn)
            _icon = app.Icons.Get(item.FullPath, item.LastWriteTimeUtc, img => { _icon = img; OnPropertyChanged(nameof(Icon)); OnPropertyChanged(nameof(HasIcon)); });
    }

    public LaunchItem? Item { get; }
    public string FullPath { get; }
    public string RelativePath { get; }
    public string? Pattern { get; }
    public ItemType Type { get; }
    public bool IsLocked => Pattern != null;
    public bool CanToggle => !IsLocked;
    public bool IsBlocked => Item?.IsBlocked == true;
    public ImageSource? Icon => _icon;
    public bool HasIcon => _icon != null;

    public string Badge => Type switch
    {
        ItemType.PowerShell => "PS1",
        ItemType.Executable => "EXE",
        ItemType.Shortcut => "LNK",
        ItemType.ConsoleSnapIn => "MSC",
        _ => System.IO.Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant()
    };

    public string Name
    {
        get
        {
            var e = _settings.FindEntry(FullPath);
            if (!string.IsNullOrWhiteSpace(e?.DisplayName)) return e!.DisplayName;
            return Item != null && Item.Type is not (ItemType.PowerShell or ItemType.Batch) ? Item.DisplayName : RelativePath;
        }
    }

    public string Info
    {
        get
        {
            if (IsLocked) return $"Ausgeblendet durch Muster {Pattern}";
            var e = _settings.FindEntry(FullPath);
            if (!string.IsNullOrWhiteSpace(e?.Description)) return e!.Description;
            if (Item == null) return string.Empty;
            return string.IsNullOrWhiteSpace(Item.Synopsis) ? (Item.Type is ItemType.PowerShell or ItemType.Batch ? "Keine Synopsis" : Item.FileName) : Item.Synopsis;
        }
    }

    public EntryVisibility State => IsLocked ? EntryVisibility.Hidden : FolderVisibility.State(_folder.Config, RelativePath);
    public bool IsNew => State == EntryVisibility.Pending;

    public bool IsVisibleInPanel
    {
        get => !IsLocked && State == EntryVisibility.Visible;
        set
        {
            if (IsLocked) return;
            FolderVisibility.SetVisible(_folder.Config, RelativePath, value);
            Refresh();
            _folder.OnRowsChanged();
        }
    }

    public string ProfilesText
    {
        get
        {
            if (Item == null || IsLocked) return "–";
            var profiles = ProfileResolver.GetProfiles(_settings, Item);
            if (profiles[0].IsImplicit) return "Standard (automatisch)";
            var def = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles[0];
            return profiles.Count == 1 ? def.Name : $"{profiles.Count} Profile · Standard: {def.Name}";
        }
    }

    public string ProfileDetail
    {
        get
        {
            if (Item == null || IsLocked) return string.Empty;
            var profiles = ProfileResolver.GetProfiles(_settings, Item);
            return profiles.Count > 1
                ? string.Join(" · ", profiles.Select(p => p.Name))
                : ProfileResolver.Summary(profiles[0], Item);
        }
    }

    public bool IsAdmin => Item != null && !IsLocked && ProfileResolver.GetDefault(_settings, Item).RunAsAdmin;

    public ICommand ToggleCommand { get; }
    public ICommand ConfigureCommand { get; }

    public void Refresh()
    {
        foreach (var p in new[] { nameof(Name), nameof(Info), nameof(State), nameof(IsNew), nameof(IsVisibleInPanel), nameof(ProfilesText), nameof(ProfileDetail), nameof(IsAdmin) })
            OnPropertyChanged(p);
    }
}

public sealed class FolderViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly AppSettings _settings;
    private string _path;
    private bool _recursive;
    private string _extensions;
    private string _newExclusion = string.Empty;
    private bool _isHighlighted;
    private bool _isExpanded;
    private bool _showOptions;
    private string _query = string.Empty;
    private string _typeFilter = "all";

    public FolderViewModel(AppController app, AppSettings settings, FolderConfig f, string status, Action<FolderViewModel> remove)
    {
        _app = app;
        _settings = settings;
        Config = f.Clone();
        _path = f.Path;
        _recursive = f.Recursive;
        _extensions = string.Join(" ", f.Extensions);
        Status = status;
        foreach (var e in f.Excludes) Exclusions.Add(new ExclusionViewModel(e, RemoveExclusion));
        RemoveCommand = new RelayCommand(() => remove(this));
        BrowseCommand = new RelayCommand(Browse);
        AddExclusionCommand = new RelayCommand(AddExclusion, () => !string.IsNullOrWhiteSpace(NewExclusion));
        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        ToggleOptionsCommand = new RelayCommand(() => ShowOptions = !ShowOptions);
        AllOnCommand = new RelayCommand(() => SetAll(true));
        AllOffCommand = new RelayCommand(() => SetAll(false));
        ApproveAllCommand = new RelayCommand(() => ResolvePending(true));
        DismissPendingCommand = new RelayCommand(() => ResolvePending(false));
        SetTypeFilterCommand = new RelayCommand(p => TypeFilter = p?.ToString() ?? "all");

        // Gefundene Einträge (auch ausgeblendete) und per Muster ausgeschlossene Dateien
        foreach (var item in app.Repository.Items.Where(i => i.RootFolder.Equals(f.Path, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(i => FolderVisibility.RelativePath(f, i.FullPath), StringComparer.CurrentCultureIgnoreCase))
            Rows.Add(new EntryRowViewModel(this, app, settings, item, item.FullPath, FolderVisibility.RelativePath(f, item.FullPath), null));
        var status2 = app.Repository.FolderStatuses.FirstOrDefault(st => st.Path.Equals(f.Path, StringComparison.OrdinalIgnoreCase));
        if (status2 != null)
            foreach (var ex in status2.ExcludedFiles.OrderBy(x => x.RelativePath, StringComparer.CurrentCultureIgnoreCase))
                Rows.Add(new EntryRowViewModel(this, app, settings, null, ex.FullPath, ex.RelativePath, ex.Pattern));

        RowsView = System.Windows.Data.CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => o is EntryRowViewModel r && Matches(r);
        _isExpanded = Rows.Any(r => r.IsNew);
    }

    /// <summary>Arbeitskopie der Ordner-Einstellungen (Haken, bekannte Dateien, Modus).</summary>
    public FolderConfig Config { get; }

    public string Path { get => _path; set => Set(ref _path, value?.Trim() ?? string.Empty); }
    public bool Recursive { get => _recursive; set => Set(ref _recursive, value); }

    /// <summary>Leer = Standard-Dateiendungen.</summary>
    public string Extensions { get => _extensions; set => Set(ref _extensions, value ?? string.Empty); }
    public string NewExclusion { get => _newExclusion; set => Set(ref _newExclusion, value ?? string.Empty); }
    public string Status { get; }
    public bool IsHighlighted { get => _isHighlighted; set { if (Set(ref _isHighlighted, value) && value) IsExpanded = true; } }
    public ObservableCollection<ExclusionViewModel> Exclusions { get; } = new();

    public ObservableCollection<EntryRowViewModel> Rows { get; } = new();
    public System.ComponentModel.ICollectionView RowsView { get; }

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public bool ShowOptions { get => _showOptions; set => Set(ref _showOptions, value); }

    public string Query
    {
        get => _query;
        set { if (Set(ref _query, value ?? string.Empty)) { RowsView.Refresh(); OnPropertyChanged(nameof(NoRows)); } }
    }

    public string TypeFilter
    {
        get => _typeFilter;
        set { if (Set(ref _typeFilter, value)) { RowsView.Refresh(); OnPropertyChanged(nameof(NoRows)); } }
    }

    public bool NoRows => Rows.Count > 0 && !Rows.Any(Matches);
    public bool HasNoEntries => Rows.Count == 0;

    public bool RequireApproval
    {
        get => Config.NewFiles == NewFilesMode.RequireApproval;
        set
        {
            Config.NewFiles = value ? NewFilesMode.RequireApproval : NewFilesMode.AutoShow;
            if (value)
                foreach (var r in Rows.Where(r => !r.IsLocked)) FolderVisibility.MarkKnown(Config, r.RelativePath); // Vorhandenes gilt als bekannt
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoShow));
            OnPropertyChanged(nameof(ModeHint));
            OnRowsChanged();
        }
    }

    public bool AutoShow { get => !RequireApproval; set => RequireApproval = !value; }

    /// <summary>Ordner als Gruppe im Panel anzeigen.</summary>
    public bool ShowGroup
    {
        get => Config.ShowGroup;
        set { Config.ShowGroup = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowGroupText)); }
    }
    public string ShowGroupText => Config.ShowGroup ? "im Panel" : "ausgeblendet";

    public string ModeHint => RequireApproval
        ? "Neue Dateien bleiben ausgeblendet; im Panel und hier erscheint ein Hinweis."
        : "Neue Scripte und Programme erscheinen sofort im Panel.";

    public int PendingCount => Rows.Count(r => r.IsNew);
    public bool HasPending => PendingCount > 0;
    public string PendingText => PendingCount == 1 ? "1 neuer Eintrag wartet auf Freigabe." : $"{PendingCount} neue Einträge warten auf Freigabe.";

    public string Summary
    {
        get
        {
            var visible = Rows.Count(r => r.IsVisibleInPanel);
            var selectable = Rows.Count(r => !r.IsLocked);
            return $"{(Recursive ? "Mit Unterordnern" : "Nur oberste Ebene")} · {selectable} gefunden · {visible} im Panel · neue Dateien: " +
                   (RequireApproval ? "nach Freigabe" : "automatisch");
        }
    }

    public string CountText => $"{Rows.Count(r => r.IsVisibleInPanel)} von {Rows.Count(r => !r.IsLocked)} im Panel";

    public ICommand RemoveCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand AddExclusionCommand { get; }
    public ICommand ToggleExpandedCommand { get; }
    public ICommand ToggleOptionsCommand { get; }
    public ICommand AllOnCommand { get; }
    public ICommand AllOffCommand { get; }
    public ICommand ApproveAllCommand { get; }
    public ICommand DismissPendingCommand { get; }
    public ICommand SetTypeFilterCommand { get; }

    private bool Matches(EntryRowViewModel r)
    {
        bool typeOk = _typeFilter switch
        {
            "scripts" => r.Type is ItemType.PowerShell or ItemType.Batch,
            "programs" => r.Type is not (ItemType.PowerShell or ItemType.Batch),
            _ => true
        };
        if (!typeOk) return false;
        if (_query.Trim().Length == 0) return true;
        var text = r.Name + " " + r.Info + " " + r.RelativePath;
        return _query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(t => text.Contains(t, StringComparison.CurrentCultureIgnoreCase));
    }

    internal void OnRowsChanged()
    {
        foreach (var p in new[] { nameof(PendingCount), nameof(HasPending), nameof(PendingText), nameof(Summary), nameof(CountText) })
            OnPropertyChanged(p);
    }

    private void SetAll(bool visible)
    {
        foreach (var r in Rows.Where(r => !r.IsLocked && Matches(r)))
            FolderVisibility.SetVisible(Config, r.RelativePath, visible);
        foreach (var r in Rows) r.Refresh();
        OnRowsChanged();
    }

    private void ResolvePending(bool show)
    {
        foreach (var r in Rows.Where(r => r.IsNew))
            FolderVisibility.SetVisible(Config, r.RelativePath, show);
        foreach (var r in Rows) r.Refresh();
        OnRowsChanged();
    }

    /// <summary>⚙: Startprofile des Eintrags bearbeiten (wird mit „Speichern“ der Einstellungen übernommen).</summary>
    internal void Configure(EntryRowViewModel row)
    {
        if (row.Item == null) return;
        var item = row.Item.IsPowerShell ? _app.Repository.Reload(row.Item) : row.Item;
        var vm = new ProfilesViewModel(_app, item, _settings, saveImmediately: false);
        vm.Saved += row.Refresh;
        var w = new Views.ProfilesWindow(vm);
        w.Owner = Application.Current.Windows.OfType<Views.SettingsWindow>().FirstOrDefault();
        w.ShowDialog();
    }

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

    public FolderConfig ToConfig()
    {
        var c = Config.Clone();
        c.Path = Path;
        c.Recursive = Recursive;
        c.Extensions = SettingsViewModel.ParseExtensions(Extensions);
        c.Excludes = Exclusions.Select(e => e.Value).ToList();
        return c;
    }
}

public sealed class ColorPresetViewModel : ObservableObject
{
    private readonly PanelSettingsViewModel _owner;

    public ColorPresetViewModel(PanelSettingsViewModel owner, ColorPreset preset)
    {
        _owner = owner;
        Name = preset.Name;
        Hex = preset.Hex;
        if (PanelColors.TryParse(preset.Hex, out var c))
            Swatch = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        else
            Swatch = Application.Current.Resources["Bg.Panel"] as Brush ?? Brushes.Gray;
        SelectCommand = new RelayCommand(() => _owner.PanelColor = Hex);
    }

    public string Name { get; }
    public string Hex { get; }
    public Brush Swatch { get; }
    public string ToolTip => Hex.Length == 0 ? "Farbe des Designs (hell/dunkel)" : $"{Name} ({Hex})";
    public bool IsSelected => string.Equals(_owner.PanelColor, Hex, StringComparison.OrdinalIgnoreCase);
    public ICommand SelectCommand { get; }
    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

public sealed class CategoryItemViewModel
{
    public CategoryItemViewModel(string name, Action<CategoryItemViewModel, int> move, Action<CategoryItemViewModel> remove)
    {
        Name = name;
        UpCommand = new RelayCommand(() => move(this, -1));
        DownCommand = new RelayCommand(() => move(this, 1));
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Name { get; }
    public string Display => GroupManager.LeafOf(Name);
    public Thickness Indent => new(GroupManager.DepthOf(Name) * 18, 0, 0, 0);
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }
    public ICommand RemoveCommand { get; }
}

public sealed record FolderTimeInfo(string Path, string Text, bool IsProblem);

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
            var vm = new FolderViewModel(app, _s, f, FolderStatusText(f.Path), RemoveFolder);
            if (focusFolder != null && f.Path.Equals(focusFolder, StringComparison.OrdinalIgnoreCase)) vm.IsHighlighted = true;
            Folders.Add(vm);
        }
        _monitors = new[] { new KeyValuePair<int, string>(-1, "Hauptmonitor"), new KeyValuePair<int, string>(-2, "Alle Monitore") }
            .Concat(ScreenService.GetMonitors().Select(m => new KeyValuePair<int, string>(m.Index, m.DisplayName)))
            .ToList();

        AddFolderCommand = new RelayCommand(AddFolder);
        ClearMachinesCommand = new RelayCommand(() => { MachineList.ClearRecent(_s); _machinesCleared = true; OnPropertyChanged(nameof(MachinesText)); });
        CheckUpdatesCommand = new RelayCommand(() => _ = CheckUpdatesAsync(), () => !_isChecking);
        InstallUpdateCommand = new RelayCommand(() => _ = _app.InstallUpdateAsync(), () => HasUpdate && !_app.Update.Busy);
        ReleaseNotesCommand = new RelayCommand(() => { if (_app.Updates.Available is { } r) Launcher.OpenUrl(r.HtmlUrl); });
        _app.Update.PropertyChanged += OnUpdateChanged;
        AppLog.Changed += OnLogChanged;
        OpenLogFolderCommand = new RelayCommand(() => Launcher.OpenLocation(_s.EffectiveLogFolder, isFolder: true));
        OpenRepoCommand = new RelayCommand(() => Launcher.OpenUrl($"https://github.com/{UpdateChecker.Owner}/{UpdateChecker.Repo}"));
        BrowseEditorCommand = new RelayCommand(BrowseEditor);
        BrowseLogFolderCommand = new RelayCommand(BrowseLogFolder);
        SelectSectionCommand = new RelayCommand(p => Section = p?.ToString() ?? "folders");

        foreach (var c in GroupManager.All(_s))
            Categories.Add(new CategoryItemViewModel(c, MoveCategory, RemoveCategory));
        AddCategoryCommand = new RelayCommand(AddCategory, () => !string.IsNullOrWhiteSpace(NewCategory));

        foreach (var pc in _s.Panels) Panels.Add(CreatePanelVm(pc));
        SelectedPanel = Panels.FirstOrDefault();
        AddPanelCommand = new RelayCommand(AddPanel);
        RemovePanelCommand = new RelayCommand(RemovePanel, () => Panels.Count > 1 && SelectedPanel != null);
        foreach (var c in _s.CustomEntries) CustomEntries.Add(new CustomEntryViewModel(c, RemoveCustomEntry));
        AddCustomEntryCommand = new RelayCommand(AddCustomEntry);
        foreach (var g in _s.MachineGroups) MachineGroups.Add(new MachineGroupViewModel(g, RemoveGroup));
        AddGroupCommand = new RelayCommand(AddGroup);
        AddWorkspaceCommand = new RelayCommand(AddWorkspace);
        LoadWorkspaces();

        CopyLogCommand = new RelayCommand(() => { try { Clipboard.SetText(DiagnosticsText()); } catch { } });
        OpenAppLogFolderCommand = new RelayCommand(() => Launcher.OpenLocation(System.IO.Path.GetDirectoryName(AppLog.FilePath)!, isFolder: true));
        RefreshLogCommand = new RelayCommand(() => OnPropertyChanged(nameof(LogText)));
        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
        UpdateStatus = app.Updates.LastCheck is { } t
            ? (app.Updates.Available is { } r ? $"Version {r.Version.ToString(3)} ist verfügbar (geprüft {t:HH:mm})." : $"Aktuell (geprüft {t:HH:mm}).")
            : "Noch nicht geprüft.";
        if (focusFolder != null) _section = "folders";
    }

    public string Section
    {
        get => _section;
        set
        {
            if (!Set(ref _section, value)) return;
            if (value == "panels") RefreshPanelChoices(); // neu hinzugefügte Ordner/Kategorien anbieten
            if (value == "workspaces") { ApplyWorkspaceRenames(); LoadWorkspaces(); }
        }
    }

    // Ordner
    public ObservableCollection<FolderViewModel> Folders { get; } = new();
    public string DefaultExtensions { get => _defaultExtensions; set => Set(ref _defaultExtensions, value ?? string.Empty); }
    public string GlobalExcludes { get => _globalExcludes; set => Set(ref _globalExcludes, value ?? string.Empty); }
    public bool AutoRescan { get => _s.AutoRescan; set { _s.AutoRescan = value; OnPropertyChanged(); } }
    public int ScanTimeoutSeconds { get => _s.ScanTimeoutSeconds; set { _s.ScanTimeoutSeconds = value; OnPropertyChanged(); } }

    public int HoverDelayMs { get => _s.HoverDelayMs; set { _s.HoverDelayMs = value; OnPropertyChanged(); } }
    public int CollapseDelayMs { get => _s.CollapseDelayMs; set { _s.CollapseDelayMs = value; OnPropertyChanged(); } }

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

    public string? HotkeyError { get => _hotkeyError; set => Set(ref _hotkeyError, value); }
    public bool StartWithWindows { get => _s.StartWithWindows; set { _s.StartWithWindows = value; OnPropertyChanged(); } }

    // ---- Panels ------------------------------------------------------------

    private readonly IReadOnlyList<KeyValuePair<int, string>> _monitors;
    private PanelSettingsViewModel? _selectedPanel;

    public ObservableCollection<PanelSettingsViewModel> Panels { get; } = new();
    public PanelSettingsViewModel? SelectedPanel { get => _selectedPanel; set { if (Set(ref _selectedPanel, value)) CommandManager.InvalidateRequerySuggested(); } }
    public bool IsMultiPanel => Panels.Count > 1;
    public ICommand AddPanelCommand { get; }
    public ICommand RemovePanelCommand { get; }

    private PanelSettingsViewModel CreatePanelVm(PanelConfig c) =>
        new(c, _monitors, Folders.Select(f => f.Path).Where(p => p.Length > 0), Categories.Select(x => x.Name));

    private void AddPanel()
    {
        var used = Panels.Select(p => p.Edge).ToHashSet();
        var edge = new[] { ScreenEdge.Left, ScreenEdge.Right, ScreenEdge.Top, ScreenEdge.Bottom }.FirstOrDefault(e => !used.Contains(e));
        var vm = CreatePanelVm(new PanelConfig { Name = $"Panel {Panels.Count + 1}", Edge = edge });
        Panels.Add(vm);
        SelectedPanel = vm;
        OnPropertyChanged(nameof(IsMultiPanel));
    }

    private void RemovePanel()
    {
        if (SelectedPanel == null || Panels.Count <= 1) return;
        if (MessageBox.Show($"Panel „{SelectedPanel.Name}“ entfernen?", "Panel entfernen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var i = Panels.IndexOf(SelectedPanel);
        Panels.Remove(SelectedPanel);
        SelectedPanel = Panels[Math.Clamp(i, 0, Panels.Count - 1)];
        OnPropertyChanged(nameof(IsMultiPanel));
    }

    /// <summary>Ordner/Kategorien haben sich geändert: Auswahl der Panels neu aufbauen (Haken bleiben erhalten).</summary>
    public void RefreshPanelChoices()
    {
        var sel = SelectedPanel?.Id;
        var configs = Panels.Select(p => p.ToConfig()).ToList();
        Panels.Clear();
        foreach (var c in configs) Panels.Add(CreatePanelVm(c));
        SelectedPanel = Panels.FirstOrDefault(p => p.Id == sel) ?? Panels.FirstOrDefault();
    }

    // ---- Eigene Einträge ------------------------------------------------------

    public ObservableCollection<CustomEntryViewModel> CustomEntries { get; } = new();
    public ICommand AddCustomEntryCommand { get; }
    public bool HasNoCustomEntries => CustomEntries.Count == 0;

    private void AddCustomEntry(object? kind)
    {
        var k = kind is CustomEntryKind ck ? ck : Enum.TryParse<CustomEntryKind>(kind?.ToString(), out var parsed) ? parsed : CustomEntryKind.Url;
        CustomEntries.Add(new CustomEntryViewModel(new CustomEntry { Kind = k }, RemoveCustomEntry));
        OnPropertyChanged(nameof(HasNoCustomEntries));
    }

    private void RemoveCustomEntry(CustomEntryViewModel e)
    {
        CustomEntries.Remove(e);
        OnPropertyChanged(nameof(HasNoCustomEntries));
    }

    // ---- Rechnergruppen -------------------------------------------------------

    public ObservableCollection<MachineGroupViewModel> MachineGroups { get; } = new();
    public ICommand AddGroupCommand { get; }
    public bool HasNoGroups => MachineGroups.Count == 0;

    private void AddGroup()
    {
        var name = "Gruppe";
        for (int i = 2; MachineGroups.Any(g => g.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)); i++) name = $"Gruppe {i}";
        MachineGroups.Add(new MachineGroupViewModel(new MachineGroup { Name = name }, RemoveGroup));
        OnPropertyChanged(nameof(HasNoGroups));
    }

    private void RemoveGroup(MachineGroupViewModel g)
    {
        MachineGroups.Remove(g);
        OnPropertyChanged(nameof(HasNoGroups));
    }

    // ---- Arbeitsbereiche ------------------------------------------------------

    public ObservableCollection<WorkspaceRowViewModel> Workspaces { get; } = new();
    public string ActiveWorkspace => _s.ActiveWorkspace;
    private string _newWorkspace = string.Empty;
    private bool _copyWorkspace = true;
    public string NewWorkspace { get => _newWorkspace; set => Set(ref _newWorkspace, value ?? string.Empty); }
    public bool CopyWorkspace { get => _copyWorkspace; set => Set(ref _copyWorkspace, value); }
    public ICommand AddWorkspaceCommand { get; }

    /// <summary>Speichern, Fenster schließen und den Arbeitsbereich wechseln (übernimmt das Fenster).</summary>
    public event Action<string>? SwitchWorkspaceRequested;

    private void LoadWorkspaces()
    {
        Workspaces.Clear();
        foreach (var w in _s.Workspaces)
        {
            bool active = w.Name.Equals(_s.ActiveWorkspace, StringComparison.CurrentCultureIgnoreCase);
            int folders = active ? Folders.Count : w.Folders.Count;
            int entries = active ? CustomEntries.Count : w.CustomEntries.Count;
            int favs = active ? _app.Settings.Favorites.Count : w.Favorites.Count;
            var summary = $"{folders} Ordner · {favs} Favoriten · {entries} eigene Einträge" + (active ? " · aktiv" : string.Empty);
            Workspaces.Add(new WorkspaceRowViewModel(w.Name, active, summary,
                r => SwitchWorkspaceRequested?.Invoke(r.OriginalName),
                RemoveWorkspace));
        }
    }

    private void AddWorkspace()
    {
        ApplyWorkspaceRenames();
        // Aktuelle Formularwerte als Grundlage für eine Kopie verwenden
        _s.Folders = Folders.Where(f => f.Path.Length > 0).Select(f => f.ToConfig()).ToList();
        _s.CustomEntries = CustomEntries.Select(c => c.ToEntry()).ToList();
        _s.Favorites = new List<string>(_app.Settings.Favorites);
        WorkspaceManager.Create(_s, NewWorkspace, CopyWorkspace);
        NewWorkspace = string.Empty;
        LoadWorkspaces();
    }

    private void RemoveWorkspace(WorkspaceRowViewModel w)
    {
        if (MessageBox.Show($"Arbeitsbereich „{w.OriginalName}“ mit seinen Ordnern, Profilen und Favoriten löschen?", "Arbeitsbereich löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        ApplyWorkspaceRenames();
        WorkspaceManager.Delete(_s, w.Name);
        LoadWorkspaces();
    }

    private void ApplyWorkspaceRenames()
    {
        foreach (var w in Workspaces.Where(w => w.Name.Trim().Length > 0 && w.Name.Trim() != w.OriginalName))
            WorkspaceManager.Rename(_s, w.OriginalName, w.Name);
    }

    public bool ShowProgramIcons { get => _s.ShowProgramIcons; set { _s.ShowProgramIcons = value; OnPropertyChanged(); } }
    public bool HideHotZoneInFullscreen { get => _s.HideHotZoneInFullscreen; set { _s.HideHotZoneInFullscreen = value; OnPropertyChanged(); } }
    public int RecentRunsCount { get => _s.RecentRunsCount; set { _s.RecentRunsCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(RecentRunsText)); } }
    public string RecentRunsText => _s.RecentRunsCount == 0 ? "aus" : _s.RecentRunsCount.ToString();

    // Gruppierung und Kategorien
    public GroupingMode Grouping { get => _s.Grouping; set { _s.Grouping = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsCategoryGrouping)); } }
    public bool IsCategoryGrouping => _s.Grouping == GroupingMode.Category;
    public IReadOnlyList<KeyValuePair<GroupingMode, string>> Groupings { get; } = new[]
    {
        new KeyValuePair<GroupingMode, string>(GroupingMode.Folder, "Eigene Gruppen + Ordner"),
        new KeyValuePair<GroupingMode, string>(GroupingMode.Category, "Nur eigene Gruppen")
    };
    public ObservableCollection<CategoryItemViewModel> Categories { get; } = new();
    private string _newCategory = string.Empty;
    public string NewCategory { get => _newCategory; set => Set(ref _newCategory, value ?? string.Empty); }
    public ICommand AddCategoryCommand { get; }

    /// <summary>Unter den Geschwistern verschieben (Untergruppen wandern mit).</summary>
    private void MoveCategory(CategoryItemViewModel c, int delta)
    {
        var tmp = new AppSettings { CategoryOrder = Categories.Select(x => x.Name).ToList() };
        GroupManager.Move(tmp, c.Name, delta);
        var order = GroupManager.All(tmp);
        for (int target = 0; target < order.Count; target++)
        {
            var current = Categories.ToList().FindIndex(x => x.Name.Equals(order[target], StringComparison.CurrentCultureIgnoreCase));
            if (current >= 0 && current != target) Categories.Move(current, target);
        }
    }

    /// <summary>Entfernt die Gruppe samt Untergruppen aus der Liste (wird beim Speichern gelöscht).</summary>
    private void RemoveCategory(CategoryItemViewModel c)
    {
        foreach (var x in Categories.Where(x => GroupManager.IsSameOrBelow(x.Name, c.Name)).ToList()) Categories.Remove(x);
    }

    private void AddCategory()
    {
        var name = GroupManager.Normalize(NewCategory);
        if (name.Length > 0 && !Categories.Any(c => c.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
            Categories.Add(new CategoryItemViewModel(name, MoveCategory, RemoveCategory));
        NewCategory = string.Empty;
    }

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
    public bool RemoteUseSsl { get => _s.RemoteUseSsl; set { _s.RemoteUseSsl = value; OnPropertyChanged(); } }
    public bool NotifyOnFinish { get => _s.NotifyOnFinish; set { _s.NotifyOnFinish = value; OnPropertyChanged(); } }
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
        Folders.Add(new FolderViewModel(_app, _s, new FolderConfig { Path = dlg.FolderName }, "Wird nach dem Speichern eingelesen", RemoveFolder) { IsExpanded = true, ShowOptions = true });
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

    // ---- Update-Hinweis in den Einstellungen ----------------------------

    private bool _isChecking;

    public UpdateState Update => _app.Update;
    public bool HasUpdate => _app.Updates.Available != null;
    public string UpdateBannerTitle => _app.Updates.Available is { } r ? $"Version {r.Version.ToString(3)} ist verfügbar" : string.Empty;
    public bool IsChecking { get => _isChecking; private set { if (Set(ref _isChecking, value)) CommandManager.InvalidateRequerySuggested(); } }

    public ICommand InstallUpdateCommand { get; }
    public ICommand ReleaseNotesCommand { get; }

    private void OnUpdateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateBannerTitle));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Vom Fenster beim Schließen aufgerufen.</summary>
    public void Detach()
    {
        _app.Update.PropertyChanged -= OnUpdateChanged;
        AppLog.Changed -= OnLogChanged;
    }

    // ---- Diagnose ---------------------------------------------------------

    public string LogText => AppLog.Recent(400);
    public string LogFile => AppLog.FilePath;

    private void OnLogChanged() => Application.Current?.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(LogText)));

    public IReadOnlyList<FolderTimeInfo> FolderTimes => _app.Repository.FolderStatuses.Select(st => new FolderTimeInfo(st.Path,
        st.Error != null ? st.Error
        : st.FromCache ? $"zwischengespeichert (Stand {st.CachedAtUtc?.ToLocalTime():dd.MM. HH:mm})"
        : $"{st.Count} Dateien · {st.Duration.TotalMilliseconds:0} ms", st.Error != null)).ToList();

    public string SystemInfo =>
        $"Power-Exec {UpdateService.CurrentVersionText} · {(UpdateService.IsInstalledViaSetup ? "installiert" : "portabel")} · " +
        $"{Environment.OSVersion.VersionString} · .NET {Environment.Version} · PowerShell 7: {(Launcher.IsPwshInstalled ? "ja" : "nein")}";

    public ICommand CopyLogCommand { get; }
    public ICommand OpenAppLogFolderCommand { get; }
    public ICommand RefreshLogCommand { get; }

    private string DiagnosticsText() =>
        SystemInfo + Environment.NewLine + Environment.NewLine +
        string.Join(Environment.NewLine, FolderTimes.Select(f => $"{f.Path}: {f.Text}")) + Environment.NewLine + Environment.NewLine +
        AppLog.Recent(400);

    // ---- Sichern & Wiederherstellen ----------------------------------------

    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }

    /// <summary>Nach einem Import schließt sich das Fenster und öffnet sich mit den neuen Einstellungen.</summary>
    public event Action? ReopenRequested;

    private void Export()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Power-Exec-Einstellungen (*.json)|*.json",
            FileName = $"Power-Exec-Einstellungen_{DateTime.Now:yyyy-MM-dd}.json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, SettingsTransfer.Export(_app.Settings));
            AppLog.Info($"Einstellungen exportiert: {dlg.FileName}");
            MessageBox.Show("Die gespeicherten Einstellungen wurden exportiert.\nKennwörter sind darin nicht enthalten.", "Exportieren",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Exportieren", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Import()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Power-Exec-Einstellungen (*.json)|*.json|Alle Dateien (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;
        AppSettings imported;
        try
        {
            imported = SettingsTransfer.Parse(File.ReadAllText(dlg.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Die Datei konnte nicht gelesen werden:\n" + ex.Message, "Importieren", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var answer = MessageBox.Show(
            $"{imported.Folders.Count} Ordner, {imported.Entries.Count} konfigurierte Einträge, {imported.Favorites.Count} Favoriten.\n\n" +
            "Ja = zusammenführen (vorhandene Einstellungen bleiben, Ordner und Profile werden ergänzt)\n" +
            "Nein = alles ersetzen\nAbbrechen = nichts ändern",
            "Einstellungen importieren", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.Cancel) return;
        var result = answer == MessageBoxResult.Yes ? SettingsTransfer.Merge(_app.Settings, imported) : imported;
        result.Fingerprints = new Dictionary<string, string>(_app.Settings.Fingerprints);
        _app.ApplySettings(result);
        AppLog.Info($"Einstellungen importiert ({(answer == MessageBoxResult.Yes ? "zusammengeführt" : "ersetzt")}): {dlg.FileName}");
        ReopenRequested?.Invoke();
    }

    private async Task CheckUpdatesAsync()
    {
        IsChecking = true;
        UpdateStatus = "Suche nach Updates …";
        var r = await _app.CheckUpdatesAsync(manual: true, includePrereleases: _s.IncludePrereleases);
        IsChecking = false;
        UpdateStatus = _app.Updates.LastError != null
            ? "Prüfung fehlgeschlagen: " + _app.Updates.LastError
            : r != null ? $"Version {r.Version.ToString(3)} ist verfügbar – siehe Hinweis oben." : $"Du verwendest die aktuelle Version ({UpdateService.CurrentVersionText}).";
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateBannerTitle));
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
        // Panels: Formular, aber „angeheftet“ und „kompakt“ aus dem laufenden Zustand übernehmen
        _s.Panels = Panels.Select(p =>
        {
            var c = p.ToConfig();
            if (_app.Settings.Panels.FirstOrDefault(x => x.Id == c.Id) is { } live) { c.Pinned = live.Pinned; }
            return c;
        }).ToList();
        _s.CustomEntries = CustomEntries.Select(c => c.ToEntry()).Where(c => c.Target.Trim().Length > 0).ToList();
        _s.MachineGroups = MachineGroups.Select(g => g.ToGroup()).Where(g => g.Name.Length > 0)
            .GroupBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).ToList();
        _s.ParameterHistory = _app.Settings.ParameterHistory.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
        ApplyWorkspaceRenames();
        _s.RecentRuns = _app.Settings.RecentRuns.ToList();
        _s.CustomOrder = new Dictionary<string, List<string>>(_app.Settings.CustomOrder);
        _s.Fingerprints = new Dictionary<string, string>(_app.Settings.Fingerprints);
        // In der Liste entfernte Gruppen löschen (Einträge kommen zurück in ihren Ordner)
        foreach (var removed in GroupManager.All(_s).Where(g => !Categories.Any(c => c.Name.Equals(g, StringComparison.CurrentCultureIgnoreCase))).ToList())
            GroupManager.Delete(_s, removed);
        _s.CategoryOrder = Categories.Select(c => c.Name).ToList();
        _s.Machines = _app.Settings.Machines
            .Where(m => !_machinesCleared || m.Favorite)
            .Select(m => new MachineEntry { Name = m.Name, Favorite = m.Favorite, LastUsedUtc = m.LastUsedUtc })
            .ToList();
        return _s;
    }
}
