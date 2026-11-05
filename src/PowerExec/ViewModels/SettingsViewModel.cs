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
        _extensions = ExtensionList.Format(f.Extensions);
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

        LoadRows();
        RowsView = System.Windows.Data.CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => o is EntryRowViewModel r && Matches(r);
        _isExpanded = Rows.Any(r => r.IsNew);
    }

    /// <summary>Gefundene Einträge (auch ausgeblendete) und per Muster ausgeschlossene Dateien laden.</summary>
    private void LoadRows()
    {
        Rows.Clear();
        var f = Config;
        foreach (var item in _app.Repository.Items.Where(i => i.RootFolder.Equals(f.Path, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(i => FolderVisibility.RelativePath(f, i.FullPath), StringComparer.CurrentCultureIgnoreCase))
            Rows.Add(new EntryRowViewModel(this, _app, _settings, item, item.FullPath, FolderVisibility.RelativePath(f, item.FullPath), null));
        var status = _app.Repository.FolderStatuses.FirstOrDefault(st => st.Path.Equals(f.Path, StringComparison.OrdinalIgnoreCase));
        if (status != null)
            foreach (var ex in status.ExcludedFiles.OrderBy(x => x.RelativePath, StringComparer.CurrentCultureIgnoreCase))
                Rows.Add(new EntryRowViewModel(this, _app, _settings, null, ex.FullPath, ex.RelativePath, ex.Pattern));
    }

    /// <summary>Nach dem Einlesen (live übernommene Ordner, Muster) die Tabelle aktualisieren.</summary>
    public void ReloadRows()
    {
        if (!Path.Equals(Config.Path, StringComparison.OrdinalIgnoreCase)) Config.Path = Path;
        LoadRows();
        RowsView.Refresh();
        foreach (var p in new[] { nameof(HasNoEntries), nameof(NoRows) }) OnPropertyChanged(p);
        OnRowsChanged();
    }

    /// <summary>Arbeitskopie der Ordner-Einstellungen (Haken, bekannte Dateien, Modus).</summary>
    public FolderConfig Config { get; }

    public string Path { get => _path; set => Set(ref _path, value?.Trim() ?? string.Empty); }
    public bool Recursive { get => _recursive; set { if (Set(ref _recursive, value)) OnPropertyChanged(nameof(Summary)); } }

    /// <summary>Leer = Standard-Dateiendungen.</summary>
    public string Extensions { get => _extensions; set { if (Set(ref _extensions, value ?? string.Empty)) OnPropertyChanged(nameof(ExtensionsError)); } }
    /// <summary>Hinweis bei falscher Schreibweise (Endungen mit Semikolon trennen).</summary>
    public string? ExtensionsError => ExtensionList.Error(_extensions);
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

    /// <summary>Unterordner im Panel als Untergruppen anzeigen (nur mit „Unterordner einbeziehen“).</summary>
    public bool SubfoldersAsGroups
    {
        get => Config.SubfoldersAsGroups;
        set { Config.SubfoldersAsGroups = value; OnPropertyChanged(); }
    }

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
        var w = new Views.ProfilesWindow(vm, _app);
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
        c.Extensions = ExtensionList.Parse(Extensions);
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

/// <summary>Eine Gruppe in den Einstellungen (Inhalte › Gruppen); smarte Gruppen haben einen Ordner.</summary>
public sealed class CategoryItemViewModel : ObservableObject
{
    private string _icon = string.Empty;
    private string _name;
    private FolderViewModel? _folder;
    private bool _isExpanded;

    public CategoryItemViewModel(string name, Action<CategoryItemViewModel, int> move, Action<CategoryItemViewModel> remove,
        string icon = "", Action<CategoryItemViewModel>? chooseIcon = null, Action<CategoryItemViewModel>? rename = null)
    {
        _name = name;
        _icon = icon;
        ChooseIconCommand = new RelayCommand(() => chooseIcon?.Invoke(this), () => chooseIcon != null);
        RenameCommand = new RelayCommand(() => rename?.Invoke(this), () => rename != null);
        UpCommand = new RelayCommand(() => move(this, -1));
        DownCommand = new RelayCommand(() => move(this, 1));
        RemoveCommand = new RelayCommand(() => remove(this));
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public string Name
    {
        get => _name;
        set
        {
            if (!Set(ref _name, value)) return;
            foreach (var p in new[] { nameof(Display), nameof(Indent), nameof(CardMargin), nameof(ColorOwn), nameof(Colors), nameof(Swatch), nameof(Layout) })
                OnPropertyChanged(p);
        }
    }
    public string Display => IsFixed ? FixedGroups.Title(Name) : GroupManager.LeafOf(Name);

    /// <summary>Feste Gruppe (Zuletzt ausgeführt, Favoriten, Eigene Einträge): verschiebbar, nicht löschbar, keine Untergruppen.</summary>
    public bool IsFixed { get; init; }
    public string FixedGlyph => FixedGroups.Glyph(Name);
    public string FixedDescription => FixedGroups.Description(Name);
    public bool IsRecentFixed => IsFixed && Name == FixedGroups.Recent;
    public bool IsCustomFixed => IsFixed && Name == FixedGroups.Custom;
    public string Icon { get => _icon; set { if (Set(ref _icon, value ?? string.Empty)) OnPropertyChanged(nameof(HasIcon)); } }
    public bool HasIcon => _icon.Length > 0;
    public Thickness Indent => new(GroupManager.DepthOf(Name) * 22, 0, 0, 0);
    public Thickness CardMargin => new(GroupManager.DepthOf(Name) * 28, 0, 0, 6);

    /// <summary>Ordner der smarten Gruppe (null = normale Gruppe).</summary>
    public FolderViewModel? Folder
    {
        get => _folder;
        set { if (Set(ref _folder, value)) { OnPropertyChanged(nameof(IsSmart)); OnPropertyChanged(nameof(KindText)); } }
    }
    public bool IsSmart => _folder != null;
    public string KindText => _folder != null ? "Smarte Gruppe · " + _folder.Path : "Gruppe";
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    // ---- Verschachtelung: Untergruppen als Karten in der Karte -----------------------

    /// <summary>Untergruppen (aus der flachen Liste abgeleitet, siehe <c>SettingsViewModel.RebuildCategoryTree</c>).</summary>
    public ObservableCollection<CategoryItemViewModel> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;
    internal void RaiseChildrenChanged() => OnPropertyChanged(nameof(HasChildren));

    /// <summary>Rechtsklick › „Neue Untergruppe …“.</summary>
    internal Action<CategoryItemViewModel>? NewSubgroupRequested { get; set; }
    public ICommand NewSubgroupCommand => new RelayCommand(() => NewSubgroupRequested?.Invoke(this));

    // ---- Gruppen ziehen: Lücke davor, dahinter oder als letzte Untergruppe -------------

    private GroupDropSlot _dropSlot;
    private bool _isDragSource;
    public GroupDropSlot DropSlot
    {
        get => _dropSlot;
        internal set
        {
            if (!Set(ref _dropSlot, value)) return;
            OnPropertyChanged(nameof(GapBefore));
            OnPropertyChanged(nameof(GapAfter));
            OnPropertyChanged(nameof(GapInside));
        }
    }
    public bool GapBefore => _dropSlot == GroupDropSlot.Before;
    public bool GapAfter => _dropSlot == GroupDropSlot.After;
    /// <summary>Gruppe wird hineingezogen: Lücke am Ende der Untergruppen, Rahmen hervorgehoben.</summary>
    public bool GapInside => _dropSlot == GroupDropSlot.Inside;
    /// <summary>Wird gerade gezogen (samt Untergruppen): macht an der alten Stelle Platz.</summary>
    public bool IsDragSource { get => _isDragSource; internal set => Set(ref _isDragSource, value); }

    public ICommand ChooseIconCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ToggleCommand { get; }

    // ---- Farbe und Darstellung -------------------------------------------------------

    /// <summary>Arbeitskopie der Einstellungen (Farbe/Darstellung werden dort direkt gesetzt).</summary>
    public AppSettings? Settings { get; init; }

    public static IReadOnlyList<KeyValuePair<string, string>> ColorList { get; } =
        new[] { new KeyValuePair<string, string>(string.Empty, "Keine / wie übergeordnet") }
            .Concat(PanelColors.AccentPresets.Select(p => new KeyValuePair<string, string>(p.Hex, p.Name))).ToList();

    /// <summary>Palette plus eine eigene Farbe, falls gesetzt.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Colors =>
        ColorOwn.Length > 0 && !ColorList.Any(c => c.Key.Equals(ColorOwn, StringComparison.OrdinalIgnoreCase))
            ? ColorList.Append(new KeyValuePair<string, string>(ColorOwn, "Eigene " + ColorOwn)).ToList()
            : ColorList;

    public string ColorOwn
    {
        get => Settings != null ? GroupManager.OwnColorOf(Settings, Name) : string.Empty;
        set
        {
            if (Settings == null) return;
            GroupManager.SetColor(Settings, Name, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Colors));
            OnPropertyChanged(nameof(Swatch));
        }
    }

    public System.Windows.Media.Brush Swatch =>
        Settings != null && PanelColors.TryParse(GroupManager.ColorOf(Settings, Name), out var c)
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B))
            : System.Windows.Media.Brushes.Transparent;

    public ICommand CustomColorCommand => new RelayCommand(() =>
    {
        var t = Views.ColorPickerWindow.Pick($"Farbe für „{Display}“", ColorOwn,
            Application.Current.Windows.OfType<Views.SettingsWindow>().FirstOrDefault());
        if (t != null) ColorOwn = t;
    });

    public static IReadOnlyList<KeyValuePair<string, string>> LayoutList { get; } = new[]
    {
        new KeyValuePair<string, string>("panel", "Wie das Panel"),
        new KeyValuePair<string, string>("List", "Liste"),
        new KeyValuePair<string, string>("Grid", "Raster")
    };

    public string Layout
    {
        get => Settings != null && GroupManager.LayoutOf(Settings, Name) is { } l ? l.ToString() : "panel";
        set
        {
            if (Settings == null) return;
            GroupManager.SetLayout(Settings, Name, value switch { "Grid" => ItemLayout.Grid, "List" => ItemLayout.List, _ => null });
            OnPropertyChanged();
        }
    }
}

