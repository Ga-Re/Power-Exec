using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;
using PowerExec.Core.Profiles;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

public sealed class PanelViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly PanelHost _host;
    private string _searchText = string.Empty;
    private TypeFilter _filter = TypeFilter.All;
    private string? _selectedKey;
    private string _footer = string.Empty;
    private bool _updateDismissed;
    private string? _folderWarning;

    public PanelViewModel(AppController app, PanelHost host)
    {
        _app = app;
        _host = host;
        _filter = host.Config.DefaultFilter;
        SetLayoutCommand = new RelayCommand(p => { if (Enum.TryParse<ItemLayout>(p?.ToString(), out var l)) Layout = l; });
        SwitchWorkspaceCommand = new RelayCommand(p => { if (p is string name) _app.SwitchWorkspace(name); });
        SettingsCommand = new RelayCommand(() => _app.ShowSettings());
        RescanCommand = new RelayCommand(() => _ = _app.Repository.ScanAsync());
        UpdateNowCommand = new RelayCommand(() => _ = _app.InstallUpdateAsync(), () => !UpdateBusy);
        UpdateLaterCommand = new RelayCommand(() => { _updateDismissed = true; OnPropertyChanged(nameof(UpdateVisible)); });
        _app.Update.PropertyChanged += OnUpdateChanged;
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        AddFolderCommand = new RelayCommand(() => _app.ShowSettings());
    }

    public ObservableCollection<GroupViewModel> Groups { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsSearching));
                Rebuild();
            }
        }
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(_searchText);

    public TypeFilter Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value)) Rebuild();
        }
    }

    public PanelConfig Config => _host.Config;
    public string PanelName => _host.Config.Name;
    public bool IsMultiPanel => _app.Settings.Panels.Count > 1;

    public bool IsPinned
    {
        get => _host.Config.Pinned;
        set
        {
            if (_host.Config.Pinned == value) return;
            _host.Config.Pinned = value;
            _app.SaveSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>Darstellung: Liste, kompakte Liste oder Raster.</summary>
    public ItemLayout Layout
    {
        get => _host.Config.Layout;
        set
        {
            if (_host.Config.Layout == value) return;
            _host.Config.Layout = value;
            _app.SaveSettings();
            RaiseLayout();
        }
    }

    private void RaiseLayout()
    {
        foreach (var p in new[] { nameof(Layout), nameof(IsCompact), nameof(IsGrid), nameof(IsList), nameof(LayoutGlyph), nameof(LayoutToolTip) }) OnPropertyChanged(p);
    }

    /// <summary>Kompakt: Liste einzeilig, Raster mit kleineren Kacheln.</summary>
    public bool IsCompact
    {
        get => _host.Config.Compact;
        set
        {
            if (_host.Config.Compact == value) return;
            _host.Config.Compact = value;
            _app.SaveSettings();
            RaiseLayout();
        }
    }

    public bool IsGrid
    {
        get => Layout == ItemLayout.Grid;
        set => Layout = value ? ItemLayout.Grid : ItemLayout.List;
    }

    public bool IsList => !IsGrid;

    /// <summary>Symbol des Umschalters Liste/Raster im Panel-Kopf (zeigt die aktuelle Darstellung).</summary>
    public string LayoutGlyph => IsGrid ? "\uE8A9" : "\uE8FD";
    public string LayoutToolTip => IsGrid ? "Raster – klicken für Liste" : "Liste – klicken für Raster";

    public ICommand SetLayoutCommand { get; }
    public ICommand ToggleGridCommand => new RelayCommand(() => IsGrid = !IsGrid);
    public ICommand ToggleCompactCommand => new RelayCommand(() => IsCompact = !IsCompact);

    // Rechtsklick auf freie Fläche
    public ICommand NewEntryCommand => new RelayCommand(p =>
        _app.NewCustomEntry(Enum.TryParse<CustomEntryKind>(p?.ToString(), out var k) ? k : CustomEntryKind.Url, null));
    public ICommand AddFromCatalogCommand => new RelayCommand(() => _app.AddFromCatalog(null));
    public ICommand AddFromClipboardCommand => new RelayCommand(() => _app.AddFromClipboard(SelectedItem is { } s ? GroupManager.GroupOf(_app.Settings, s.Item.FullPath) is { Length: > 0 } g ? g : null : null));
    public ICommand ImportEntriesCommand => new RelayCommand(() => _app.ImportEntries());
    public ICommand AddFilesCommand => new RelayCommand(() =>
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Dateien hinzufügen",
            Multiselect = true,
            Filter = "Scripte und Programme (*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc)|*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc|Alle Dateien (*.*)|*.*"
        };
        if (_app.WithDialog(() => dlg.ShowDialog()) != true) return;
        _app.AddDropped(dlg.FileNames.Select(f => new DroppedTarget(CustomEntryKind.File, f, Path.GetFileName(f))).ToList(), null);
    });

    /// <summary>Panel wird angezeigt: eigene Einträge im Hintergrund prüfen (höchstens alle 10 Minuten je Eintrag).</summary>
    public void OnShown()
    {
        if (_app.Health == null) return;
        var keys = new HashSet<string>(AllItems.Where(i => i.Item.Source != null).Select(i => i.Item.FullPath), StringComparer.OrdinalIgnoreCase);
        var entries = _app.Settings.CustomEntries.Where(c => keys.Contains(c.Key)).ToList();
        if (entries.Count > 0) _ = _app.Health.CheckAsync(entries);
    }

    // Arbeitsbereiche
    public IReadOnlyList<string> Workspaces => _app.Settings.Workspaces.Select(w => w.Name).ToList();
    public string ActiveWorkspace => _app.Settings.ActiveWorkspace;
    public bool HasWorkspaces => _app.Settings.Workspaces.Count > 1;
    public ICommand SwitchWorkspaceCommand { get; }

    public string HotkeyHint => HotkeyService.Display(_host.Config.Hotkey);
    public bool HasHotkey => !string.IsNullOrWhiteSpace(_host.Config.Hotkey);

    public string Footer { get => _footer; private set => Set(ref _footer, value); }
    public string? FolderWarning { get => _folderWarning; private set => Set(ref _folderWarning, value); }
    public bool HasNoFolders => _app.Settings.Folders.Count == 0 && _app.Settings.CustomEntries.Count == 0 && GroupManager.All(_app.Settings).Count == 0;
    public bool IsEmpty => Groups.Count == 0 && !HasNoFolders;

    // Update-Hinweis
    public bool UpdateVisible => _app.Update.Available && !_updateDismissed;
    public string UpdateTitle => _app.Update.Title;
    public string UpdateSubtitle => _app.Update.Subtitle;
    public bool UpdateBusy => _app.Update.Busy;

    private void OnUpdateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateState.Available)) _updateDismissed = false;
        foreach (var p in new[] { nameof(UpdateVisible), nameof(UpdateTitle), nameof(UpdateSubtitle), nameof(UpdateBusy) }) OnPropertyChanged(p);
    }

    /// <summary>Beim Entfernen des Panels: Ereignisse abmelden.</summary>
    public void Detach() => _app.Update.PropertyChanged -= OnUpdateChanged;

    public ICommand SettingsCommand { get; }
    public ICommand RescanCommand { get; }
    public ICommand UpdateNowCommand { get; }
    public ICommand UpdateLaterCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand AddFolderCommand { get; }

    /// <summary>Baut die Gruppen anhand von Suche, Filter und Favoriten neu auf.</summary>
    public void Rebuild()
    {
        var settings = _app.Settings;
        var repo = _app.Repository;
        // „#schlagwort“ filtert nach Schlagworten, der Rest ist normale Suche
        var (tagFilter, query) = Tags.SplitQuery(_searchText.Trim());
        bool tagsOk(LaunchItem i) => tagFilter.Count == 0 || Tags.Matches(Tags.Of(settings, i.FullPath), tagFilter);
        bool searching = query.Length > 0 || tagFilter.Count > 0;
        var collapsed = new HashSet<string>(settings.CollapsedGroups, StringComparer.OrdinalIgnoreCase);

        // Nur sichtbare Einträge (Haken in den Ordner-Einstellungen, neue Dateien ggf. erst nach Freigabe)
        var folderByPath = settings.Folders.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var pending = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var visible = new List<LaunchItem>();
        var cfg = _host.Config;
        // Inhaltsauswahl des Panels: eine angehakte Gruppe umfasst auch ihre Untergruppen
        bool GroupSelected(string path) => cfg.Categories.Count == 0 ||
            (path.Length == 0 ? cfg.ShowsCategory(string.Empty) : cfg.Categories.Any(c => c.Length > 0 && GroupManager.IsSameOrBelow(path, GroupManager.Normalize(c))));
        bool CategoryOk(LaunchItem i) => GroupSelected(GroupManager.GroupOf(settings, i.FullPath));
        foreach (var i in repo.Items)
        {
            if (!folderByPath.TryGetValue(i.RootFolder, out var folder) || !cfg.ShowsFolder(folder.Path)) continue;
            var state = FolderVisibility.State(folder, FolderVisibility.RelativePath(folder, i.FullPath));
            if (state == EntryVisibility.Visible) { if (CategoryOk(i)) visible.Add(i); }
            else if (state == EntryVisibility.Pending) pending[folder.Path] = pending.GetValueOrDefault(folder.Path) + 1;
        }
        // Eigene Einträge ohne Datei (Webseiten, Ordner, RDP, Befehle)
        var customItems = new List<LaunchItem>();
        if (cfg.ShowsFolder(PanelConfig.CustomEntriesKey))
        {
            var seen = new HashSet<string>(visible.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
            foreach (var c in settings.CustomEntries)
            {
                if (c.Target.Trim().Length == 0) continue;
                var li = _app.DescribeCustom(c);
                if (!seen.Add(li.FullPath)) continue; // Datei liegt bereits in einem durchsuchten Ordner
                if (CategoryOk(li)) customItems.Add(li);
            }
        }
        visible.AddRange(customItems);
        UpdatePending(pending);

        bool Matches(LaunchItem i)
        {
            if (ItemFilter.MatchesQuery(i, query)) return true;
            var e = settings.FindEntry(i.FullPath);
            return e != null && query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(t => (i.SearchText + " " + e.DisplayName + " " + e.Description + " " + e.Notes + " " + e.Category + " " + string.Join(' ', e.Tags) + " " + i.Source?.Code)
                    .Contains(t, StringComparison.CurrentCultureIgnoreCase));
        }

        var pool = visible
            .Where(i => ItemFilter.MatchesType(i, _filter, _app.IsFavorite(i.FullPath)) && tagsOk(i) && (query.Length == 0 || Matches(i)))
            .ToList();

        IEnumerable<LaunchItem> Order(IEnumerable<LaunchItem> items) => searching
            ? items.OrderBy(i => ItemFilter.Rank(i, query)).ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            : items.OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase);

        // Eigene Reihenfolge (Drag & Drop) gilt nur ohne Suche
        List<LaunchItem> Arrange(string key, IEnumerable<LaunchItem> items)
        {
            var ordered = Order(items).ToList();
            return searching ? ordered : ItemOrdering.Apply(ordered, i => i.FullPath, settings.CustomOrder.GetValueOrDefault(key));
        }

        GroupViewModel MakeGroup(string key, string title, string kind, string? folderPath, IEnumerable<LaunchItem> items, string? error = null) =>
            new(this, _app, key, title, kind == "fav", folderPath,
                Arrange(key, items).Select(i => new ItemViewModel(_app, this, i, key)).ToList(),
                searching || !collapsed.Contains(key), error, kind, allowReorder: !searching);

        var groups = new List<GroupViewModel>();
        if (!searching && _filter == TypeFilter.All)
        {
            // Zuletzt ausgeführt (mit dem jeweils verwendeten Profil)
            if (settings.RecentRunsCount > 0 && cfg.ShowRecent)
            {
                var byPath = pool.GroupBy(i => i.FullPath, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var recent = settings.RecentRuns
                    .Where(r => byPath.ContainsKey(r.Path))
                    .Take(settings.RecentRunsCount)
                    .Select(r =>
                    {
                        var item = byPath[r.Path];
                        var profile = ProfileResolver.GetProfiles(settings, item)
                            .FirstOrDefault(p => p.Name.Equals(r.ProfileName, StringComparison.OrdinalIgnoreCase)) ?? ProfileResolver.GetDefault(settings, item);
                        var subtitle = (profile.IsImplicit ? string.Empty : profile.Name + " · ") + RecentRunList.Ago(r.TimeUtc);
                        return new ItemViewModel(_app, this, item, "recent", profile, subtitle);
                    })
                    .ToList();
                if (recent.Count > 0)
                    groups.Add(new GroupViewModel(this, _app, "recent", "Zuletzt ausgeführt", false, null, recent,
                        !collapsed.Contains("recent"), null, "recent", allowReorder: false));
            }

            var favs = pool.Where(i => _app.IsFavorite(i.FullPath)).ToList();
            if (favs.Count > 0 && cfg.ShowFavorites) groups.Add(MakeGroup("fav", "Favoriten", "fav", null, favs));
        }

        // Eigene Gruppen: gruppierte Einträge erscheinen in ihrer Gruppe statt in ihrem Ordner
        string GroupOf(LaunchItem i) => GroupManager.GroupOf(settings, i.FullPath);
        bool showEmpty = !searching && _filter == TypeFilter.All;
        // Baum: jede Gruppe mit ihren Einträgen, Untergruppen darunter (Reihenfolge wie in den Einstellungen)
        var nodes = new Dictionary<string, GroupViewModel>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var path in GroupManager.All(settings))
        {
            bool visibleGroup = GroupSelected(path) || cfg.Categories.Any(c => GroupManager.IsSameOrBelow(GroupManager.Normalize(c), path));
            if (!visibleGroup) continue;
            var items = GroupSelected(path) ? pool.Where(i => GroupOf(i).Equals(path, StringComparison.CurrentCultureIgnoreCase)).ToList() : new List<LaunchItem>();
            var node = MakeGroup(GroupManager.GroupKey(path), path, "group", null, items);
            nodes[path] = node;
            if (GroupManager.ParentOf(path) is { } parent && nodes.TryGetValue(parent, out var p)) p.Children.Add(node);
            else if (GroupManager.ParentOf(path) == null) groups.Add(node);
        }
        // Leere Zweige bei Suche/Filter weglassen
        bool Prune(GroupViewModel g)
        {
            g.Children.RemoveAll(c => !Prune(c));
            return showEmpty || g.TotalCount > 0;
        }
        groups.RemoveAll(g => g.IsUserGroup && !Prune(g));
        var rest = pool.Where(i => GroupOf(i).Length == 0).ToList();

        if (settings.Grouping == GroupingMode.Category)
        {
            if (rest.Count > 0) groups.Add(MakeGroup(GroupManager.KeyPrefix, "Ohne Gruppe", "ungrouped", null, rest));
        }
        else
        {
            foreach (var folder in settings.Folders.Where(f => cfg.ShowsFolder(f.Path) && (f.ShowGroup || searching)))
            {
                var status = repo.FolderStatuses.FirstOrDefault(s => string.Equals(s.Path, folder.Path, StringComparison.OrdinalIgnoreCase));
                var items = rest.Where(i => string.Equals(i.RootFolder, folder.Path, StringComparison.OrdinalIgnoreCase)).ToList();
                if (items.Count == 0 && (searching || _filter != TypeFilter.All || status?.Error == null)) continue;
                groups.Add(MakeGroup("folder:" + folder.Path, folder.Path, "folder", folder.Path, items, status?.Error));
            }
            var customVisible = rest.Where(i => i.IsCustom).ToList();
            if (customVisible.Count > 0) groups.Add(MakeGroup("custom", "Eigene Einträge", "custom", null, customVisible));
        }

        Groups.Clear();
        foreach (var g in groups) Groups.Add(g);
        OnPropertyChanged(nameof(SelectedItem)); OnPropertyChanged(nameof(HasSelection));

        // Auswahl wiederherstellen
        var selected = AllItems.FirstOrDefault(i => i.Key == _selectedKey);
        if (selected != null) selected.IsSelected = true;
        else _selectedKey = null;

        var errors = repo.FolderStatuses.Where(s => s.Error != null).ToList();
        FolderWarning = errors.Count == 0 ? null
            : errors.Count == 1 ? $"{errors[0].Path}: {errors[0].Error}"
            : $"{errors.Count} Ordner nicht erreichbar";

        if (repo.IsScanning && repo.Items.Count == 0) Footer = "Ordner werden eingelesen …";
        else if (searching) Footer = $"{pool.Count} Treffer für „{_searchText.Trim()}“";
        else Footer = $"{visible.Count} Einträge · eingelesen {repo.LastScan:HH:mm}" + (HasWorkspaces ? $" · {settings.ActiveWorkspace}" : string.Empty);

        OnPropertyChanged(nameof(HasNoFolders));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HotkeyHint));
        OnPropertyChanged(nameof(HasHotkey));
        OnPropertyChanged(nameof(IsPinned));
        RaiseLayout();
        OnPropertyChanged(nameof(Workspaces));
        OnPropertyChanged(nameof(ActiveWorkspace));
        OnPropertyChanged(nameof(HasWorkspaces));
        OnPropertyChanged(nameof(PanelName));
        OnPropertyChanged(nameof(IsMultiPanel));
    }

    // ---- Neue Einträge, die auf Freigabe warten -----------------------------

    private string? _pendingText;
    private string? _pendingFolder;

    public string? PendingText { get => _pendingText; private set => Set(ref _pendingText, value); }
    public ICommand ReviewPendingCommand => new RelayCommand(() => _app.ShowSettings(_pendingFolder));

    private void UpdatePending(Dictionary<string, int> pending)
    {
        var total = pending.Values.Sum();
        _pendingFolder = pending.Keys.FirstOrDefault();
        PendingText = total == 0 ? null
            : pending.Count == 1
                ? $"{total} {(total == 1 ? "neuer Eintrag" : "neue Einträge")} in {_pendingFolder} {(total == 1 ? "wartet" : "warten")} auf Freigabe"
                : $"{total} neue Einträge in {pending.Count} Ordnern warten auf Freigabe";
    }

    /// <summary>Alle Gruppen einschließlich Untergruppen.</summary>
    public IEnumerable<GroupViewModel> AllGroups => Groups.SelectMany(g => g.SelfAndDescendants);

    public IEnumerable<ItemViewModel> AllItems => AllGroups.SelectMany(g => g.Items);

    /// <summary>Drag &amp; Drop: Eintrag innerhalb seiner Gruppe vor bzw. hinter einen anderen schieben.</summary>
    public void MoveItem(ItemViewModel dragged, ItemViewModel target, bool after)
    {
        var group = AllGroups.FirstOrDefault(g => g.Items.Contains(dragged));
        if (group == null || !group.AllowReorder || !group.Items.Contains(target) || ReferenceEquals(dragged, target)) return;
        var current = group.Items.Select(i => i.Item.FullPath).ToList();
        _app.Settings.CustomOrder[group.Key] = ItemOrdering.Move(current, dragged.Item.FullPath, target.Item.FullPath, after);
        _app.SaveSettings();
        Rebuild();
    }

    /// <summary>
    /// Drag &amp; Drop zwischen Gruppen: in eine eigene Gruppe schieben, zurück in Ordner/„Eigene Einträge“
    /// (= aus der Gruppe nehmen) oder auf „Favoriten“ (= als Favorit markieren).
    /// </summary>
    public void MoveToGroup(ItemViewModel dragged, GroupViewModel target, ItemViewModel? near = null, bool after = false) =>
        MoveKeyToGroup(dragged.Item.FullPath, target, near, after);

    /// <summary>Wie <see cref="MoveToGroup"/>, aber über den Schlüssel (z. B. aus der Einstellungsliste gezogen).</summary>
    public void MoveKeyToGroup(string path, GroupViewModel target, ItemViewModel? near = null, bool after = false)
    {
        if (target.Kind == "fav")
        {
            if (!_app.IsFavorite(path) && _app.FindItem(path) is { } item) _app.ToggleFavorite(item);
            return;
        }
        if (!target.AcceptsItems) return;
        var current = GroupManager.GroupOf(_app.Settings, path);
        var wanted = target.GroupName ?? string.Empty;
        if (current.Equals(wanted, StringComparison.CurrentCultureIgnoreCase) && near == null) return;
        _app.AssignGroup(path, wanted, target.Key, near?.Item.FullPath, after, target.Items.Select(i => i.Item.FullPath).ToList());
    }

    /// <summary>Eintrag aus der Einstellungsliste außerhalb einer Gruppe abgelegt: unter „Eigene Einträge“ (ohne Gruppe).</summary>
    public void DropEntryWithoutGroup(string key)
    {
        if (GroupManager.GroupOf(_app.Settings, key).Length > 0) _app.AssignGroup(key, null);
    }

    /// <summary>Aus dem Explorer/Browser auf eine Gruppe (oder ins Panel) gezogen.</summary>
    public void DropExternal(IReadOnlyList<DroppedTarget> targets, GroupViewModel? group) =>
        _app.AddDropped(targets, group?.GroupName);

    public ICommand NewGroupCommand => new RelayCommand(() => _app.CreateGroup());

    /// <summary>Drag &amp; Drop einer Gruppe auf eine andere.</summary>
    public void MoveGroup(GroupViewModel dragged, GroupViewModel target, GroupDropPosition position)
    {
        if (dragged.GroupPath == null || target.GroupPath == null || ReferenceEquals(dragged, target)) return;
        _app.MoveGroupTo(dragged.GroupPath, target.GroupPath, position);
    }

    /// <summary>Eigene Reihenfolge einer Gruppe verwerfen (wieder alphabetisch).</summary>
    public void ResetOrder(GroupViewModel group)
    {
        if (_app.Settings.CustomOrder.Remove(group.Key))
        {
            _app.SaveSettings();
            Rebuild();
        }
    }

    /// <summary>Sichtbare Einträge in Anzeigereihenfolge (eingeklappte Gruppen samt Untergruppen ausgenommen).</summary>
    public IEnumerable<ItemViewModel> VisibleItems => Groups.SelectMany(VisibleIn);

    private static IEnumerable<ItemViewModel> VisibleIn(GroupViewModel g) =>
        g.IsExpanded ? g.Items.Concat(g.Children.SelectMany(VisibleIn)) : Enumerable.Empty<ItemViewModel>();

    public ItemViewModel? SelectedItem => AllItems.FirstOrDefault(i => i.IsSelected);
    public bool HasSelection => SelectedItem != null;

    public void Select(ItemViewModel item)
    {
        bool wasSelected = item.IsSelected;
        foreach (var i in AllItems) i.IsSelected = false;
        item.IsSelected = !wasSelected;
        _selectedKey = item.IsSelected ? item.Key : null;
        OnPropertyChanged(nameof(SelectedItem)); OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>Pfeiltasten im Suchfeld: Auswahl verschieben.</summary>
    public ItemViewModel? MoveSelection(int delta)
    {
        var list = VisibleItems.ToList();
        if (list.Count == 0) return null;
        var idx = list.FindIndex(i => i.IsSelected);
        var next = idx < 0 ? (delta > 0 ? 0 : list.Count - 1) : Math.Clamp(idx + delta, 0, list.Count - 1);
        foreach (var i in AllItems) i.IsSelected = false;
        list[next].IsSelected = true;
        _selectedKey = list[next].Key;
        OnPropertyChanged(nameof(SelectedItem)); OnPropertyChanged(nameof(HasSelection));
        return list[next];
    }

    /// <summary>Enter im Suchfeld: ausgewählten oder ersten Treffer ausführen.</summary>
    public void RunSelectedOrFirst()
    {
        var item = SelectedItem ?? VisibleItems.FirstOrDefault();
        item?.RunCommand.Execute(null);
    }

    /// <summary>Beim Schließen: Suche leeren und den voreingestellten Filter wiederherstellen.</summary>
    public void ResetForNextOpen()
    {
        _searchText = string.Empty;
        _filter = _host.Config.DefaultFilter;
        _selectedKey = null;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(Filter));
        Rebuild();
    }

    public void ClearSelection()
    {
        foreach (var i in AllItems) i.IsSelected = false;
        _selectedKey = null;
        OnPropertyChanged(nameof(SelectedItem)); OnPropertyChanged(nameof(HasSelection));
    }

    internal void OnGroupExpandedChanged(GroupViewModel group)
    {
        if (IsSearching) return; // Während der Suche nichts dauerhaft speichern
        var list = _app.Settings.CollapsedGroups;
        list.RemoveAll(k => k.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
        if (!group.IsExpanded) list.Add(group.Key);
        _app.SaveSettings();
    }

    internal void CollapseAllExcept(GroupViewModel keep)
    {
        // Die Gruppe selbst, ihre Vorfahren und ihre Untergruppen bleiben offen
        var keepPath = keep.GroupPath;
        foreach (var g in AllGroups)
            g.SetExpandedSilently(g == keep || (keepPath != null && g.GroupPath != null &&
                (GroupManager.IsSameOrBelow(keepPath, g.GroupPath) || GroupManager.IsSameOrBelow(g.GroupPath, keepPath))));
        PersistAll();
    }

    internal void SetAllExpanded(bool expanded)
    {
        foreach (var g in AllGroups) g.SetExpandedSilently(expanded);
        PersistAll();
    }

    private void PersistAll()
    {
        if (IsSearching) return;
        _app.Settings.CollapsedGroups = AllGroups.Where(g => !g.IsExpanded).Select(g => g.Key).ToList();
        _app.SaveSettings();
    }

    public void RefreshFavorites() => Rebuild();
}
