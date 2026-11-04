using System.Windows.Input;
using System.Windows.Media;
using PowerExec.Core.Config;
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
        // „subfolder“: Unterordner einer smarten Gruppe (nicht gespeichert, ergibt sich aus dem Ordner)
        GroupPath = kind is "group" or "subfolder" ? title : null;
        Title = GroupPath != null ? Core.Config.GroupManager.LeafOf(title) : title;
        Depth = GroupPath != null ? Core.Config.GroupManager.DepthOf(title) : 0;
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
        FolderSettingsCommand = new RelayCommand(() => app.ShowSettings(GroupPath, "groups"));
        ResetOrderCommand = new RelayCommand(() => _panel.ResetOrder(this), () => AllowReorder && app.Settings.CustomOrder.ContainsKey(Key));

        // Eigene Gruppen
        RenameCommand = new RelayCommand(() => app.RenameGroup(GroupPath!), () => IsUserGroup);
        DeleteCommand = new RelayCommand(() => app.DeleteGroup(GroupPath!, TotalCount), () => IsUserGroup);
        MoveUpCommand = new RelayCommand(() => app.MoveGroup(GroupPath!, -1), () => IsUserGroup);
        MoveDownCommand = new RelayCommand(() => app.MoveGroup(GroupPath!, 1), () => IsUserGroup);
        NewGroupCommand = new RelayCommand(() => app.CreateGroup());
        NewSubgroupCommand = new RelayCommand(() => app.CreateGroup(GroupPath), () => IsUserGroup);
        HideFolderCommand = new RelayCommand(() => app.HideGroupInWorkspace(GroupPath!), () => GroupPath != null);
        IsSmartGroup = GroupPath != null && Core.Config.SmartGroups.FolderOf(app.Settings, GroupPath) != null;
        ChooseIconCommand = new RelayCommand(() => app.ChooseGroupIcon(GroupPath!), () => IsUserGroup);
        _app = app;
        SetLayoutCommand = new RelayCommand(p => app.SetGroupLayout(GroupPath!, p?.ToString() switch { "Grid" => ItemLayout.Grid, "List" => ItemLayout.List, _ => null }), _ => GroupPath != null);
        ToggleLayoutCommand = new RelayCommand(() => app.SetGroupLayout(GroupPath!, IsGrid ? ItemLayout.List : ItemLayout.Grid), () => GroupPath != null);
        SetColorCommand = new RelayCommand(p => app.SetGroupColor(GroupPath!, p?.ToString()), _ => GroupPath != null);
        CustomColorCommand = new RelayCommand(() => app.PickGroupColor(GroupPath!), () => GroupPath != null);
        ExportCommand = new RelayCommand(() => app.ExportGroup(GroupPath!), () => IsUserGroup);
        ImportCommand = new RelayCommand(() => app.ImportEntries(GroupPath), () => IsUserGroup || Kind == "custom");
        AddFromCatalogCommand = new RelayCommand(() => app.AddFromCatalog(GroupPath));
        AddFromClipboardCommand = new RelayCommand(() => app.AddFromClipboard(GroupPath));
        CustomIcon = GroupPath != null ? Core.Config.GroupManager.IconOf(app.Settings, GroupPath).ToString() : string.Empty;
        NewEntryCommand = new RelayCommand(p => app.NewCustomEntry(p?.ToString(), GroupPath));
        AddFilesCommand = new RelayCommand(() => app.AddFilesDialog(GroupPath));
    }

    /// <summary>Vom Benutzer angelegte Gruppe (umbenennen, löschen, Einträge hineinziehen).</summary>
    public bool IsUserGroup => Kind == "group";

    /// <summary>Gruppen, in die man Einträge ziehen kann (eigene Gruppen, Ordner, „Eigene Einträge“, „Ohne Gruppe“, Favoriten).</summary>
    public bool AcceptsItems => Kind is "group" or "subfolder" or "folder" or "custom" or "ungrouped" or "fav";

    /// <summary>Gruppe für Zuordnungen: eigene Gruppe = ihr Pfad, sonst null (= aus der Gruppe nehmen).</summary>
    public string? GroupName => GroupPath;

    /// <summary>Pfad einer eigenen Gruppe („Server/Exchange“), sonst null.</summary>
    public string? GroupPath { get; }

    /// <summary>Ebene (0 = oberste Ebene).</summary>
    public int Depth { get; }

    /// <summary>Pfad für die Anzeige („Kunde A › AD“); bei Sondergruppen der Titel.</summary>
    public string Breadcrumb => GroupPath != null ? string.Join(" › ", GroupPath.Split(GroupManager.Separator)) : Title;

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
    /// <summary>Gruppe mit hinterlegtem Ordner.</summary>
    public bool IsSmartGroup { get; }

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

    // ---- Farbe und Darstellung je Gruppe -----------------------------------------------

    private readonly AppController _app;

    public ICommand SetLayoutCommand { get; }
    public ICommand ToggleLayoutCommand { get; }
    public ICommand SetColorCommand { get; }
    public ICommand CustomColorCommand { get; }

    /// <summary>Darstellung der Gruppe: eigene Einstellung oder die des Panels.</summary>
    public bool IsGrid => (GroupPath != null ? GroupManager.LayoutOf(_app.Settings, GroupPath) : null) is { } own ? own == ItemLayout.Grid : _panel.IsGrid;
    /// <summary>„panel“, „List“ oder „Grid“ (für die Haken im Menü).</summary>
    public string LayoutSetting => GroupPath != null && GroupManager.LayoutOf(_app.Settings, GroupPath) is { } l ? l.ToString() : "panel";
    public string LayoutGlyph => IsGrid ? "\uE8FD" : "\uE8A9";
    public string LayoutToolTip => IsGrid ? "Diese Gruppe als Liste anzeigen" : "Diese Gruppe als Raster anzeigen";
    public bool CanChangeLayout => GroupPath != null;

    internal void RefreshLayout()
    {
        foreach (var p in new[] { nameof(IsGrid), nameof(LayoutSetting), nameof(LayoutGlyph), nameof(LayoutToolTip) }) OnPropertyChanged(p);
    }

    /// <summary>Wirksame Farbe (eigene oder geerbt), leer = keine.</summary>
    public string ColorHex => GroupPath != null ? GroupManager.ColorOf(_app.Settings, GroupPath) : string.Empty;
    public string OwnColorHex => GroupPath != null ? GroupManager.OwnColorOf(_app.Settings, GroupPath) : string.Empty;
    public bool HasColor => ColorHex.Length > 0;

    private Brush? BrushOf(byte alpha) =>
        PanelColors.TryParse(ColorHex, out var c) ? Freeze(new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B))) : null;
    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    /// <summary>Symbol in voller Farbe.</summary>
    public Brush? AccentBrush => BrushOf(255);
    /// <summary>Getönte Karte: Hintergrund dezent, Rand etwas kräftiger.</summary>
    public Brush CardBackground => BrushOf(0x24) ?? Brushes.Transparent;
    public Brush CardBorder => BrushOf(0x73) ?? Brushes.Transparent;

    /// <summary>Farben für das Menü „Farbe“.</summary>
    public IReadOnlyList<GroupColorChoice> ColorChoices => PanelColors.AccentPresets
        .Select(p => new GroupColorChoice(p.Name, p.Hex, OwnColorHex.Equals(p.Hex, StringComparison.OrdinalIgnoreCase), SetColorCommand)).ToList();
    public ICommand AddFilesCommand { get; }

    public string Key { get; }
    public string Title { get; }
    public bool IsFavorites { get; }
    public bool IsFolder => FolderPath != null;
    public string Kind { get; }
    public bool AllowReorder { get; }

    /// <summary>Symbol im Gruppenkopf (Segoe Fluent Icons).</summary>
    /// <summary>Tooltip am Punkt der Sprungleiste.</summary>
    public string RailToolTip => $"{Title} ({CountText}) – Klick: aufklappen und hinspringen · erneuter Klick: zuklappen";

    /// <summary>Gruppe eines abonnierten Team-Katalogs.</summary>
    public bool IsCatalog => GroupPath != null && _app.Settings.Catalogs.Any(c => GroupManager.IsSameOrBelow(GroupPath, GroupManager.Normalize(c.Name)));

    public string IconGlyph => IsCatalog && GroupPath != null && _app.Settings.Catalogs.Any(c => GroupManager.Normalize(c.Name).Equals(GroupPath, StringComparison.CurrentCultureIgnoreCase)) ? "\uE72E" : Kind switch
    {
        "fav" => "\uE735",
        "recent" => "\uE81C",
        "best" => "\uE721",
        "category" or "group" when FolderPath != null => "\uE8B7",
        "category" or "group" => "\uE8EC",
        "subfolder" => "\uE8B7",
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

/// <summary>Eintrag im Menü „Farbe“ einer Gruppe.</summary>
public sealed record GroupColorChoice(string Name, string Hex, bool IsCurrent, ICommand Command)
{
    public Brush Swatch { get; } = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Hex));
}