/// <summary>Gruppe in der Auswahl eines Arbeitsbereichs (sichtbar oder ausgeblendet).</summary>
public sealed class WorkspaceGroupChoice : ObservableObject
{
    private readonly Workspace _ws;
    private readonly Action _changed;

    public WorkspaceGroupChoice(Workspace ws, string path, string icon, Action changed)
    {
        _ws = ws;
        Path = path;
        Icon = icon;
        _changed = changed;
    }

    public string Path { get; }
    public string Icon { get; }
    public string Display => IsFixed ? FixedGroups.Title(Path) : GroupManager.LeafOf(Path);
    public Thickness Indent => new(IsFixed ? 0 : GroupManager.DepthOf(Path) * 22, 0, 0, 0);
    /// <summary>Feste Gruppe (Schloss-Symbol): nur ein-/ausblendbar.</summary>
    public bool IsFixed => FixedGroups.IsFixed(Path);
    public string FixedGlyph => FixedGroups.Glyph(Path);

    /// <summary>Die Gruppe selbst ist nicht ausgeblendet.</summary>
    public bool IsVisible
    {
        get => !_ws.HiddenGroups.Contains(Path, StringComparer.CurrentCultureIgnoreCase);
        set { WorkspaceManager.SetGroupHidden(_ws, Path, !value); OnPropertyChanged(); _changed(); }
    }

    /// <summary>Übergeordnete Gruppe ist sichtbar (sonst ist diese automatisch mit ausgeblendet).</summary>
    public bool IsEnabled => GroupManager.ParentOf(Path) is not { } parent || WorkspaceManager.ShowsGroupIn(_ws, parent);

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsEnabled));
    }
}

