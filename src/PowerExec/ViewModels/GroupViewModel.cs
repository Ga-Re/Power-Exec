using System.Windows.Input;
using PowerExec.Infrastructure;

namespace PowerExec.ViewModels;

public sealed class GroupViewModel : ObservableObject
{
    private readonly PanelViewModel _panel;
    private bool _isExpanded;

    public GroupViewModel(PanelViewModel panel, AppController app, string key, string title, bool isFavorites, string? folderPath,
        IReadOnlyList<ItemViewModel> items, bool expanded, string? error, string kind = "folder", bool allowReorder = true)
    {
        Kind = kind;
        AllowReorder = allowReorder;
        _panel = panel;
        Key = key;
        // Eigene Gruppe: „title“ ist der Pfad („Server/Exchange“), angezeigt wird der letzte Teil
        GroupPath = kind == "group" ? title : null;
        Title = kind == "group" ? Core.Config.GroupManager.LeafOf(title) : title;
        Depth = kind == "group" ? Core.Config.GroupManager.DepthOf(title) : 0;
        IsFavorites = isFavorites;
        FolderPath = folderPath;
        Items = items;
        _isExpanded = expanded;
        Error = error;

        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        OpenInExplorerCommand = new RelayCommand(() => { if (FolderPath != null) Services.Launcher.OpenLocation(FolderPath, isFolder: true); }, () => FolderPath != null);
        RescanCommand = new RelayCommand(() => _ = app.Repository.ScanAsync());
        CollapseOthersCommand = new RelayCommand(() => _panel.CollapseAllExcept(this));
        CollapseAllCommand = new RelayCommand(() => _panel.SetAllExpanded(false));
        ExpandAllCommand = new RelayCommand(() => _panel.SetAllExpanded(true));
        FolderSettingsCommand = new RelayCommand(() => app.ShowSettings(FolderPath));
        ResetOrderCommand = new RelayCommand(() => _panel.ResetOrder(this), () => AllowReorder && app.Settings.CustomOrder.ContainsKey(Key));

        // Eigene Gruppen
        RenameCommand = new RelayCommand(() => app.RenameGroup(GroupPath!), () => IsUserGroup);
        DeleteCommand = new RelayCommand(() => app.DeleteGroup(GroupPath!, TotalCount), () => IsUserGroup);
        MoveUpCommand = new RelayCommand(() => app.MoveGroup(GroupPath!, -1), () => IsUserGroup);
        MoveDownCommand = new RelayCommand(() => app.MoveGroup(GroupPath!, 1), () => IsUserGroup);
        NewGroupCommand = new RelayCommand(() => app.CreateGroup());
        NewSubgroupCommand = new RelayCommand(() => app.CreateGroup(GroupPath), () => IsUserGroup);
        HideFolderCommand = new RelayCommand(() => app.SetFolderGroupVisible(FolderPath!, false), () => FolderPath != null);
        ChooseIconCommand = new RelayCommand(() => app.ChooseGroupIcon(GroupPath!), () => IsUserGroup);
        ExportCommand = new RelayCommand(() => app.ExportGroup(GroupPath!), () => IsUserGroup);
        ImportCommand = new RelayCommand(() => app.ImportEntries(GroupPath), () => IsUserGroup || Kind == "custom");
        AddFromCatalogCommand = new RelayCommand(() => app.AddFromCatalog(GroupPath));
        AddFromClipboardCommand = new RelayCommand(() => app.AddFromClipboard(GroupPath));
        CustomIcon = GroupPath != null ? Core.Config.GroupManager.IconOf(app.Settings, GroupPath).ToString() : string.Empty;
        NewEntryCommand = new RelayCommand(p =>
        {
            var kind = Enum.TryParse<Core.Config.CustomEntryKind>(p?.ToString(), out var k) ? k : Core.Config.CustomEntryKind.Url;
            app.NewCustomEntry(kind, GroupPath);
        });
        AddFilesCommand = new RelayCommand(() =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = IsUserGroup ? $"Dateien zur Gruppe „{Title}“ hinzufügen" : "Dateien hinzufügen",
                Multiselect = true,
                Filter = "Scripte und Programme (*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc)|*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc|Alle Dateien (*.*)|*.*"
            };
            if (app.WithDialog(() => dlg.ShowDialog()) != true) return;
            app.AddDropped(dlg.FileNames.Select(f => new Core.Config.DroppedTarget(Core.Config.CustomEntryKind.File, f, Path.GetFileName(f))).ToList(),
                GroupPath);
        });
    }

    /// <summary>Vom Benutzer angelegte Gruppe (umbenennen, löschen, Einträge hineinziehen).</summary>
    public bool IsUserGroup => Kind == "group";

    /// <summary>Gruppen, in die man Einträge ziehen kann (eigene Gruppen, Ordner, „Eigene Einträge“, „Ohne Gruppe“, Favoriten).</summary>
    public bool AcceptsItems => Kind is "group" or "folder" or "custom" or "ungrouped" or "fav";

    /// <summary>Gruppe für Zuordnungen: eigene Gruppe = ihr Pfad, sonst null (= aus der Gruppe nehmen).</summary>
    public string? GroupName => GroupPath;

    /// <summary>Pfad einer eigenen Gruppe („Server/Exchange“), sonst null.</summary>
    public string? GroupPath { get; }

    /// <summary>Ebene (0 = oberste Ebene).</summary>
    public int Depth { get; }

    /// <summary>Untergruppen (nur bei eigenen Gruppen).</summary>
    public List<GroupViewModel> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;

    /// <summary>Einträge dieser Gruppe und aller Untergruppen.</summary>
    public int TotalCount => Items.Count + Children.Sum(c => c.TotalCount);

    /// <summary>Diese Gruppe und alle Untergruppen (für Auswahl, Tastatur, Drag &amp; Drop).</summary>
    public IEnumerable<GroupViewModel> SelfAndDescendants => new[] { this }.Concat(Children.SelectMany(c => c.SelfAndDescendants));

    public bool IsEmptyUserGroup => IsUserGroup && Items.Count == 0 && Children.Count == 0;
    public bool ShowDropHint => IsEmptyUserGroup && IsExpanded;
    public bool IsUserGroupOrCustom => IsUserGroup || Kind == "custom";

    public ICommand RenameCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand NewGroupCommand { get; }
    public ICommand NewSubgroupCommand { get; }
    public ICommand ChooseIconCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand AddFromCatalogCommand { get; }
    public ICommand AddFromClipboardCommand { get; }

    /// <summary>Selbst gewähltes Symbol der Gruppe (IconRef-Text), leer = Standardsymbol.</summary>
    public string CustomIcon { get; }
    public bool HasCustomIcon => CustomIcon.Length > 0;
    public ICommand HideFolderCommand { get; }
    public ICommand NewEntryCommand { get; }
    public ICommand AddFilesCommand { get; }

    public string Key { get; }
    public string Title { get; }
    public bool IsFavorites { get; }
    public bool IsFolder => FolderPath != null;
    public string Kind { get; }
    public bool AllowReorder { get; }

    /// <summary>Symbol im Gruppenkopf (Segoe Fluent Icons).</summary>
    public string IconGlyph => Kind switch
    {
        "fav" => "\uE735",
        "recent" => "\uE81C",
        "category" or "group" => "\uE8EC",
        "ungrouped" => "\uE8FD",
        "custom" => "\uE71B",
        _ => "\uE8B7"
    };
    public string? FolderPath { get; }
    public IReadOnlyList<ItemViewModel> Items { get; }
    public string CountText => TotalCount.ToString();
    public string? Error { get; }
    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value)) { _panel.OnGroupExpandedChanged(this); OnPropertyChanged(nameof(ShowDropHint)); }
        }
    }

    internal void SetExpandedSilently(bool value)
    {
        _isExpanded = value;
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(ShowDropHint));
    }

    public ICommand ToggleCommand { get; }
    public ICommand OpenInExplorerCommand { get; }
    public ICommand RescanCommand { get; }
    public ICommand CollapseOthersCommand { get; }
    public ICommand CollapseAllCommand { get; }
    public ICommand ExpandAllCommand { get; }
    public ICommand FolderSettingsCommand { get; }
    public ICommand ResetOrderCommand { get; }
}
