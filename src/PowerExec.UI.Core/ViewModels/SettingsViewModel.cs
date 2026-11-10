using PowerExec.Core.Localization;
using System.Collections.ObjectModel;
using System.Windows.Input;
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
        ExclusionKind.File => L.T("File"),
        ExclusionKind.Type => L.T("Type"),
        _ => L.T("Pattern")
    };
    public ICommand RemoveCommand { get; }
}

/// <summary>Eine Zeile der Eintragstabelle eines Ordners.</summary>
public sealed class EntryRowViewModel : ObservableObject
{
    private readonly FolderViewModel _folder;
    private readonly AppSettings _settings;
    private IconImage? _icon;

    public EntryRowViewModel(FolderViewModel folder, ISettingsWindowHost app, AppSettings settings, LaunchItem? item, string fullPath, string relativePath, string? pattern)
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
            _icon = app.IconProvider.Get(item.FullPath, item.LastWriteTimeUtc, img => { _icon = img; OnPropertyChanged(nameof(Icon)); OnPropertyChanged(nameof(HasIcon)); });
    }

    public LaunchItem? Item { get; }
    public string FullPath { get; }
    public string RelativePath { get; }
    public string? Pattern { get; }
    public ItemType Type { get; }
    public bool IsLocked => Pattern != null;
    public bool CanToggle => !IsLocked;
    public bool IsBlocked => Item?.IsBlocked == true;
    public IconImage? Icon => _icon;
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
            if (IsLocked) return L.F("Hidden by pattern {0}", Pattern);
            var e = _settings.FindEntry(FullPath);
            if (!string.IsNullOrWhiteSpace(e?.Description)) return e!.Description;
            if (Item == null) return string.Empty;
            return string.IsNullOrWhiteSpace(Item.Synopsis) ? (Item.Type is ItemType.PowerShell or ItemType.Batch ? L.T("No synopsis") : Item.FileName) : Item.Synopsis;
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
            if (profiles[0].IsImplicit) return L.T("Default (automatic)");
            var def = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles[0];
            return profiles.Count == 1 ? def.Name : L.F("{0} profiles · default: {1}", profiles.Count, def.Name);
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
    private readonly ISettingsWindowHost _app;
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

    public FolderViewModel(ISettingsWindowHost app, AppSettings settings, FolderConfig f, string status, Action<FolderViewModel> remove)
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

        _rowsView = new FilteredView<EntryRowViewModel>(Rows, Matches);
        LoadRows();
        _isExpanded = Rows.Any(r => r.IsNew);
    }

    /// <summary>Gefundene Einträge (auch ausgeblendete) und per Muster ausgeschlossene Dateien laden.</summary>
    private void LoadRows()
    {
        using var defer = _rowsView.DeferRefresh();
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
    private readonly FilteredView<EntryRowViewModel> _rowsView;
    /// <summary>Zeilen, die zu Suche und Typfilter passen.</summary>
    public ObservableCollection<EntryRowViewModel> VisibleRows => _rowsView.Items;

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public bool ShowOptions { get => _showOptions; set => Set(ref _showOptions, value); }

    public string Query
    {
        get => _query;
        set { if (Set(ref _query, value ?? string.Empty)) { _rowsView.Refresh(); OnPropertyChanged(nameof(NoRows)); } }
    }

    public string TypeFilter
    {
        get => _typeFilter;
        set { if (Set(ref _typeFilter, value)) { _rowsView.Refresh(); OnPropertyChanged(nameof(NoRows)); } }
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
        ? L.T("New files stay hidden; a notice appears in the panel and here.")
        : L.T("New scripts and programs appear in the panel immediately.");

    public int PendingCount => Rows.Count(r => r.IsNew);
    public bool HasPending => PendingCount > 0;
    public string PendingText => PendingCount == 1 ? L.T("1 new entry is waiting for approval.") : L.F("{0} new entries are waiting for approval.", PendingCount);

    public string Summary
    {
        get
        {
            var visible = Rows.Count(r => r.IsVisibleInPanel);
            var selectable = Rows.Count(r => !r.IsLocked);
            return L.F("{0} · {1} found · {2} in the panel · new files: {3}", Recursive ? L.T("With subfolders") : L.T("Top level only"),
                selectable, visible, RequireApproval ? L.T("after approval") : L.T("automatic"));
        }
    }

    public string CountText => L.F("{0} of {1} in the panel", Rows.Count(r => r.IsVisibleInPanel), Rows.Count(r => !r.IsLocked));

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
        _app.ShowProfilesDialog(vm, _app.SettingsDialogOwner);
    }

    private void Browse()
    {
        if (_app.Dialogs.PickFolder(L.T("Select folder"), _app.SettingsDialogOwner) is { } folder) Path = folder;
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
            foreach (var p in new[] { nameof(Display), nameof(IndentLeft), nameof(CardIndentLeft), nameof(ColorOwn), nameof(Colors), nameof(SwatchColor), nameof(Layout) })
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
    /// <summary>Einrückung links in Pixeln (Zeile bzw. Karte), je nach Tiefe der Gruppe.</summary>
    public double IndentLeft => GroupManager.DepthOf(Name) * 22;
    public double CardIndentLeft => GroupManager.DepthOf(Name) * 28;

    /// <summary>Ordner der smarten Gruppe (null = normale Gruppe).</summary>
    public FolderViewModel? Folder
    {
        get => _folder;
        set { if (Set(ref _folder, value)) { OnPropertyChanged(nameof(IsSmart)); OnPropertyChanged(nameof(KindText)); } }
    }
    public bool IsSmart => _folder != null;
    public string KindText => _folder != null ? L.T("Smart group · ") + _folder.Path : L.T("Group");
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

    /// <summary>Dialoge für „Eigene Farbe …“ (setzt das Einstellungsfenster).</summary>
    public IDialogs? Dialogs { get; init; }
    public object? DialogOwner { get; init; }

    public static IReadOnlyList<KeyValuePair<string, string>> ColorList => ColorChoices.WithNone;

    /// <summary>Palette plus eine eigene Farbe, falls gesetzt.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Colors => ColorChoices.Including(ColorOwn);

    public string ColorOwn
    {
        get => Settings != null ? GroupManager.OwnColorOf(Settings, Name) : string.Empty;
        set
        {
            if (Settings == null) return;
            GroupManager.SetColor(Settings, Name, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Colors));
            OnPropertyChanged(nameof(SwatchColor));
        }
    }

    /// <summary>Wirksame Gruppenfarbe (eigene oder geerbt); null = keine.</summary>
    public ArgbColor? SwatchColor => Settings != null ? ArgbColor.FromHex(GroupManager.ColorOf(Settings, Name)) : null;

    public ICommand CustomColorCommand => new RelayCommand(() =>
    {
        var t = Dialogs?.PickColor(L.F("Color for “{0}”", Display), ColorOwn, DialogOwner);
        if (t != null) ColorOwn = t;
    });

    public static IReadOnlyList<KeyValuePair<string, string>> LayoutList { get; } = new[]
    {
        new KeyValuePair<string, string>("panel", L.T("Like the panel")),
        new KeyValuePair<string, string>("List", L.T("List")),
        new KeyValuePair<string, string>("Grid", L.T("Grid"))
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

public sealed record FolderTimeInfo(string Path, string Text, bool IsProblem);

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsWindowHost _app;
    private readonly AppSettings _s;
    private string _section = "general";
    private string _defaultExtensions;
    private string _globalExcludes;
    private string _updateStatus = string.Empty;
    private string? _hotkeyError;
    private bool _machinesCleared;

    public SettingsViewModel(ISettingsWindowHost app, string? focusFolder)
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
        _monitors = new[] { new KeyValuePair<int, string>(-1, L.T("Primary monitor")), new KeyValuePair<int, string>(-2, L.T("All monitors")) }
            .Concat(app.Monitors)
            .ToList();

        ClearMachinesCommand = new RelayCommand(() => { MachineList.ClearRecent(_s); _machinesCleared = true; OnPropertyChanged(nameof(MachinesText)); });
        CheckUpdatesCommand = new RelayCommand(() => _ = CheckUpdatesAsync(), () => !_isChecking);
        InstallUpdateCommand = new RelayCommand(() => _ = _app.InstallUpdateAsync(), () => HasUpdate && !_app.Update.Busy);
        ReleaseNotesCommand = new RelayCommand(() => { if (_app.AvailableUpdate is { } r) _app.Shell.OpenUrl(r.HtmlUrl); });
        ShowWelcomeCommand = new RelayCommand(() => _app.ShowWelcome());
        CurrentReleaseNotesCommand = new RelayCommand(() => _app.Shell.OpenUrl(
            $"https://github.com/{UpdateChecker.Owner}/{UpdateChecker.Repo}/releases/tag/v{_app.CurrentVersionText}"));
        _app.Update.PropertyChanged += OnUpdateChanged;
        _app.Repository.Changed += OnRepositoryChanged;
        AppLog.Changed += OnLogChanged;
        OpenLogFolderCommand = new RelayCommand(() => _app.Shell.OpenLocation(_s.EffectiveLogFolder, isFolder: true));
        OpenRepoCommand = new RelayCommand(() => _app.Shell.OpenUrl($"https://github.com/{UpdateChecker.Owner}/{UpdateChecker.Repo}"));
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
        Panel = new PanelSettingsViewModel(app, _s.Panels[0], _monitors, _s.RecentColors);
        // Vorschau des aktuellen Designs folgt jeder Änderung an Farbe, Deckkraft, Hintergrund …
        Panel.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(CurrentPresetText)); OnPropertyChanged(nameof(CurrentDesign)); };
        EntryList = new CustomEntryListViewModel(app, _s, () => Categories.Select(c => c.Name).ToList());
        TagList = new TagsViewModel(app, _s, () => EntryList.ApplyTo(_s), () => EntryList.ReloadTags());
        foreach (var g in _s.MachineGroups) MachineGroups.Add(new MachineGroupViewModel(app, g, RemoveGroup));
        VariablesPage = new VariablesViewModel(app, _s, text => app.Shell.SetClipboardText(text));
        MarketplacePage = new MarketplaceViewModel(app, _s, OnMarketplaceInstalled, VariablesPage);
        AddGroupCommand = new RelayCommand(AddGroup);
        AddWorkspaceCommand = new RelayCommand(AddWorkspace);
        LoadWorkspaces();

        CopyLogCommand = new RelayCommand(() => _app.Shell.SetClipboardText(DiagnosticsText()));
        OpenAppLogFolderCommand = new RelayCommand(() => _app.Shell.OpenLocation(System.IO.Path.GetDirectoryName(AppLog.FilePath)!, isFolder: true));
        RefreshLogCommand = new RelayCommand(() => OnPropertyChanged(nameof(LogText)));
        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
        UpdateStatus = app.LastUpdateCheck is { } t
            ? (app.AvailableUpdate is { } r ? L.F("Version {0} is available (checked {1:HH:mm}).", r.Version.ToString(3), t) : L.F("Up to date (checked {0:HH:mm}).", t))
            : L.T("Not checked yet.");

    }

    // ---- Suche in den Einstellungen ---------------------------------------------

    public sealed record SearchHit(string Section, string Page, string Title, string Keywords);

    private static readonly (string Section, string Page, string Segment)[] Pages =
    {
        ("general", L.T("General"), ""),
        ("groups", L.T("Groups"), "content"), ("custom", L.T("Custom entries"), "content"), ("tags", L.T("Tags"), "content"), ("variables", L.T("Variables"), "content"), ("workspaces", L.T("Workspaces"), "content"),
        ("market-entries", L.T("Marketplace › Entries"), "market"), ("market-scripts", L.T("Marketplace › Scripts"), "market"),
        ("market-designs", L.T("Marketplace › Designs"), "market"), ("market-installed", L.T("Marketplace › Installed"), "market"),
        ("market-sources", L.T("Marketplace › Sources"), "market"),
        ("look", L.T("Appearance"), "panel"), ("behavior", L.T("Behavior"), "panel"),
        ("exec", "PowerShell", "exec"), ("remote", L.T("Remote"), "exec"), ("machines", L.T("Computer groups"), "exec"),
        ("samples", L.T("Examples"), "help"), ("diag", L.T("Diagnostics"), "help")
    };

    private static readonly SearchHit[] Index =
    {
        new("general", L.T("General"), L.T("Version and updates"), L.T("update upgrade version pre-release beta github")),
        new("general", L.T("General"), L.T("Release notes"), L.T("release notes what's new changes changelog")),
        new("general", L.T("General"), L.T("Display language"), L.T("language sprache english german deutsch")),
        new("general", L.T("General"), L.T("Start with Windows"), L.T("autostart sign-in startup")),
        new("general", L.T("General"), L.T("Back up settings"), L.T("export import backup file")),
        new("groups", L.T("Groups"), L.T("Groups and subgroups"), L.T("group subgroup order category")),
        new("groups", L.T("Groups"), L.T("Smart group (folder)"), L.T("folder path share unc network scan smart")),
        new("groups", L.T("Groups"), L.T("Group icons"), "icon symbol fluent heroicons emoji"),
        new("groups", L.T("Groups"), L.T("Favorites"), L.T("star favorite show")),
        new("groups", L.T("Groups"), L.T("Recently run"), L.T("history count recent")),
        new("groups", L.T("Groups"), L.T("Subfolders as subgroups"), L.T("recursive subfolders")),
        new("groups", L.T("Groups"), L.T("Default file extensions"), L.T("ps1 exe cmd extension")),
        new("groups", L.T("Groups"), L.T("Global exclusions"), L.T("exclude pattern")),
        new("groups", L.T("Groups"), L.T("Rescan automatically"), L.T("file watcher rescan")),
        new("groups", L.T("Groups"), L.T("Timeout for unreachable folders"), "timeout offline"),
        new("groups", L.T("Groups"), L.T("Approve new files"), L.T("approval visible checkbox")),
        new("custom", L.T("Custom entries"), L.T("Manage custom entries"), L.T("website url folder rdp ssh snippet tool command file")),
        new("custom", L.T("Custom entries"), L.T("From templates"), L.T("catalog mmc control panel ms-settings")),
        new("custom", L.T("Custom entries"), L.T("Check reachability"), L.T("health dead links broken")),
        new("custom", L.T("Custom entries"), L.T("Import / export"), L.T("share colleagues file")),
        new("tags", L.T("Tags"), L.T("Manage tags"), L.T("tag rename merge delete color #")),
        new("market-entries", L.T("Marketplace"), L.T("Download entries, scripts and designs"), L.T("marketplace download folder design script matrix42 google virustotal")),
        new("variables", L.T("Variables"), L.T("Manage variables"), L.T("variable placeholder $ company name date time user computer clipboard secret token environment variable powershell workspace")),
        new("workspaces", L.T("Workspaces"), L.T("Workspaces"), L.T("customer profile switch")),
        new("workspaces", L.T("Workspaces"), L.T("Groups per workspace"), L.T("hide visible selection")),
        new("look", L.T("Appearance"), "Design", L.T("light dark theme color")),
        new("look", L.T("Appearance"), "Animation", L.T("slide out smooth fast")),
        new("look", L.T("Appearance"), L.T("Show icons"), L.T("icons favicon program icon")),
        new("look", L.T("Appearance"), L.T("List or grid"), L.T("tiles view")),
        new("look", L.T("Appearance"), L.T("Group style"), L.T("accent stripe cards colored fluent group color")),
        new("look", L.T("Appearance"), L.T("Item size"), L.T("small medium large compact")),
        new("look", L.T("Appearance"), L.T("Panel color and opacity"), L.T("color transparency")),
        new("look", L.T("Appearance"), L.T("Panel dimensions"), L.T("width height position size")),
        new("look", L.T("Appearance"), L.T("Design templates"), L.T("template design oled nordic espresso fluent glass custom design save")),
        new("look", L.T("Appearance"), L.T("Accent color"), L.T("accent windows color hover zone")),
        new("look", L.T("Appearance"), L.T("Mini dock"), L.T("dock favorites bar")),
        new("look", L.T("Appearance"), L.T("Background"), L.T("solid opaque clear glass glass frosted glass")),
        new("look", L.T("Appearance"), L.T("Hover zone"), L.T("edge monitor width length trigger left right top bottom")),
        new("behavior", L.T("Behavior"), L.T("Dwell time before sliding out"), L.T("hover delay")),
        new("behavior", L.T("Behavior"), L.T("Collapse after leaving"), L.T("close delay")),
        new("behavior", L.T("Behavior"), L.T("Hide while full-screen programs run"), L.T("video presentation game rdp")),
        new("behavior", L.T("Behavior"), L.T("Dragging groups: drag image at the mouse pointer"), L.T("drag drop move group preview")),
        new("behavior", L.T("Behavior"), L.T("Dragging groups: open gap smoothly"), L.T("drag drop move animation placeholder")),
        new("behavior", L.T("Behavior"), L.T("Dragging groups: collapse contents"), L.T("drag drop move compact cards")),
        new("behavior", L.T("Behavior"), L.T("Dragging groups: reason at invalid places"), L.T("drag drop move hint error")),
        new("behavior", L.T("Behavior"), L.T("Hotkey"), "hotkey shortcut"),
        new("exec", "PowerShell", L.T("Default PowerShell"), "pwsh 7 5.1 version"),
        new("exec", "PowerShell", "Execution Policy", "bypass remotesigned"),
        new("exec", "PowerShell", L.T("Without profile / keep console open"), "noprofile noexit"),
        new("exec", "PowerShell", L.T("Ask for the profile at launch"), L.T("profile choice ask")),
        new("machines", L.T("Computer groups"), L.T("Dynamic groups from AD"), L.T("active directory rules filter ldap dynamic")),
        new("look", L.T("Appearance"), L.T("Blur strength and saturation (glass)"), L.T("transparency blur frosted glass")),
        new("exec", "PowerShell", "Editor", "vscode ise notepad"),
        new("remote", L.T("Remote"), L.T("PowerShell for remote"), "winrm 5.1 7"),
        new("remote", L.T("Remote"), "HTTPS (Port 5986)", "ssl winrm"),
        new("remote", L.T("Remote"), L.T("Concurrent connections and timeout"), L.T("throttle bulk")),
        new("remote", L.T("Remote"), L.T("Notification and log folder"), L.T("log finished")),
        new("remote", L.T("Remote"), L.T("Recently used computers"), L.T("computer list clear")),
        new("machines", L.T("Computer groups"), L.T("Computer groups"), L.T("group csv import ad active directory")),
        new("samples", L.T("Examples"), L.T("Create sample scripts"), L.T("example samples scripts templates")),
        new("diag", L.T("Diagnostics"), L.T("Log"), L.T("log error copy")),
        new("diag", L.T("Diagnostics"), L.T("Folder scan times"), L.T("scan duration")),
    };

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
            if (value == "marketplace") { Section = "market-entries"; return; }
            if (value.StartsWith("market-", StringComparison.Ordinal))
            {
                MarketplacePage.Tab = value switch
                {
                    "market-scripts" => "Script",
                    "market-designs" => "Design",
                    "market-installed" => "Installed",
                    "market-sources" => "Sources",
                    _ => "Entry"
                };
                _ = MarketplacePage.EnsureLoadedAsync();
            }
            OnPropertyChanged(nameof(IsMarketplaceSection));
        }
    }

    /// <summary>Eine der Marktplatz-Seiten ist offen (Einträge, Scripte, Designs, Installiert, Quellen).</summary>
    public bool IsMarketplaceSection => _section.StartsWith("market-", StringComparison.Ordinal);

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
        new KeyValuePair<AnimationMode, string>(AnimationMode.Smooth, L.T("Smooth (320 ms)")),
        new KeyValuePair<AnimationMode, string>(AnimationMode.Fast, L.T("Fast (150 ms)")),
        new KeyValuePair<AnimationMode, string>(AnimationMode.Off, L.T("Off"))
    };

    public ThemeMode Theme { get => _s.Theme; set { _s.Theme = value; OnPropertyChanged(); RaisePresets(); } }
    public IReadOnlyList<KeyValuePair<ThemeMode, string>> Themes { get; } = new[]
    {
        new KeyValuePair<ThemeMode, string>(ThemeMode.System, L.T("Like Windows")),
        new KeyValuePair<ThemeMode, string>(ThemeMode.Dark, L.T("Dark")),
        new KeyValuePair<ThemeMode, string>(ThemeMode.Light, L.T("Light"))
    };

    // ---- Darstellung: Akzent, Größe, Vorlagen ---------------------------------------

    public bool UseWindowsAccent { get => _s.UseWindowsAccent; set { _s.UseWindowsAccent = value; OnPropertyChanged(); RaisePresets(); } }


    public int UiScalePercent { get => _s.UiScalePercent; set { _s.UiScalePercent = Math.Clamp(value / 10 * 10, Appearance.MinScale, Appearance.MaxScale); OnPropertyChanged(); } }

    public bool IsHighContrast => _app.Theme.IsHighContrast;

    /// <summary>Designvorlagen und eigene Designs: Design, Panelfarbe, Deckkraft, Hintergrund, Milchglas und Akzent mit einem Klick.</summary>
    public IReadOnlyList<DesignPresetChoice> DesignPresets
    {
        get
        {
            var current = CurrentPreset?.Key;
            return Appearance.All(_s).Select(p => new DesignPresetChoice(p, current == p.Key, new RelayCommand(() => ApplyPreset(p)),
                p.IsCustom ? new RelayCommand(() => DeleteDesign(p)) : null)).ToList();
        }
    }

    private DesignPreset? CurrentPreset
    {
        get
        {
            var probe = SettingsStore.Clone(_s);
            probe.Panels = new List<PanelConfig> { Panel.ToConfig() };
            return Appearance.Current(probe);
        }
    }

    /// <summary>Kopfzeile der Designvorlagen: welche gerade aktiv ist.</summary>
    public string CurrentPresetText => CurrentPreset is { } p ? L.T("Current: ") + p.Name : L.T("Current: custom settings");

    /// <summary>Das aktuelle Aussehen als Design (Vorschau im Kopf der Designvorlagen, auch eingeklappt).</summary>
    public DesignPreset CurrentDesign => Appearance.Capture(_s, Panel.ToConfig(), CurrentPreset?.Name ?? string.Empty);

    private bool _presetsExpanded;
    public bool PresetsExpanded { get => _presetsExpanded; set => Set(ref _presetsExpanded, value); }

    private void ApplyPreset(DesignPreset p)
    {
        Theme = p.Theme;
        UseWindowsAccent = p.WindowsAccent;
        Panel.ApplyDesign(p);
        RaisePresets();
    }

    /// <summary>Aktuelles Aussehen unter einem Namen als eigenes Design speichern.</summary>
    public ICommand SaveDesignCommand => _saveDesign ??= new RelayCommand(() =>
    {
        var name = _app.Dialogs.Ask(L.T("Save design"), L.T("Name for the current design (color, background, opacity, frosted glass, accent):"),
            L.T("My design"), L.T("Save"), n => n.Trim().Length == 0 ? L.T("Please enter a name.")
                : Appearance.All(_s).Any(d => d.IsCustom && d.Name.Equals(n.Trim(), StringComparison.OrdinalIgnoreCase)) ? L.T("A custom design with this name already exists.") : null);
        if (name == null) return;
        _s.CustomDesigns.Add(Appearance.Capture(_s, Panel.ToConfig(), name));
        RaisePresets();
    });
    private ICommand? _saveDesign;

    private void DeleteDesign(DesignPreset p)
    {
        if (!_app.Dialogs.Confirm(L.F("Delete the custom design “{0}”?", p.Name), L.T("Delete design"), destructive: true, owner: _app.SettingsDialogOwner)) return;
        _s.CustomDesigns.RemoveAll(d => d.Key == p.Key);
        // Aus dem Marktplatz installiert? Dann gilt es dort wieder als nicht installiert
        if (_s.MarketplaceInstalls.RemoveAll(i => i.DesignKey == p.Key) > 0) MarketplacePage.Rebuild();
        RaisePresets();
    }

    private void RaisePresets()
    {
        OnPropertyChanged(nameof(DesignPresets));
        OnPropertyChanged(nameof(CurrentPresetText));
        OnPropertyChanged(nameof(CurrentDesign));
    }

    public ICommand ShowWelcomeCommand { get; }

    // ---- Team-Kataloge (live, nicht über „Änderungen verwerfen“) -----------------------

    public IReadOnlyList<CatalogRow> CatalogRows => _app.Settings.Catalogs.Select(c => new CatalogRow(_app, c, () => OnPropertyChanged(nameof(CatalogRows)))).ToList();
    public bool HasCatalogs => _app.Settings.Catalogs.Count > 0;
    private string? _catalogError;
    public string? CatalogError { get => _catalogError; private set => Set(ref _catalogError, value); }

    public ICommand SubscribeCatalogCommand => _subscribeCatalog ??= new RelayCommand(async () =>
    {
        var file = _app.Dialogs.OpenFile(EntryShare.FileFilter, L.T("Subscribe to team catalog (export file, e.g. on a share)"), _app.SettingsDialogOwner);
        if (file == null) return;
        CatalogError = await _app.SubscribeCatalogAsync(file);
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

    /// <summary>Sprache der Oberfläche: "" = wie Windows, "de" oder "en".</summary>
    /// <summary>„Wie Windows“ plus jede verfügbare Sprache (Englisch und jede Datei unter Translations/).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> LanguageList { get; } =
        new[] { new KeyValuePair<string, string>(string.Empty, L.T("Like Windows")) }
            .Concat(L.Available.Select(l => new KeyValuePair<string, string>(l, L.NativeName(l))).OrderBy(kv => kv.Value, StringComparer.CurrentCulture))
            .ToList();
    public string Language
    {
        get => _s.Language;
        set
        {
            if (_s.Language == (value ?? string.Empty)) return;
            _s.Language = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LanguageChangePending));
        }
    }
    /// <summary>Gewählte Sprache weicht von der laufenden ab – wirkt nach einem Neustart.</summary>
    public bool LanguageChangePending => L.Resolve(_s.Language, System.Globalization.CultureInfo.CurrentUICulture) != L.Language;
    public ICommand RestartCommand => _restartCommand ??= new RelayCommand(() => _app.RestartApp());
    private ICommand? _restartCommand;

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

    // ---- Variablen ({{$Name}}) --------------------------------------------------

    public VariablesViewModel VariablesPage { get; }

    // ---- Marktplatz -------------------------------------------------------------

    public MarketplaceViewModel MarketplacePage { get; }

    /// <summary>Nach einer Installation: neue Gruppen, Variablen und Designs in den anderen Seiten zeigen.</summary>
    private void OnMarketplaceInstalled()
    {
        // Marktplatz hat _s direkt geändert: neue Gruppen und Einträge in die Listen übernehmen, sonst überschreibt
        // das Übernehmen (Build) sie mit dem alten Stand
        foreach (var g in GroupManager.All(_s).Where(g => !Categories.Any(c => c.Name.Equals(g, StringComparison.CurrentCultureIgnoreCase))).ToList())
            Categories.Add(NewCategoryVm(g, GroupManager.IconOf(_s, g).ToString()));
        EntryList.SyncFrom(_s);
        RaisePresets();
        MarketplaceChanged?.Invoke();
    }

    /// <summary>Marktplatz hat etwas installiert – das Fenster übernimmt die Einstellungen sofort.</summary>
    public event Action? MarketplaceChanged;

    // ---- Rechnergruppen -------------------------------------------------------

    public ObservableCollection<MachineGroupViewModel> MachineGroups { get; } = new();
    public ICommand AddGroupCommand { get; }
    public bool HasNoGroups => MachineGroups.Count == 0;

    private void AddGroup()
    {
        var name = L.T("Group");
        for (int i = 2; MachineGroups.Any(g => g.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)); i++) name = L.F("Group {0}", i);
        MachineGroups.Add(new MachineGroupViewModel(_app, new MachineGroup { Name = name }, RemoveGroup));
        OnPropertyChanged(nameof(HasNoGroups));
    }

    /// <summary>Neue Rechnergruppe aus einer Liste (Text oder CSV): dynamisch aus der Datei, mit Filtern.</summary>
    public ICommand ImportGroupFromListCommand => new RelayCommand(() =>
    {
        var file = _app.Dialogs.OpenFile(L.T("Computer lists (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*"), L.T("Import computer group from list"));
        if (file == null) return;
        var name = System.IO.Path.GetFileNameWithoutExtension(file).Trim();
        if (name.Length == 0) name = L.T("Group");
        var unique = name;
        for (int i = 2; MachineGroups.Any(g => g.Name.Equals(unique, StringComparison.CurrentCultureIgnoreCase)); i++) unique = $"{name} {i}";
        var vm = new MachineGroupViewModel(_app, new MachineGroup { Name = unique }, RemoveGroup);
        vm.UseList(file);
        MachineGroups.Add(vm);
        OnPropertyChanged(nameof(HasNoGroups));
    });

    private void RemoveGroup(MachineGroupViewModel g)
    {
        MachineGroups.Remove(g);
        OnPropertyChanged(nameof(HasNoGroups));
    }

    /// <summary>Dynamische Gruppen: wie oft im Hintergrund aus dem AD aktualisieren.</summary>
    public int AdSyncIntervalHours { get => _s.AdSyncIntervalHours; set { _s.AdSyncIntervalHours = Math.Clamp(value, 1, 168); OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<int, string>> AdSyncIntervals { get; } = new[]
    {
        new KeyValuePair<int, string>(1, L.T("every hour")), new KeyValuePair<int, string>(2, L.T("every 2 hours")),
        new KeyValuePair<int, string>(4, L.T("every 4 hours")), new KeyValuePair<int, string>(8, L.T("every 8 hours")),
        new KeyValuePair<int, string>(12, L.T("every 12 hours")), new KeyValuePair<int, string>(24, L.T("once a day"))
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
            var summary = L.F("{0} of {1} groups · {2} favorites", groups.Count - hidden, groups.Count, favs) + (active ? L.T(" · active") : string.Empty);
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
        if (!_app.Dialogs.Confirm(L.F("Delete workspace “{0}” with its favorites, launch profiles and group selection?\nGroups and entries are kept.", w.OriginalName), L.T("Delete workspace"),
                destructive: true)) return;
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
        new KeyValuePair<GroupingMode, string>(GroupingMode.Folder, L.T("Own groups + folders")),
        new KeyValuePair<GroupingMode, string>(GroupingMode.Category, L.T("Own groups only"))
    };
    public ObservableCollection<CategoryItemViewModel> Categories { get; } = new();

    /// <summary>Gruppen der obersten Ebene; Untergruppen hängen als <see cref="CategoryItemViewModel.Children"/> darunter.</summary>
    public ObservableCollection<CategoryItemViewModel> CategoryRoots { get; } = new();
    private bool _treeSuspended;

    private CategoryItemViewModel NewCategoryVm(string name, string icon = "") =>
        new(name, MoveCategory, RemoveCategory, icon, ChooseCategoryIcon, RenameCategory) { Settings = _s, Dialogs = _app.Dialogs, DialogOwner = _app.SettingsDialogOwner };

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
        var byName = roots.ToDictionary(r => r.Name, r => r, StringComparer.CurrentCultureIgnoreCase);
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
        var picked = _app.Dialogs.PickIcon(IconRef.Parse(c.Icon), L.F("Icon for “{0}”", c.Display));
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
    private ArgbColor? _categoryDragColor;

    public string CategoryDragLabel { get => _categoryDragLabel; private set { _categoryDragLabel = value; OnPropertyChanged(); } }
    public ArgbColor? CategoryDragColor { get => _categoryDragColor; private set { _categoryDragColor = value; OnPropertyChanged(); } }
    public GroupDropPlan? CurrentCategoryPlan => _categoryPlan;
    public bool IsDraggingCategory => _dragCategory != null;

    private IEnumerable<CategoryItemViewModel> SubtreeOf(CategoryItemViewModel c) =>
        Categories.Where(x => GroupManager.IsSameOrBelow(x.Name, c.Name));

    public void BeginCategoryDrag(CategoryItemViewModel c)
    {
        EndCategoryDrag();
        _dragCategory = c;
        var sub = SubtreeOf(c).Count() - 1;
        CategoryDragLabel = c.Display + (sub > 0 ? (sub == 1 ? L.T("  + 1 subgroup") : L.F("  + {0} subgroups", sub)) : string.Empty);
        CategoryDragColor = c.SwatchColor;
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
            _categoryPlan = new GroupDropPlan { IsValid = false, Reason = L.T("Fixed groups stay at the top level – drag them before or after a group.") };
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

    private object? OwnerWindow() => _app.SettingsDialogOwner;

    /// <summary>Entfernt die Gruppe samt Untergruppen (nach Rückfrage); smarte Gruppen lesen ihren Ordner nicht mehr ein.</summary>
    private void RemoveCategory(CategoryItemViewModel c)
    {
        var affected = Categories.Where(x => GroupManager.IsSameOrBelow(x.Name, c.Name)).ToList();
        var folders = affected.Where(x => x.Folder != null).Select(x => x.Folder!.Path).ToList();
        var text = (affected.Count > 1 ? L.F("Delete group “{0}” with {1} subgroup(s)?", c.Display, affected.Count - 1) : L.F("Delete group “{0}”?", c.Display)) +
                   (folders.Count > 0 ? L.F("\n\nThe folder {0} will then no longer be scanned. The files themselves stay unchanged.", string.Join(", ", folders)) : string.Empty) +
                   L.T("\n\nEntries you had dragged into the group reappear in their smart group or under “Custom entries”.");
        if (!_app.Dialogs.Confirm(text, L.T("Delete group"), destructive: true, owner: OwnerWindow())) return;
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
        var newName = _app.Dialogs.Ask(L.T("Rename group"), c.IsSmart ? L.T("The name appears in the panel; the folder stays the same.") : string.Empty, c.Display, L.T("Rename"),
            n =>
            {
                var path = parent == null ? GroupManager.CleanName(n) : parent + GroupManager.Separator + GroupManager.CleanName(n);
                return Categories.Any(x => x != c && x.Name.Equals(path, StringComparison.CurrentCultureIgnoreCase)) ? L.T("A group with this name already exists here.") : null;
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
        var spec = _app.Dialogs.AskNewGroup(parent == null ? L.T("New group") : L.F("New subgroup in “{0}”", GroupManager.LeafOf(parent)),
            parent == null ? L.T("New group") : L.T("New subgroup"),
            n => Categories.Any(x => x.Name.Equals(PathOf(n), StringComparison.CurrentCultureIgnoreCase)) ? L.T("A group with this name already exists here.") : null,
            f => NewGroup.FolderError(Folders.Where(x => x.Path.Length > 0).Select(x => x.Config), f));
        if (spec == null) return;
        AddGroup(spec, PathOf(spec.Name), parent);
    }

    /// <summary>Gruppe in die Formularlisten aufnehmen (mit Ordner: smarte Gruppe); übernommen wird wie sonst auch.</summary>
    private void AddGroup(NewGroupSpec spec, string path, string? parent)
    {
        var cat = NewCategoryVm(path, spec.Icon.ToString());
        if (spec.IsSmart)
        {
            var folder = new FolderViewModel(_app, _s, new FolderConfig { Path = spec.Folder!.Trim(), Group = path }, L.T("Scanning …"), RemoveFolder) { IsExpanded = true, ShowOptions = true };
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

    /// <summary>
    /// Beispielscripte (auch neue aus einem Update) im Beispielordner anlegen und ihn als Gruppe „Beispiele“ einbinden,
    /// falls er noch nicht eingebunden ist. Vorhandene, vielleicht angepasste Dateien bleiben unverändert.
    /// </summary>
    public ICommand AddSamplesCommand => new RelayCommand(() =>
    {
        var folder = WelcomeViewModel.CreateSampleFolder();
        if (folder == null) { SamplesStatus = L.T("The sample folder couldn't be created – details in the log."); return; }
        if (Folders.Any(f => f.Path.Equals(folder, StringComparison.OrdinalIgnoreCase)))
        {
            SamplesStatus = null;
            Snackbar.Show(L.F("Examples are in {0}; the folder is already included.", folder));
            return;
        }
        var name = L.T("Examples");
        for (int i = 2; Categories.Any(c => c.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)); i++) name = L.F("Examples {0}", i);
        AddGroup(new NewGroupSpec(name, IconRef.None, folder, string.Empty, null), name, null);
        SamplesStatus = null;
        Snackbar.Show(L.F("Group “{0}” with the sample scripts from {1} added.", name, folder));
    });

    private string? _samplesStatus;
    public string? SamplesStatus { get => _samplesStatus; private set { if (Set(ref _samplesStatus, value)) OnPropertyChanged(nameof(SamplesSeverity)); } }
    /// <summary>Art der Rückmeldung für die InfoBar: Success oder Error.</summary>
    public string SamplesSeverity => _samplesStatus != null ? "Error" : "Success";

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
        new KeyValuePair<RemoteEngineMode, string>(RemoteEngineMode.PreferPowerShell7, L.T("PowerShell 7 if available, otherwise 5.1"))
    };

    public string ExecutionPolicy { get => _s.ExecutionPolicy; set { _s.ExecutionPolicy = value ?? string.Empty; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<string, string>> Policies { get; } = new[]
    {
        new KeyValuePair<string, string>("Bypass", L.T("Bypass (only for launched scripts)")),
        new KeyValuePair<string, string>("RemoteSigned", "RemoteSigned"),
        new KeyValuePair<string, string>("", L.T("System default"))
    };

    public bool NoProfile { get => _s.NoProfile; set { _s.NoProfile = value; OnPropertyChanged(); } }
    public bool NoExit { get => _s.NoExit; set { _s.NoExit = value; OnPropertyChanged(); } }
    public bool AskProfile { get => _s.AskProfile; set { _s.AskProfile = value; OnPropertyChanged(); } }
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
        new KeyValuePair<EditorKind, string>(EditorKind.Notepad, L.T("Notepad (notepad.exe)")),
        new KeyValuePair<EditorKind, string>(EditorKind.Custom, L.T("Custom path …"))
    };

    public string MachinesText => L.F("{0} recently used · {1} favorites", _s.Machines.Count(m => !m.Favorite), _s.Machines.Count(m => m.Favorite));

    // Updates
    public string CurrentVersion => _app.CurrentVersionText;
    public string RepoText => $"github.com/{UpdateChecker.Owner}/{UpdateChecker.Repo}";
    public bool CheckUpdatesOnStart { get => _s.CheckUpdatesOnStart; set { _s.CheckUpdatesOnStart = value; OnPropertyChanged(); } }
    public bool IncludePrereleases { get => _s.IncludePrereleases; set { _s.IncludePrereleases = value; OnPropertyChanged(); } }
    public string UpdateStatus { get => _updateStatus; set => Set(ref _updateStatus, value); }
    public string SettingsFile => _app.SettingsFilePath;

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
        if (st == null) return L.T("Not scanned yet");
        if (st.Error != null) return st.Error;
        return st.Excluded > 0 ? L.F("{0} files, {1} hidden", st.Count, st.Excluded) : L.F("{0} files", st.Count);
    }

    /// <summary>Ordner von der smarten Gruppe trennen (Gruppe bleibt als normale Gruppe).</summary>
    private void RemoveFolder(FolderViewModel f)
    {
        if (!_app.Dialogs.Confirm(L.F("Detach folder {0} from the group?\n\nIts files will no longer appear in the panel; the group itself and entries dragged in stay.", f.Path),
                L.T("Remove folder"), destructive: true, owner: OwnerWindow())) return;
        Folders.Remove(f);
        foreach (var c in Categories.Where(c => c.Folder == f)) c.Folder = null;
    }

    private void BrowseEditor()
    {
        if (_app.Dialogs.OpenFile(L.T("Programs (*.exe)|*.exe"), owner: OwnerWindow()) is { } file) EditorPath = file;
    }

    private void BrowseLogFolder()
    {
        if (_app.Dialogs.PickFolder(owner: OwnerWindow()) is { } folder) LogFolder = folder;
    }

    // ---- Update-Hinweis in den Einstellungen ----------------------------

    private bool _isChecking;

    public UpdateState Update => _app.Update;
    public bool HasUpdate => _app.AvailableUpdate != null;
    public string UpdateBannerTitle => _app.AvailableUpdate is { } r ? L.F("Version {0} is available", r.Version.ToString(3)) : string.Empty;
    public bool IsChecking { get => _isChecking; private set { if (Set(ref _isChecking, value)) CommandRequery.Invalidate(); } }

    public ICommand InstallUpdateCommand { get; }
    public ICommand ReleaseNotesCommand { get; }
    /// <summary>Versionshinweise der installierten Version auf GitHub.</summary>
    public ICommand CurrentReleaseNotesCommand { get; }

    private void OnUpdateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateBannerTitle));
        CommandRequery.Invalidate();
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

    private void OnLogChanged() => _app.UiDispatcher.Post(() => OnPropertyChanged(nameof(LogText)));

    public IReadOnlyList<FolderTimeInfo> FolderTimes => _app.Repository.FolderStatuses.Select(st => new FolderTimeInfo(st.Path,
        st.Error != null ? st.Error
        : st.FromCache ? L.F("cached (as of {0:g})", st.CachedAtUtc?.ToLocalTime())
        : L.F("{0} files · {1:0} ms", st.Count, st.Duration.TotalMilliseconds), st.Error != null)).ToList();

    public string SystemInfo =>
        $"Power-Exec {_app.CurrentVersionText} · {(_app.IsInstalledViaSetup ? L.T("installed") : L.T("portable"))} · " +
        $"{Environment.OSVersion.VersionString} · .NET {Environment.Version} · PowerShell 7: {(_app.Shell.IsPwshInstalled ? L.T("yes") : L.T("no"))}";

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
        var file = _app.Dialogs.SaveFile(L.T("Power-Exec settings (*.json)|*.json"), $"Power-Exec-Einstellungen_{DateTime.Now:yyyy-MM-dd}.json");
        if (file == null) return;
        try
        {
            File.WriteAllText(file, SettingsTransfer.Export(_app.Settings));
            AppLog.Info(L.F("Settings exported: {0}", file));
            _app.Dialogs.Inform(L.T("The saved settings were exported.\nPasswords are not included."), L.T("Export"));
        }
        catch (Exception ex)
        {
            _app.Dialogs.Warn(ex.Message, L.T("Export"));
        }
    }

    private void Import()
    {
        var file = _app.Dialogs.OpenFile(L.T("Power-Exec settings (*.json)|*.json|All files (*.*)|*.*"));
        if (file == null) return;
        AppSettings imported;
        try
        {
            imported = SettingsTransfer.Parse(File.ReadAllText(file));
        }
        catch (Exception ex)
        {
            _app.Dialogs.Warn(L.T("The file couldn't be read:\n") + ex.Message, L.T("Import"));
            return;
        }
        var merge = _app.Dialogs.ConfirmOrCancel(
            L.F("{0} folders, {1} configured entries, {2} favorites.\n\n", imported.Folders.Count, imported.Entries.Count, imported.Favorites.Count) +
            L.T("Yes = merge (existing settings stay, folders and profiles are added)\nNo = replace everything\nCancel = change nothing"),
            L.T("Import settings"));
        if (merge == null) return;
        var result = merge == true ? SettingsTransfer.Merge(_app.Settings, imported) : imported;
        result.Fingerprints = new Dictionary<string, string>(_app.Settings.Fingerprints);
        _app.ApplySettings(result);
        AppLog.Info($"Einstellungen importiert ({(merge == true ? "zusammengeführt" : "ersetzt")}): {file}");
        ReopenRequested?.Invoke();
    }

    private async Task CheckUpdatesAsync()
    {
        IsChecking = true;
        UpdateStatus = L.T("Checking for updates …");
        var r = await _app.CheckUpdatesAsync(manual: true, includePrereleases: _s.IncludePrereleases);
        IsChecking = false;
        UpdateStatus = _app.UpdateError != null
            ? L.T("Check failed: ") + _app.UpdateError
            : r != null ? L.F("Version {0} is available – see the notice above.", r.Version.ToString(3)) : L.F("You're using the latest version ({0}).", _app.CurrentVersionText);
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
        // Panel: Formular, aber „angeheftet“ und eine am Panel gezogene Größe aus dem laufenden Zustand übernehmen
        var panel = Panel.ToConfig();
        if (_app.Settings.Panels.FirstOrDefault() is { } live)
        {
            panel.Pinned = live.Pinned;
            (panel.PanelWidth, panel.PanelHeightPercent) = Panel.EffectiveSize(live);
        }
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
public sealed record DesignPresetChoice(DesignPreset Preset, bool IsCurrent, ICommand ApplyCommand, ICommand? DeleteCommand = null)
{
    public bool IsCustom => Preset.IsCustom;
    /// <summary>Akzent der Vorschau: eigene Akzentfarbe der Vorlage, sonst die aktuelle Akzentfarbe.</summary>
    public ArgbColor? AccentSwatch => ArgbColor.FromHex(Preset.AccentColor, 255);
    /// <summary>Kurzinfo zum Hintergrund (Glas, Vollton …) für die Kachel.</summary>
    public string BackdropText => Preset.Backdrop switch
    {
        PanelBackdrop.Glass => L.T("Glass"),
        PanelBackdrop.Solid => L.T("Solid"),
        _ => Preset.Opacity < 100 ? L.F("Transparent {0} %", Preset.Opacity) : L.T("Opaque")
    };
    public string Name => Preset.Name;
    public string Description => Preset.Description;
    /// <summary>Vorschau: Panelfarbe bzw. Grundfarbe des Designs.</summary>
    public ArgbColor SwatchColor { get; } = PanelColors.TryParse(Preset.PanelColor, out var c)
        ? ArgbColor.From(c)
        : Preset.Theme == ThemeMode.Light ? new ArgbColor(255, 0xF3, 0xF3, 0xF3) : new ArgbColor(255, 0x2B, 0x2B, 0x2B);
    /// <summary>Schriftfarbe auf der Vorschau (schwarz auf hell, weiß auf dunkel).</summary>
    public ArgbColor InkColor { get; } = PanelColors.TryParse(Preset.PanelColor, out var c) && PanelColors.IsLight(c) || Preset.Theme == ThemeMode.Light && Preset.PanelColor.Length == 0
        ? new ArgbColor(255, 0, 0, 0) : new ArgbColor(255, 255, 255, 255);
}

/// <summary>Ein abonnierter Team-Katalog in den Einstellungen.</summary>
public sealed class CatalogRow
{
    private readonly ISettingsWindowHost _app;
    private readonly Action _changed;

    public CatalogRow(ISettingsWindowHost app, CatalogSubscription sub, Action changed)
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
    public string Status => Subscription.LastError.Length > 0 ? L.T("Problem: ") + Subscription.LastError
        : Subscription.PendingSummary.Length > 0 ? L.T("Changes available: ") + Subscription.PendingSummary
        : L.F("{0} entries", Count) + (Subscription.LastAppliedUtc is { } t ? L.F(" · updated {0}", RecentRunList.Ago(t)) : string.Empty)
          + (Subscription.LastCheckedUtc is { } c ? L.F(" · checked {0}", RecentRunList.Ago(c)) : string.Empty);
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

