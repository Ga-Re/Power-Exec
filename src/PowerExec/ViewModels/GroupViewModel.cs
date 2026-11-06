using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Infrastructure;

namespace PowerExec.ViewModels;

/// <summary>Wo an dieser Gruppe beim Ziehen einer anderen die Lücke aufgeht.</summary>
public enum GroupDropSlot { None, Before, After, Inside }

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
        Items = new ObservableCollection<ItemViewModel>(items);
        _isExpanded = expanded;
        Error = error;

        ToggleCommand = new RelayCommand(() =>
        {
            // Nur der Klick auf den Kopf klappt weich auf/zu (nicht Suche oder „Alle ausklappen“)
            AnimateExpand = true;
            IsExpanded = !IsExpanded;
            AnimateExpand = false;
        });
        OpenInExplorerCommand = new RelayCommand(() => { if (FolderPath != null) app.Shell.OpenLocation(FolderPath, isFolder: true); }, () => FolderPath != null);
        RescanCommand = new RelayCommand(() => _ = app.Repository.ScanAsync());
        CollapseOthersCommand = new RelayCommand(() => _panel.CollapseAllExcept(this));
        CollapseAllCommand = new RelayCommand(() => _panel.SetAllExpanded(false));
        ExpandAllCommand = new RelayCommand(() => _panel.SetAllExpanded(true));
        FolderSettingsCommand = new RelayCommand(() => app.ShowSettings(GroupPath, "groups"));
        ResetOrderCommand = new RelayCommand(() => _panel.ResetOrder(this), () => AllowReorder && app.Settings.CustomOrder.ContainsKey(Key));

        // Eigene Gruppen
        RenameCommand = new RelayCommand(() => app.RenameGroup(GroupPath!), () => IsUserGroup);
        DeleteCommand = new RelayCommand(() => app.DeleteGroup(GroupPath!, TotalCount), () => IsUserGroup);
        NewSubgroupCommand = new RelayCommand(() => app.CreateGroup(GroupPath), () => IsUserGroup);
        HideFolderCommand = new RelayCommand(() => app.HideGroupInWorkspace(GroupPath!), () => GroupPath != null);
        IsSmartGroup = GroupPath != null && Core.Config.SmartGroups.FolderOf(app.Settings, GroupPath) != null;
        ChooseIconCommand = new RelayCommand(() => app.ChooseGroupIcon(GroupPath!), () => IsUserGroup);
        _app = app;
        SetLayoutCommand = new RelayCommand(p => app.SetGroupLayout(GroupPath!, p?.ToString() switch { "Grid" => ItemLayout.Grid, "List" => ItemLayout.List, _ => null }), _ => GroupPath != null);
        ToggleLayoutCommand = new RelayCommand(() =>
        {
            // Liste ↔ Raster über den Knopf im Gruppenkopf: Höhe der Gruppe ändert sich weich
            AnimateResize = true;
            app.SetGroupLayout(GroupPath!, IsGrid ? ItemLayout.List : ItemLayout.Grid);
            AnimateResize = false;
        }, () => GroupPath != null);
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

    /// <summary>Feste Gruppe (Zuletzt ausgeführt, Favoriten, Eigene Einträge): Schlüssel, sonst null.</summary>
    public string? FixedKey => Kind switch
    {
        "recent" => FixedGroups.Recent,
        "fav" => FixedGroups.Favorites,
        "custom" => FixedGroups.Custom,
        _ => null
    };

    /// <summary>Lässt sich im Panel ziehen: eigene Gruppen und feste Gruppen (diese nur auf oberster Ebene).</summary>
    public bool CanDrag => IsUserGroup && GroupPath != null || FixedKey != null;

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

    /// <summary>Untergruppen (nur bei eigenen Gruppen). Beim Rebuild abgeglichen statt neu erzeugt.</summary>
    public ObservableCollection<GroupViewModel> Children { get; } = new();
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

    private ArgbColor? ColorOf(byte alpha, bool readable = false)
    {
        if (!PanelColors.TryParse(ColorHex, out var c)) return null;
        if (readable) c = PanelColors.Readable(c, _panel.IsDarkSurface);
        return ArgbColor.From(c, alpha);
    }

    /// <summary>Symbol und Rahmen in voller Farbe – im hellen Design bei Bedarf abgedunkelt (mind. 3:1 Kontrast).</summary>
    public ArgbColor? AccentColor => ColorOf(255, readable: true);
    /// <summary>Getönte Karte: Hintergrund dezent, Rand etwas kräftiger.</summary>
    public ArgbColor CardBackgroundColor => ColorOf(0x24) ?? ArgbColor.Transparent;
    public ArgbColor CardBorderColor => ColorOf(0x73, readable: true) ?? ArgbColor.Transparent;

    /// <summary>Untermenü „Farbe“: Vorgaben, dazu „Eigene Farbe …“ und – wenn gesetzt – „Farbe entfernen“.</summary>
    public IReadOnlyList<GroupColorChoice> ColorChoices
    {
        get
        {
            var own = OwnColorHex;
            var list = PanelColors.AccentPresets
                .Select(p => new GroupColorChoice(p.Name, p.Hex, own.Equals(p.Hex, StringComparison.OrdinalIgnoreCase), SetColorCommand))
                .ToList();
            list.Add(new GroupColorChoice("Eigene Farbe …", string.Empty, false, CustomColorCommand) { Glyph = "\uE790", IsFirstAction = true });
            if (own.Length > 0) list.Add(new GroupColorChoice("Farbe entfernen", string.Empty, false, SetColorCommand) { Glyph = "\uE894" });
            return list;
        }
    }
    public ICommand AddFilesCommand { get; }

    public string Key { get; }
    public string Title { get; }
    public bool IsFavorites { get; }
    public bool IsFolder => FolderPath != null;
    public string Kind { get; }
    private bool _allowReorder;
    /// <summary>Reihenfolge per Drag &amp; Drop änderbar (nicht während der Suche) – ändert sich beim Wiederverwenden.</summary>
    public bool AllowReorder { get => _allowReorder; internal set => Set(ref _allowReorder, value); }

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
    /// <summary>Einträge der Gruppe; beim Rebuild abgeglichen (siehe <see cref="Core.Collections.CollectionSync"/>).</summary>
    public ObservableCollection<ItemViewModel> Items { get; }
    public string CountText => TotalCount.ToString();
    public string? Error { get; }
    public bool HasError => !string.IsNullOrEmpty(Error);

    private bool _animateResize;
    /// <summary>Kurz true, während der Knopf Liste/Raster umschaltet – dann ändert sich die Höhe animiert.</summary>
    public bool AnimateResize { get => _animateResize; private set => Set(ref _animateResize, value); }

    private bool _animateExpand;
    /// <summary>Kurz true, während der Klick auf den Gruppenkopf umschaltet – dann klappt die Gruppe animiert.</summary>
    public bool AnimateExpand { get => _animateExpand; private set => Set(ref _animateExpand, value); }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value)) { _panel.OnGroupExpandedChanged(this); OnPropertyChanged(nameof(ShowDropHint)); }
        }
    }

    /// <summary>Kann diese Gruppe für einen neuen Rebuild weiterverwendet werden? (Alles, was im Konstruktor feststeht, ist gleich.)</summary>
    internal bool CanReuseFor(string title, string kind, bool isFavorites, string? folderPath, string? error) =>
        Title == (kind is "group" or "subfolder" ? GroupManager.LeafOf(title) : title) &&
        GroupPath == (kind is "group" or "subfolder" ? title : null) &&
        Kind == kind && IsFavorites == isFavorites && FolderPath == folderPath && Error == error;

    /// <summary>Nach dem Abgleich von Einträgen und Untergruppen: abhängige Anzeigen aktualisieren.</summary>
    internal void RaiseContentChanged()
    {
        foreach (var p in new[] { nameof(TotalCount), nameof(CountText), nameof(HasChildren), nameof(IsEmptyUserGroup), nameof(ShowDropHint), nameof(RailToolTip) })
            OnPropertyChanged(p);
    }

    // ---- Gruppen ziehen: Lücke und Quelle ----------------------------------------------

    private GroupDropSlot _dropSlot;
    /// <summary>Lücke vor, hinter oder (als letzte Untergruppe) in dieser Gruppe.</summary>
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
    public bool GapInside => _dropSlot == GroupDropSlot.Inside;

    private bool _isDragSource;
    /// <summary>Wird gerade gezogen: macht an der alten Stelle Platz (samt Untergruppen).</summary>
    public bool IsDragSource { get => _isDragSource; internal set => Set(ref _isDragSource, value); }

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
    /// <summary>Farbpunkt; bei Aktionen („Eigene Farbe …“, „Farbe entfernen“) keiner.</summary>
    public ArgbColor? SwatchColor { get; } = ArgbColor.FromHex(Hex);

    /// <summary>Symbol einer Aktion statt Farbpunkt.</summary>
    public string Glyph { get; init; } = string.Empty;
    public bool IsAction => Glyph.Length > 0;

    /// <summary>Erste Aktion nach den Farben: Trennlinie darüber.</summary>
    public bool IsFirstAction { get; init; }
}