public sealed record FolderTimeInfo(string Path, string Text, bool IsProblem);

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly AppSettings _s;
    private string _section = "general";
    private string _defaultExtensions;
    private string _globalExcludes;
    private string _updateStatus = string.Empty;
    private string? _hotkeyError;
    private bool _machinesCleared;

    public SettingsViewModel(AppController app, string? focusFolder)
    {
        _app = app;
        _s = SettingsStore.Clone(app.Settings);
        _defaultExtensions = ExtensionList.Format(_s.DefaultExtensions);
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

        ClearMachinesCommand = new RelayCommand(() => { MachineList.ClearRecent(_s); _machinesCleared = true; OnPropertyChanged(nameof(MachinesText)); });
        CheckUpdatesCommand = new RelayCommand(() => _ = CheckUpdatesAsync(), () => !_isChecking);
        InstallUpdateCommand = new RelayCommand(() => _ = _app.InstallUpdateAsync(), () => HasUpdate && !_app.Update.Busy);
        ReleaseNotesCommand = new RelayCommand(() => { if (_app.Updates.Available is { } r) Launcher.OpenUrl(r.HtmlUrl); });
        ShowWelcomeCommand = new RelayCommand(() => _app.ShowWelcome());
        CurrentReleaseNotesCommand = new RelayCommand(() => Launcher.OpenUrl(
            $"https://github.com/{UpdateChecker.Owner}/{UpdateChecker.Repo}/releases/tag/v{UpdateService.CurrentVersionText}"));
        _app.Update.PropertyChanged += OnUpdateChanged;
        _app.Repository.Changed += OnRepositoryChanged;
        AppLog.Changed += OnLogChanged;
        OpenLogFolderCommand = new RelayCommand(() => Launcher.OpenLocation(_s.EffectiveLogFolder, isFolder: true));
        OpenRepoCommand = new RelayCommand(() => Launcher.OpenUrl($"https://github.com/{UpdateChecker.Owner}/{UpdateChecker.Repo}"));
        BrowseEditorCommand = new RelayCommand(BrowseEditor);
        BrowseLogFolderCommand = new RelayCommand(BrowseLogFolder);
        SelectSectionCommand = new RelayCommand(p => Section = p?.ToString() ?? "general");

        foreach (var c in GroupManager.All(_s))
        {
            var cat = NewCategoryVm(c, GroupManager.IconOf(_s, c).ToString());
            cat.Folder = Folders.FirstOrDefault(f => f.Config.Group.Equals(c, StringComparison.CurrentCultureIgnoreCase));
            Categories.Add(cat);
        }
        Categories.CollectionChanged += (_, _) => RebuildCategoryTree();
        RebuildCategoryTree();
        if (focusFolder != null) FocusGroup(focusFolder);
        AddCategoryCommand = new RelayCommand(AddCategory, () => !string.IsNullOrWhiteSpace(NewCategory));

        if (_s.Panels.Count == 0) _s.Panels.Add(new PanelConfig());
        Panel = new PanelSettingsViewModel(_s.Panels[0], _monitors);
        EntryList = new CustomEntryListViewModel(app, _s, () => Categories.Select(c => c.Name).ToList());
        TagList = new TagsViewModel(app, _s, () => EntryList.ApplyTo(_s), () => EntryList.ReloadTags());
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

    }

    // ---- Suche in den Einstellungen ---------------------------------------------

    public sealed record SearchHit(string Section, string Page, string Title, string Keywords);

    private static readonly (string Section, string Page, string Segment)[] Pages =
    {
        ("general", "Allgemein", ""),
        ("groups", "Gruppen", "content"), ("custom", "Eigene Einträge", "content"), ("tags", "Schlagworte", "content"), ("workspaces", "Arbeitsbereiche", "content"),
        ("look", "Darstellung", "panel"), ("behavior", "Verhalten", "panel"),
        ("exec", "PowerShell", "exec"), ("remote", "Remote", "exec"), ("machines", "Rechnergruppen", "exec"),
        ("diag", "Diagnose", "help")
    };

    private static readonly SearchHit[] Index =
    {
        new("general", "Allgemein", "Version und Updates", "update aktualisieren version vorabversion beta github"),
        new("general", "Allgemein", "Versionshinweise", "release notes neuerungen änderungen"),
        new("general", "Allgemein", "Mit Windows starten", "autostart anmeldung start"),
        new("general", "Allgemein", "Einstellungen sichern", "export import backup datei"),
        new("groups", "Gruppen", "Gruppen und Untergruppen", "gruppe untergruppe reihenfolge kategorie"),
        new("groups", "Gruppen", "Smarte Gruppe (Ordner)", "ordner pfad freigabe unc netzwerk scan smart"),
        new("groups", "Gruppen", "Gruppensymbole", "icon symbol fluent heroicons emoji"),
        new("groups", "Gruppen", "Favoriten", "stern favorit anzeigen"),
        new("groups", "Gruppen", "Zuletzt ausgeführt", "verlauf anzahl recent"),
        new("groups", "Gruppen", "Unterordner als Untergruppen", "rekursiv unterordner"),
        new("groups", "Gruppen", "Standard-Dateiendungen", "ps1 exe cmd endung"),
        new("groups", "Gruppen", "Globale Ausschlüsse", "ausschließen muster"),
        new("groups", "Gruppen", "Automatisch neu einlesen", "dateiwächter rescan"),
        new("groups", "Gruppen", "Zeitlimit für nicht erreichbare Ordner", "timeout offline"),
        new("groups", "Gruppen", "Neue Dateien freigeben", "freigabe sichtbar haken"),
        new("custom", "Eigene Einträge", "Eigene Einträge verwalten", "webseite url ordner rdp ssh snippet werkzeug befehl datei"),
        new("custom", "Eigene Einträge", "Aus Vorlagen", "katalog mmc systemsteuerung ms-settings"),
        new("custom", "Eigene Einträge", "Erreichbarkeit prüfen", "health tote links"),
        new("custom", "Eigene Einträge", "Importieren / Exportieren", "teilen kollegen datei"),
        new("tags", "Schlagworte", "Schlagworte verwalten", "tag umbenennen zusammenführen löschen farbe #"),
        new("workspaces", "Arbeitsbereiche", "Arbeitsbereiche", "kunde profil wechseln"),
        new("workspaces", "Arbeitsbereiche", "Gruppen je Arbeitsbereich", "ausblenden sichtbar auswahl"),
        new("look", "Darstellung", "Design", "hell dunkel theme farbe"),
        new("look", "Darstellung", "Animation", "ausfahren sanft schnell"),
        new("look", "Darstellung", "Symbole anzeigen", "icons favicon programmsymbol"),
        new("look", "Darstellung", "Liste oder Raster", "kacheln ansicht"),
        new("look", "Darstellung", "Elementgröße", "klein mittel groß kompakt"),
        new("look", "Darstellung", "Panelfarbe und Deckkraft", "farbe transparenz"),
        new("look", "Darstellung", "Panelgröße", "breite höhe lage"),
        new("look", "Darstellung", "Hover-Zone", "rand monitor breite länge auslöser links rechts oben unten"),
        new("behavior", "Verhalten", "Verweilzeit bis zum Ausfahren", "hover verzögerung"),
        new("behavior", "Verhalten", "Einklappen nach Verlassen", "schließen verzögerung"),
        new("behavior", "Verhalten", "Bei Vollbild-Programmen ausblenden", "video präsentation spiel rdp"),
        new("behavior", "Verhalten", "Gruppen ziehen: Ziehbild am Mauszeiger", "drag drop verschieben gruppe vorschau"),
        new("behavior", "Verhalten", "Gruppen ziehen: Lücke weich öffnen", "drag drop verschieben animation platzhalter"),
        new("behavior", "Verhalten", "Gruppen ziehen: Inhalte zuklappen", "drag drop verschieben kompakt karten"),
        new("behavior", "Verhalten", "Gruppen ziehen: Begründung an ungültigen Stellen", "drag drop verschieben hinweis fehler"),
        new("behavior", "Verhalten", "Tastenkürzel", "hotkey shortcut"),
        new("exec", "PowerShell", "Standard-PowerShell", "pwsh 7 5.1 version"),
        new("exec", "PowerShell", "Execution Policy", "bypass remotesigned"),
        new("exec", "PowerShell", "Ohne Profil / Konsole offen lassen", "noprofile noexit"),
        new("exec", "PowerShell", "Editor", "vscode ise notepad"),
        new("remote", "Remote", "PowerShell bei Remote", "winrm 5.1 7"),
        new("remote", "Remote", "HTTPS (Port 5986)", "ssl winrm"),
        new("remote", "Remote", "Gleichzeitige Verbindungen und Timeout", "throttle masse"),
        new("remote", "Remote", "Benachrichtigung und Log-Ordner", "protokoll fertig"),
        new("remote", "Remote", "Zuletzt verwendete Rechner", "rechnerliste leeren"),
        new("machines", "Rechnergruppen", "Rechnergruppen", "gruppe csv import ad active directory"),
        new("diag", "Diagnose", "Protokoll", "log fehler kopieren"),
        new("diag", "Diagnose", "Einlesezeiten der Ordner", "scan dauer"),
    };

    // ---- Navigation: einklappbare Bereiche --------------------------------------

    private bool _navContent = true, _navPanel = true, _navExec = true, _navHelp = true;
    public bool NavContent { get => _navContent; set => Set(ref _navContent, value); }
    public bool NavPanel { get => _navPanel; set => Set(ref _navPanel, value); }
    public bool NavExec { get => _navExec; set => Set(ref _navExec, value); }
    public bool NavHelp { get => _navHelp; set => Set(ref _navHelp, value); }
    public ICommand ToggleNavCommand => new RelayCommand(p =>
    {
        switch (p?.ToString())
        {
            case "content": NavContent = !NavContent; break;
            case "panel": NavPanel = !NavPanel; break;
            case "exec": NavExec = !NavExec; break;
            case "help": NavHelp = !NavHelp; break;
        }
    });

    /// <summary>Bereich der gewählten Seite aufklappen (z. B. nach Suche).</summary>
    private void NavExpandFor(string section)
    {
        switch (Pages.FirstOrDefault(x => x.Section == section).Segment)
        {
            case "content": NavContent = true; break;
            case "panel": NavPanel = true; break;
            case "exec": NavExec = true; break;
            case "help": NavHelp = true; break;
        }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value ?? string.Empty)) { OnPropertyChanged(nameof(SearchResults)); OnPropertyChanged(nameof(HasSearchResults)); } }
    }

    public IReadOnlyList<SearchHit> SearchResults
    {
        get
        {
            var words = _searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return Array.Empty<SearchHit>();
            return Index.Where(h => words.All(w => (h.Title + " " + h.Keywords + " " + h.Page).Contains(w, StringComparison.CurrentCultureIgnoreCase))).Take(12).ToList();
        }
    }
    public bool HasSearchResults => SearchResults.Count > 0;

    public ICommand OpenSearchHitCommand => new RelayCommand(p =>
    {
        if (p is SearchHit h) { Section = h.Section; SearchText = string.Empty; }
    });

    public string PageTitle => Pages.FirstOrDefault(x => x.Section == _section).Page ?? string.Empty;

    public string Section
    {
        get => _section;
        set
        {
            if (!Set(ref _section, value)) return;
            OnPropertyChanged(nameof(PageTitle));
            if (value == "workspaces") { ApplyWorkspaceRenames(); LoadWorkspaces(); }
            if (value == "tags") { EntryList.ApplyTo(_s); TagList.Load(); }
            NavExpandFor(value);
        }
    }

    // Ordner
    public ObservableCollection<FolderViewModel> Folders { get; } = new();
    public string DefaultExtensions { get => _defaultExtensions; set { if (Set(ref _defaultExtensions, value ?? string.Empty)) OnPropertyChanged(nameof(DefaultExtensionsError)); } }
    public string? DefaultExtensionsError => ExtensionList.Error(_defaultExtensions);
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

    public ThemeMode Theme { get => _s.Theme; set { _s.Theme = value; OnPropertyChanged(); RaisePresets(); } }
    public IReadOnlyList<KeyValuePair<ThemeMode, string>> Themes { get; } = new[]
    {
        new KeyValuePair<ThemeMode, string>(ThemeMode.System, "Wie Windows"),
        new KeyValuePair<ThemeMode, string>(ThemeMode.Dark, "Dunkel"),
        new KeyValuePair<ThemeMode, string>(ThemeMode.Light, "Hell")
    };

    // ---- Darstellung: Akzent, Mica, Größe, Vorlagen ---------------------------------

    public bool UseWindowsAccent { get => _s.UseWindowsAccent; set { _s.UseWindowsAccent = value; OnPropertyChanged(); RaisePresets(); } }

    /// <summary>Mica als Hintergrund von Einstellungen und Dialogen (Windows 11 22H2+).</summary>
    public bool UseMica
    {
        get => _s.WindowBackdrop == WindowBackdrop.Mica;
        set { _s.WindowBackdrop = value ? WindowBackdrop.Mica : WindowBackdrop.Solid; OnPropertyChanged(); RaisePresets(); }
    }
    public bool IsMicaSupported => Views.WindowHelper.IsMicaSupported;
    public string MicaDescription => IsMicaSupported
        ? "Einstellungen und Dialoge nehmen die Desktopfarbe dezent auf (wie der Explorer). Gilt für neu geöffnete Fenster."
        : "Benötigt Windows 11 (22H2 oder neuer). Für das Panel gibt es Acrylic unter „Panel“.";

    public int UiScalePercent { get => _s.UiScalePercent; set { _s.UiScalePercent = Math.Clamp(value / 10 * 10, Appearance.MinScale, Appearance.MaxScale); OnPropertyChanged(); } }

    public bool IsHighContrast => Services.ThemeService.IsHighContrast;

    /// <summary>Designvorlagen: Design, Panelfarbe, Deckkraft, Acrylic und Akzent mit einem Klick.</summary>
    public IReadOnlyList<DesignPresetChoice> DesignPresets => Appearance.Presets.Select(p => new DesignPresetChoice(p,
        CurrentPresetKey == p.Key, new RelayCommand(() => ApplyPreset(p)))).ToList();

    private string? CurrentPresetKey
    {
        get
        {
            var probe = SettingsStore.Clone(_s);
            probe.Panels = new List<PanelConfig> { Panel.ToConfig() };
            return Appearance.Current(probe)?.Key;
        }
    }

    private void ApplyPreset(DesignPreset p)
    {
        Theme = p.Theme;
        UseWindowsAccent = p.WindowsAccent;
        UseMica = p.Backdrop == WindowBackdrop.Mica;
        Panel.PanelColor = p.PanelColor;
        Panel.PanelOpacity = p.Opacity;
        Panel.Acrylic = p.Acrylic;
        RaisePresets();
    }

    private void RaisePresets() => OnPropertyChanged(nameof(DesignPresets));

    public ICommand ShowWelcomeCommand { get; }

    // ---- Team-Kataloge (live, nicht über „Änderungen verwerfen“) -----------------------

    public IReadOnlyList<CatalogRow> CatalogRows => _app.Settings.Catalogs.Select(c => new CatalogRow(_app, c, () => OnPropertyChanged(nameof(CatalogRows)))).ToList();
    public bool HasCatalogs => _app.Settings.Catalogs.Count > 0;
    private string? _catalogError;
    public string? CatalogError { get => _catalogError; private set => Set(ref _catalogError, value); }

    public ICommand SubscribeCatalogCommand => _subscribeCatalog ??= new RelayCommand(async () =>
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Team-Katalog abonnieren (Exportdatei, z. B. auf einer Freigabe)", Filter = EntryShare.FileFilter };
        if (dlg.ShowDialog() != true) return;
        CatalogError = await _app.SubscribeCatalogAsync(dlg.FileName);
        RaiseCatalogs();
    });
    public ICommand CheckCatalogsCommand => _checkCatalogs ??= new RelayCommand(async () => { await _app.CheckCatalogsAsync(); RaiseCatalogs(); });
    private ICommand? _subscribeCatalog, _checkCatalogs;

    private void RaiseCatalogs()
    {
        OnPropertyChanged(nameof(CatalogRows));
        OnPropertyChanged(nameof(HasCatalogs));
    }

    public string? HotkeyError { get => _hotkeyError; set => Set(ref _hotkeyError, value); }
    public bool StartWithWindows { get => _s.StartWithWindows; set { _s.StartWithWindows = value; OnPropertyChanged(); } }

    // ---- Panels ------------------------------------------------------------

    private readonly IReadOnlyList<KeyValuePair<int, string>> _monitors;

    /// <summary>Das (einzige) Panel.</summary>
    public PanelSettingsViewModel Panel { get; }

    // ---- Besondere Gruppen (Favoriten, Zuletzt ausgeführt) ----------------------

    public bool ShowFavorites { get => Panel.ShowFavorites; set { Panel.ShowFavorites = value; OnPropertyChanged(); } }
    public bool ShowRecent { get => Panel.ShowRecent; set { Panel.ShowRecent = value; OnPropertyChanged(); } }

    // ---- Eigene Einträge ------------------------------------------------------

    /// <summary>Liste „Eigene Einträge“ (Suche, Filter, Mehrfachauswahl).</summary>
    public CustomEntryListViewModel EntryList { get; }

    /// <summary>Schlagwort-Verwaltung.</summary>
    public TagsViewModel TagList { get; }

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

    /// <summary>Dynamische Gruppen: wie oft im Hintergrund aus dem AD aktualisieren.</summary>
    public int AdSyncIntervalHours { get => _s.AdSyncIntervalHours; set { _s.AdSyncIntervalHours = Math.Clamp(value, 1, 168); OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<int, string>> AdSyncIntervals { get; } = new[]
    {
        new KeyValuePair<int, string>(1, "jede Stunde"), new KeyValuePair<int, string>(2, "alle 2 Stunden"),
        new KeyValuePair<int, string>(4, "alle 4 Stunden"), new KeyValuePair<int, string>(8, "alle 8 Stunden"),
        new KeyValuePair<int, string>(12, "alle 12 Stunden"), new KeyValuePair<int, string>(24, "einmal am Tag")
    };

    /// <summary>Hintergrund-Synchronisierung fertig: gefundene Rechner in die offenen Gruppen übernehmen.</summary>
    public void ApplyMachineSync()
    {
        foreach (var vm in MachineGroups)
            if (_app.Settings.MachineGroups.FirstOrDefault(g => g.Name.Equals(vm.Name.Trim(), StringComparison.CurrentCultureIgnoreCase)) is { } g)
                vm.ApplySynced(g);
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
        var groups = Categories.Select(c => c.Name).ToList();
        foreach (var w in _s.Workspaces)
        {
            bool active = w.Name.Equals(_s.ActiveWorkspace, StringComparison.CurrentCultureIgnoreCase);
            int favs = active ? _app.Settings.Favorites.Count : w.Favorites.Count;
            int hidden = groups.Count(g => !WorkspaceManager.ShowsGroupIn(w, g));
            var summary = $"{groups.Count - hidden} von {groups.Count} Gruppen · {favs} Favoriten" + (active ? " · aktiv" : string.Empty);
            var row = new WorkspaceRowViewModel(w.Name, active, summary,
                r => SwitchWorkspaceRequested?.Invoke(r.OriginalName),
                RemoveWorkspace);
            foreach (var key in FixedGroups.Keys)
                row.Groups.Add(new WorkspaceGroupChoice(w, key, string.Empty, () => { foreach (var x in row.Groups) x.Refresh(); }));
            foreach (var g in groups)
                row.Groups.Add(new WorkspaceGroupChoice(w, g, Categories.First(c => c.Name == g).Icon, () => { foreach (var x in row.Groups) x.Refresh(); }));
            Workspaces.Add(row);
        }
    }

    private void AddWorkspace()
    {
        ApplyWorkspaceRenames();
        // Aktuelle Formularwerte als Grundlage für eine Kopie verwenden
        _s.Favorites = new List<string>(_app.Settings.Favorites);
        WorkspaceManager.Create(_s, NewWorkspace, CopyWorkspace);
        NewWorkspace = string.Empty;
        LoadWorkspaces();
    }

    private void RemoveWorkspace(WorkspaceRowViewModel w)
    {
        if (MessageBox.Show($"Arbeitsbereich „{w.OriginalName}“ mit seinen Favoriten, Startprofilen und der Gruppenauswahl löschen?\nGruppen und Einträge bleiben erhalten.", "Arbeitsbereich löschen",
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

    // Gruppen ziehen
    public bool GroupDragCollapse { get => _s.GroupDragCollapse; set { _s.GroupDragCollapse = value; OnPropertyChanged(); } }
    public bool GroupDragExplain { get => _s.GroupDragExplain; set { _s.GroupDragExplain = value; OnPropertyChanged(); } }
    public bool GroupDragGhost { get => _s.GroupDragGhost; set { _s.GroupDragGhost = value; OnPropertyChanged(); } }
    public bool GroupDragAnimate { get => _s.GroupDragAnimate; set { _s.GroupDragAnimate = value; OnPropertyChanged(); } }
    /// <summary>Weiche Bewegung der Lücke im Einstellungsfenster selbst (folgt auch der Einstellung „Animation“).</summary>
    public bool AnimateDropGap => _s.GroupDragAnimate && _s.Animation != AnimationMode.Off;
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

    /// <summary>Gruppen der obersten Ebene; Untergruppen hängen als <see cref="CategoryItemViewModel.Children"/> darunter.</summary>
    public ObservableCollection<CategoryItemViewModel> CategoryRoots { get; } = new();
    private bool _treeSuspended;

    private CategoryItemViewModel NewCategoryVm(string name, string icon = "") =>
        new(name, MoveCategory, RemoveCategory, icon, ChooseCategoryIcon, RenameCategory) { Settings = _s };

    /// <summary>Karten der festen Gruppen (stehen nicht in <see cref="Categories"/>, nur in <see cref="CategoryRoots"/>).</summary>
    private readonly Dictionary<string, CategoryItemViewModel> _fixedVms = new();

    private CategoryItemViewModel FixedVm(string key)
    {
        if (!_fixedVms.TryGetValue(key, out var vm))
            _fixedVms[key] = vm = new CategoryItemViewModel(key, (_, _) => { }, _ => { }) { Settings = _s, IsFixed = true };
        return vm;
    }

    /// <summary>Baum aus der flachen Liste ableiten (abgleichen statt neu aufbauen, damit Karten erhalten bleiben).</summary>
    private void RebuildCategoryTree()
    {
        if (_treeSuspended) return;
        var children = Categories.ToDictionary(c => c, _ => new List<CategoryItemViewModel>(), ReferenceEqualityComparer.Instance);
        var roots = new List<CategoryItemViewModel>();
        foreach (var c in Categories)
        {
            c.NewSubgroupRequested = x => AddGroupPrompt(x.Name);
            var parentPath = GroupManager.ParentOf(c.Name);
            var parent = parentPath == null ? null : Categories.FirstOrDefault(x => x.Name.Equals(parentPath, StringComparison.CurrentCultureIgnoreCase));
            if (parent == null) roots.Add(c);
            else ((List<CategoryItemViewModel>)children[parent]).Add(c);
        }
        // Feste Gruppen an ihrer Position zwischen den Gruppen der obersten Ebene
        var byName = roots.ToDictionary(r => r.Name, StringComparer.CurrentCultureIgnoreCase);
        var composed = FixedGroups.Compose(_s, roots.Select(r => r.Name).ToList())
            .Select(k => FixedGroups.IsFixed(k) ? FixedVm(k) : byName[k]).ToList();
        Core.Collections.CollectionSync.SyncTo(CategoryRoots, composed);
        foreach (var c in Categories)
        {
            Core.Collections.CollectionSync.SyncTo(c.Children, (List<CategoryItemViewModel>)children[c]);
            c.RaiseChildrenChanged();
        }
    }
    private string _newCategory = string.Empty;
    public string NewCategory { get => _newCategory; set => Set(ref _newCategory, value ?? string.Empty); }
    public ICommand AddCategoryCommand { get; }

    private void ChooseCategoryIcon(CategoryItemViewModel c)
    {
        var picked = Views.IconPickerWindow.Pick(IconRef.Parse(c.Icon), $"Symbol für „{c.Display}“");
        if (picked == null) return;
        GroupManager.SetIcon(_s, c.Name, picked.Value);
        c.Icon = picked.Value.ToString();
    }

    /// <summary>Drag &amp; Drop in den Einstellungen: nur unter Geschwistern (gleiche übergeordnete Gruppe).</summary>
    // ---- Gruppen ziehen (Lücke an der Zielstelle; hier nur Reihenfolge unter Geschwistern) ----------------

    private CategoryItemViewModel? _dragCategory;
    private GroupDropPlan? _categoryPlan;
    private Dictionary<CategoryItemViewModel, bool>? _expandedBeforeDrag;
    private string _categoryDragLabel = string.Empty;
    private Brush? _categoryDragBrush;

    public string CategoryDragLabel { get => _categoryDragLabel; private set { _categoryDragLabel = value; OnPropertyChanged(); } }
    public Brush? CategoryDragBrush { get => _categoryDragBrush; private set { _categoryDragBrush = value; OnPropertyChanged(); } }
    public GroupDropPlan? CurrentCategoryPlan => _categoryPlan;
    public bool IsDraggingCategory => _dragCategory != null;

    private IEnumerable<CategoryItemViewModel> SubtreeOf(CategoryItemViewModel c) =>
        Categories.Where(x => GroupManager.IsSameOrBelow(x.Name, c.Name));

    public void BeginCategoryDrag(CategoryItemViewModel c)
    {
        EndCategoryDrag();
        _dragCategory = c;
        var sub = SubtreeOf(c).Count() - 1;
        CategoryDragLabel = c.Display + (sub > 0 ? $"  + {sub} Untergruppe{(sub == 1 ? "" : "n")}" : string.Empty);
        CategoryDragBrush = c.Swatch;
        OnPropertyChanged(nameof(AnimateDropGap));
        foreach (var x in SubtreeOf(c)) x.IsDragSource = true;
        c.IsDragSource = true; // auch feste Gruppen
        if (_s.GroupDragCollapse)
        {
            _expandedBeforeDrag = Categories.ToDictionary(x => x, x => x.IsExpanded);
            foreach (var x in Categories) x.IsExpanded = false;
        }
    }

    /// <summary>Ziel bestimmen und die Lücke öffnen (über der Zielkarte bzw. unter ihrer letzten Untergruppe).</summary>
    public GroupDropPlan? PreviewCategoryDrop(CategoryItemViewModel hovered, GroupDropZone zone, int levelOffset)
    {
        if (_dragCategory == null) return null;
        if (_dragCategory.IsFixed || hovered.IsFixed) return PreviewTopLevelDrop(hovered, zone);
        var plan = GroupDropPlanner.Plan(Categories.Select(x => x.Name).ToList(), _dragCategory.Name, hovered.Name, zone, levelOffset,
            siblingsOnly: false, exists: p => Categories.Any(x => x.Name.Equals(p, StringComparison.CurrentCultureIgnoreCase)));
        _categoryPlan = plan;
        var target = plan.IsValid ? Categories.FirstOrDefault(x => x.Name.Equals(plan.Target, StringComparison.CurrentCultureIgnoreCase)) : null;
        var slot = plan.Position switch
        {
            GroupDropPosition.Before => GroupDropSlot.Before,
            GroupDropPosition.After => GroupDropSlot.After,
            _ => GroupDropSlot.Inside
        };
        foreach (var x in Categories) x.DropSlot = ReferenceEquals(x, target) ? slot : GroupDropSlot.None;
        foreach (var x in _fixedVms.Values) x.DropSlot = GroupDropSlot.None;
        _topDrop = null;
        return plan;
    }

    /// <summary>Feste Gruppe beteiligt: nur oberste Ebene, vor oder hinter eine Karte (nicht hinein).</summary>
    private (CategoryItemViewModel Anchor, bool After)? _topDrop;

    private GroupDropPlan PreviewTopLevelDrop(CategoryItemViewModel hovered, GroupDropZone zone)
    {
        var dragged = _dragCategory!;
        // Untergruppe als Ziel: ihre Gruppe der obersten Ebene
        var anchor = hovered.IsFixed ? hovered : CategoryRoots.FirstOrDefault(r => !r.IsFixed && GroupManager.IsSameOrBelow(hovered.Name, r.Name)) ?? hovered;
        foreach (var x in Categories.Concat(_fixedVms.Values)) x.DropSlot = GroupDropSlot.None;
        if (zone == GroupDropZone.Into && !hovered.IsFixed && dragged.IsFixed)
        {
            _topDrop = null;
            _categoryPlan = new GroupDropPlan { IsValid = false, Reason = "Feste Gruppen bleiben auf oberster Ebene – vor oder hinter eine Gruppe ziehen." };
            return _categoryPlan;
        }
        if (ReferenceEquals(anchor, dragged))
        {
            _topDrop = null;
            _categoryPlan = new GroupDropPlan { IsValid = true, IsNoOp = true, Target = dragged.Name };
            return _categoryPlan;
        }
        var after = zone != GroupDropZone.Before; // „hinein“ in eine feste Gruppe: dahinter
        anchor.DropSlot = after ? GroupDropSlot.After : GroupDropSlot.Before;
        _topDrop = (anchor, after);
        _categoryPlan = new GroupDropPlan { IsValid = true, Target = anchor.Name, Position = after ? GroupDropPosition.After : GroupDropPosition.Before };
        return _categoryPlan;
    }

    /// <summary>Oberste Ebene neu anordnen: Gruppe bzw. feste Gruppe vor/hinter den Anker; eigene Gruppen werden dafür zur obersten Ebene.</summary>
    private bool CommitTopLevelDrop(CategoryItemViewModel dragged, CategoryItemViewModel anchor, bool after)
    {
        var key = dragged.Name;
        if (!dragged.IsFixed && GroupManager.ParentOf(dragged.Name) != null)
        {
            // Untergruppe neben eine feste Gruppe: erst auf die oberste Ebene holen (ans Ende), dann einsortieren
            var lastRoot = CategoryRoots.LastOrDefault(r => !r.IsFixed && !GroupManager.IsSameOrBelow(r.Name, dragged.Name));
            if (lastRoot == null || !MoveCategoryTo(dragged, new GroupDropPlan { IsValid = true, Target = lastRoot.Name, Position = GroupDropPosition.After })) return false;
            key = dragged.Name;
        }
        var top = CategoryRoots.Select(r => r.Name).Where(n => !n.Equals(key, StringComparison.CurrentCultureIgnoreCase)).ToList();
        var at = top.FindIndex(n => n.Equals(anchor.Name, StringComparison.CurrentCultureIgnoreCase));
        if (at < 0) return false;
        top.Insert(after ? at + 1 : at, key);
        FixedGroups.SetPositions(_s, top);
        // Reihenfolge der eigenen Gruppen (samt Untergruppen) übernehmen
        var userRoots = top.Where(k => !FixedGroups.IsFixed(k)).ToList();
        var order = userRoots.SelectMany(r => Categories.Where(x => GroupManager.IsSameOrBelow(x.Name, r)).Select(x => x.Name)).ToList();
        _treeSuspended = true;
        try { ApplyOrder(order); }
        finally { _treeSuspended = false; }
        RebuildCategoryTree();
        LoadWorkspaces();
        return true;
    }

    public bool CommitCategoryDrop()
    {
        var plan = _categoryPlan;
        var dragged = _dragCategory;
        var top = _topDrop;
        EndCategoryDrag();
        if (top is { } t && dragged != null && plan is { IsValid: true, IsNoOp: false }) return CommitTopLevelDrop(dragged, t.Anchor, t.After);
        if (plan is not { IsValid: true, IsNoOp: false } || dragged == null) return false;
        return MoveCategoryTo(dragged, plan);
    }

    /// <summary>
    /// Gruppe samt Untergruppen verschieben – auch in eine andere Gruppe. Wie im Panel über <see cref="GroupManager.MoveGroup"/>
    /// auf der Arbeitskopie, damit Zuordnungen, Farben, Symbole, Darstellung und Ordner mitwandern.
    /// </summary>
    private bool MoveCategoryTo(CategoryItemViewModel dragged, GroupDropPlan plan)
    {
        _s.CategoryOrder = Categories.Select(x => x.Name).ToList();
        _s.Folders = Folders.Where(f => f.Path.Length > 0).Select(f => f.ToConfig()).ToList();
        var oldPath = dragged.Name;
        var newPath = GroupManager.MoveGroup(_s, oldPath, plan.Target, plan.Position);
        if (newPath == null) return false;
        string Map(string p) => GroupManager.IsSameOrBelow(p, oldPath) ? newPath + p[oldPath.Length..] : p;
        _treeSuspended = true;
        try
        {
            foreach (var x in Categories) x.Name = Map(x.Name);
            foreach (var f in Folders) f.Config.Group = Map(f.Config.Group);
            ApplyOrder(GroupManager.All(_s));
        }
        finally { _treeSuspended = false; }
        RebuildCategoryTree();
        LoadWorkspaces();
        return true;
    }

    public void EndCategoryDrag()
    {
        foreach (var x in Categories.Concat(_fixedVms.Values)) { x.DropSlot = GroupDropSlot.None; x.IsDragSource = false; }
        _topDrop = null;
        if (_expandedBeforeDrag != null)
            foreach (var (x, expanded) in _expandedBeforeDrag) x.IsExpanded = expanded;
        _expandedBeforeDrag = null;
        _dragCategory = null;
        _categoryPlan = null;
    }

    public bool CanDropCategory(CategoryItemViewModel dragged, CategoryItemViewModel target) =>
        !ReferenceEquals(dragged, target) &&
        string.Equals(GroupManager.ParentOf(dragged.Name), GroupManager.ParentOf(target.Name), StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Gruppe vor bzw. hinter eine Geschwistergruppe schieben (Untergruppen wandern mit).</summary>
    public void DropCategory(CategoryItemViewModel dragged, CategoryItemViewModel target, bool after)
    {
        if (!CanDropCategory(dragged, target)) return;
        var tmp = new AppSettings { CategoryOrder = Categories.Select(x => x.Name).ToList() };
        GroupManager.MoveGroup(tmp, dragged.Name, target.Name, after ? GroupDropPosition.After : GroupDropPosition.Before);
        ApplyOrder(GroupManager.All(tmp));
    }

    private void ApplyOrder(IReadOnlyList<string> order)
    {
        for (int target = 0; target < order.Count; target++)
        {
            var current = Categories.ToList().FindIndex(x => x.Name.Equals(order[target], StringComparison.CurrentCultureIgnoreCase));
            if (current >= 0 && current != target) Categories.Move(current, target);
        }
    }

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

    private static Window? OwnerWindow() => Application.Current.Windows.OfType<Views.SettingsWindow>().FirstOrDefault();

    /// <summary>Entfernt die Gruppe samt Untergruppen (nach Rückfrage); smarte Gruppen lesen ihren Ordner nicht mehr ein.</summary>
    private void RemoveCategory(CategoryItemViewModel c)
    {
        var affected = Categories.Where(x => GroupManager.IsSameOrBelow(x.Name, c.Name)).ToList();
        var folders = affected.Where(x => x.Folder != null).Select(x => x.Folder!.Path).ToList();
        var text = $"Gruppe „{c.Display}“" + (affected.Count > 1 ? $" mit {affected.Count - 1} Untergruppe(n)" : string.Empty) + " löschen?" +
                   (folders.Count > 0 ? $"\n\nDer Ordner {string.Join(", ", folders)} wird dann nicht mehr eingelesen. Die Dateien selbst bleiben unverändert." : string.Empty) +
                   "\n\nEinträge, die du in die Gruppe gezogen hattest, erscheinen wieder in ihrer smarten Gruppe bzw. unter „Eigene Einträge“.";
        if (MessageBox.Show(OwnerWindow()!, text, "Gruppe löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        foreach (var x in affected)
        {
            Categories.Remove(x);
            if (x.Folder != null) Folders.Remove(x.Folder);
        }
    }

    /// <summary>Gruppe umbenennen (auch Anzeigename einer smarten Gruppe); Untergruppen, Zuordnungen, Symbole wandern mit.</summary>
    private void RenameCategory(CategoryItemViewModel c)
    {
        var parent = GroupManager.ParentOf(c.Name);
        var newName = Views.InputWindow.Ask("Gruppe umbenennen", c.IsSmart ? "Der Name erscheint im Panel; der Ordner bleibt gleich." : string.Empty, c.Display, "Umbenennen",
            n =>
            {
                var path = parent == null ? GroupManager.CleanName(n) : parent + GroupManager.Separator + GroupManager.CleanName(n);
                return Categories.Any(x => x != c && x.Name.Equals(path, StringComparison.CurrentCultureIgnoreCase)) ? "Eine Gruppe mit diesem Namen gibt es hier schon." : null;
            });
        if (newName == null || newName == c.Display) return;
        var oldPath = c.Name;
        var newPath = parent == null ? GroupManager.CleanName(newName) : parent + GroupManager.Separator + GroupManager.CleanName(newName);
        // Arbeitskopie auf den Stand der Liste bringen, dann wie im Panel umbenennen
        _s.CategoryOrder = Categories.Select(x => x.Name).ToList();
        _s.Folders = Folders.Where(f => f.Path.Length > 0).Select(f => f.ToConfig()).ToList();
        GroupManager.Rename(_s, oldPath, GroupManager.CleanName(newName));
        string Map(string p) => GroupManager.IsSameOrBelow(p, oldPath) ? newPath + p[oldPath.Length..] : p;
        foreach (var x in Categories) x.Name = Map(x.Name);
        foreach (var f in Folders) f.Config.Group = Map(f.Config.Group);
        RebuildCategoryTree();
        LoadWorkspaces();
    }

    /// <summary>Gruppe (Pfad) oder Ordner einer smarten Gruppe hervorheben und aufklappen.</summary>
    public void FocusGroup(string groupOrFolder)
    {
        var cat = Categories.FirstOrDefault(c => c.Name.Equals(groupOrFolder, StringComparison.CurrentCultureIgnoreCase))
                  ?? Categories.FirstOrDefault(c => c.Folder != null && c.Folder.Path.Equals(groupOrFolder, StringComparison.OrdinalIgnoreCase));
        // Untergruppe aus Unterordnern: zur smarten Gruppe darüber
        for (var p = GroupManager.ParentOf(groupOrFolder); cat == null && p != null; p = GroupManager.ParentOf(p))
            cat = Categories.FirstOrDefault(c => c.Name.Equals(p, StringComparison.CurrentCultureIgnoreCase));
        if (cat == null) return;
        cat.IsExpanded = true;
        if (cat.Folder != null) cat.Folder.IsHighlighted = true;
        Section = "groups";
    }

    /// <summary>
    /// Dialog „Neue Gruppe“ (Name, Symbol/Bild, Ordner, Farbe, Darstellung): ohne <paramref name="parent"/> unten auf
    /// oberster Ebene, sonst als letzte Untergruppe. Mit Ordner wird es eine smarte Gruppe.
    /// </summary>
    private void AddGroupPrompt(string? parent)
    {
        string PathOf(string n) => parent == null ? GroupManager.CleanName(n) : parent + GroupManager.Separator + GroupManager.CleanName(n);
        var spec = Views.NewGroupWindow.Ask(parent == null ? "Neue Gruppe" : $"Neue Untergruppe in „{GroupManager.LeafOf(parent)}“",
            parent == null ? "Neue Gruppe" : "Neue Untergruppe",
            n => Categories.Any(x => x.Name.Equals(PathOf(n), StringComparison.CurrentCultureIgnoreCase)) ? "Eine Gruppe mit diesem Namen gibt es hier schon." : null,
            f => NewGroup.FolderError(Folders.Where(x => x.Path.Length > 0).Select(x => x.Config), f));
        if (spec == null) return;
        var path = PathOf(spec.Name);
        var cat = NewCategoryVm(path, spec.Icon.ToString());
        if (spec.IsSmart)
        {
            var folder = new FolderViewModel(_app, _s, new FolderConfig { Path = spec.Folder!.Trim(), Group = path }, "Wird eingelesen …", RemoveFolder) { IsExpanded = true, ShowOptions = true };
            Folders.Add(folder);
            cat.Folder = folder;
            cat.IsExpanded = true;
        }
        if (!spec.Icon.IsEmpty) GroupManager.SetIcon(_s, path, spec.Icon);
        var index = parent == null ? Categories.Count
            : Categories.ToList().FindLastIndex(x => GroupManager.IsSameOrBelow(x.Name, parent)) + 1;
        Categories.Insert(Math.Clamp(index, 0, Categories.Count), cat);
        if (spec.Color.Length > 0) cat.ColorOwn = spec.Color;
        if (spec.Layout != null) cat.Layout = spec.Layout.ToString()!;
    }

    public ICommand NewGroupCommand => new RelayCommand(() => AddGroupPrompt(null));

    private void AddCategory()
    {
        var name = GroupManager.Normalize(NewCategory);
        if (name.Length > 0 && !Categories.Any(c => c.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
            Categories.Add(NewCategoryVm(name));
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
    public bool InlineParameters { get => _s.InlineParameters; set { _s.InlineParameters = value; OnPropertyChanged(); } }
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

    /// <summary>Ordner von der smarten Gruppe trennen (Gruppe bleibt als normale Gruppe).</summary>
    private void RemoveFolder(FolderViewModel f)
    {
        if (MessageBox.Show(OwnerWindow()!, $"Ordner {f.Path} von der Gruppe trennen?\n\nSeine Dateien erscheinen dann nicht mehr im Panel; die Gruppe selbst und hineingezogene Einträge bleiben.",
                "Ordner entfernen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Folders.Remove(f);
        foreach (var c in Categories.Where(c => c.Folder == f)) c.Folder = null;
    }

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
    /// <summary>Versionshinweise der installierten Version auf GitHub.</summary>
    public ICommand CurrentReleaseNotesCommand { get; }

    private void OnUpdateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateBannerTitle));
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnRepositoryChanged(object? sender, EventArgs e)
    {
        if (_app.Repository.IsScanning) return;
        foreach (var f in Folders) f.ReloadRows();
    }

    /// <summary>Vom Fenster beim Schließen aufgerufen.</summary>
    public void Detach()
    {
        _app.Update.PropertyChanged -= OnUpdateChanged;
        _app.Repository.Changed -= OnRepositoryChanged;
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


    /// <summary>Erstellt die neuen Einstellungen aus dem Formular.</summary>
    public AppSettings Build()
    {
        _s.Folders = Folders.Where(f => f.Path.Length > 0).Select(f => f.ToConfig()).ToList();
        var exts = ExtensionList.Parse(DefaultExtensions);
        _s.DefaultExtensions = exts.Count > 0 ? exts : new List<string>(AppSettings.StandardExtensions);
        _s.GlobalExcludes = GlobalExcludes.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        // Laufzeitzustand aus den aktuellen Einstellungen übernehmen (kann sich währenddessen geändert haben)
        _s.Favorites = new List<string>(_app.Settings.Favorites);
        _s.CollapsedGroups = new List<string>(_app.Settings.CollapsedGroups);
        _s.EditorSizes = new Dictionary<string, string>(_app.Settings.EditorSizes);
        // Panel: Formular, aber „angeheftet“ aus dem laufenden Zustand übernehmen
        var panel = Panel.ToConfig();
        if (_app.Settings.Panels.FirstOrDefault() is { } live) panel.Pinned = live.Pinned;
        _s.Panels = new List<PanelConfig> { panel };
        EntryList.ApplyTo(_s);
        _s.MachineGroups = MachineGroups.Select(g => g.ToGroup()).Where(g => g.Name.Length > 0)
            .GroupBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).ToList();
        _s.ParameterHistory = _app.Settings.ParameterHistory.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
        ApplyWorkspaceRenames();
        _s.RecentRuns = _app.Settings.RecentRuns.ToList();
        _s.RunStats = new Dictionary<string, RunStat>(_app.Settings.RunStats);
        _s.Schedules = _app.Settings.Schedules.Select(x => x.Clone()).ToList(); // werden im Panel verwaltet
        _s.Catalogs = _app.Settings.Catalogs.ToList(); // Abos verwaltet die App (Einstellungen › Allgemein)
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

/// <summary>Kachel einer Designvorlage in den Einstellungen.</summary>
public sealed record DesignPresetChoice(DesignPreset Preset, bool IsCurrent, ICommand ApplyCommand)
{
    public string Name => Preset.Name;
    public string Description => Preset.Description;
    /// <summary>Vorschau: Panelfarbe bzw. Grundfarbe des Designs.</summary>
    public Brush Swatch { get; } = new SolidColorBrush(PanelColors.TryParse(Preset.PanelColor, out var c)
        ? Color.FromRgb(c.R, c.G, c.B)
        : Preset.Theme == ThemeMode.Light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x2B, 0x2B, 0x2B));
    public Brush Ink { get; } = new SolidColorBrush(PanelColors.TryParse(Preset.PanelColor, out var c) && PanelColors.IsLight(c) || Preset.Theme == ThemeMode.Light && Preset.PanelColor.Length == 0
        ? Colors.Black : Colors.White);
}

/// <summary>Ein abonnierter Team-Katalog in den Einstellungen.</summary>
public sealed class CatalogRow
{
    private readonly AppController _app;
    private readonly Action _changed;

    public CatalogRow(AppController app, CatalogSubscription sub, Action changed)
    {
        _app = app;
        Subscription = sub;
        _changed = changed;
        RemoveCommand = new RelayCommand(() => { _app.UnsubscribeCatalog(sub); _changed(); });
        ApplyCommand = new RelayCommand(async () => { await _app.ApplyPendingCatalogsAsync(); _changed(); }, () => sub.PendingSummary.Length > 0);
    }

    public CatalogSubscription Subscription { get; }
    public string Name => Subscription.Name;
    public string Path => Subscription.Path;
    public int Count => TeamCatalogs.EntriesOf(_app.Settings, Subscription).Count();
    public string Status => Subscription.LastError.Length > 0 ? "Problem: " + Subscription.LastError
        : Subscription.PendingSummary.Length > 0 ? "Änderungen verfügbar: " + Subscription.PendingSummary
        : $"{Count} Einträge" + (Subscription.LastAppliedUtc is { } t ? $" · aktualisiert {RecentRunList.Ago(t)}" : string.Empty)
          + (Subscription.LastCheckedUtc is { } c ? $" · geprüft {RecentRunList.Ago(c)}" : string.Empty);
    public bool HasProblem => Subscription.LastError.Length > 0;
    public bool HasPending => Subscription.PendingSummary.Length > 0;

    /// <summary>Änderungen sofort übernehmen (sonst Hinweis im Panel).</summary>
    public bool AutoApply
    {
        get => Subscription.AutoApply;
        set { Subscription.AutoApply = value; _app.SaveSettings(); }
    }

    public ICommand RemoveCommand { get; }
    public ICommand ApplyCommand { get; }
}

