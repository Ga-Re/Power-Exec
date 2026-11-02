using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

public sealed class PanelViewModel : ObservableObject
{
    private readonly AppController _app;
    private string _searchText = string.Empty;
    private TypeFilter _filter = TypeFilter.All;
    private string? _selectedKey;
    private string _footer = string.Empty;
    private string _updateTitle = string.Empty;
    private string _updateSubtitle = string.Empty;
    private bool _updateVisible;
    private bool _updateBusy;
    private string? _folderWarning;

    public PanelViewModel(AppController app)
    {
        _app = app;
        _filter = app.Settings.DefaultFilter;
        SettingsCommand = new RelayCommand(() => _app.ShowSettings());
        RescanCommand = new RelayCommand(() => _ = _app.Repository.ScanAsync());
        UpdateNowCommand = new RelayCommand(() => _ = _app.InstallUpdateAsync(), () => !UpdateBusy);
        UpdateLaterCommand = new RelayCommand(() => UpdateVisible = false);
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

    public bool IsPinned
    {
        get => _app.Settings.Pinned;
        set
        {
            if (_app.Settings.Pinned == value) return;
            _app.Settings.Pinned = value;
            _app.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string HotkeyHint => HotkeyService.Display(_app.Settings.Hotkey);
    public bool HasHotkey => !string.IsNullOrWhiteSpace(_app.Settings.Hotkey);

    public string Footer { get => _footer; private set => Set(ref _footer, value); }
    public string? FolderWarning { get => _folderWarning; private set => Set(ref _folderWarning, value); }
    public bool HasNoFolders => _app.Settings.Folders.Count == 0;
    public bool IsEmpty => Groups.Count == 0 && !HasNoFolders;

    // Update-Hinweis
    public bool UpdateVisible { get => _updateVisible; set => Set(ref _updateVisible, value); }
    public string UpdateTitle { get => _updateTitle; set => Set(ref _updateTitle, value); }
    public string UpdateSubtitle { get => _updateSubtitle; set => Set(ref _updateSubtitle, value); }
    public bool UpdateBusy { get => _updateBusy; set => Set(ref _updateBusy, value); }

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
        var query = _searchText.Trim();
        bool searching = query.Length > 0;
        var collapsed = new HashSet<string>(settings.CollapsedGroups, StringComparer.OrdinalIgnoreCase);

        // Nur sichtbare Einträge (Haken in den Ordner-Einstellungen, neue Dateien ggf. erst nach Freigabe)
        var folderByPath = settings.Folders.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var pending = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var visible = new List<LaunchItem>();
        foreach (var i in repo.Items)
        {
            if (!folderByPath.TryGetValue(i.RootFolder, out var folder)) continue;
            var state = FolderVisibility.State(folder, FolderVisibility.RelativePath(folder, i.FullPath));
            if (state == EntryVisibility.Visible) visible.Add(i);
            else if (state == EntryVisibility.Pending) pending[folder.Path] = pending.GetValueOrDefault(folder.Path) + 1;
        }
        UpdatePending(pending);

        bool Matches(LaunchItem i)
        {
            if (ItemFilter.MatchesQuery(i, query)) return true;
            var e = settings.FindEntry(i.FullPath);
            return e != null && query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(t => (e.DisplayName + " " + e.Description).Contains(t, StringComparison.CurrentCultureIgnoreCase));
        }

        var pool = visible
            .Where(i => ItemFilter.MatchesType(i, _filter, _app.IsFavorite(i.FullPath)) && (query.Length == 0 || Matches(i)))
            .ToList();

        IEnumerable<LaunchItem> Order(IEnumerable<LaunchItem> items) => searching
            ? items.OrderBy(i => ItemFilter.Rank(i, query)).ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            : items.OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase);

        var groups = new List<GroupViewModel>();
        if (!searching && _filter == TypeFilter.All)
        {
            var favs = Order(pool.Where(i => _app.IsFavorite(i.FullPath))).ToList();
            if (favs.Count > 0)
            {
                const string key = "fav";
                groups.Add(new GroupViewModel(this, _app, key, "Favoriten", true, null,
                    favs.Select(i => new ItemViewModel(_app, this, i, key)).ToList(), !collapsed.Contains(key), null));
            }
        }

        foreach (var folder in settings.Folders)
        {
            var status = repo.FolderStatuses.FirstOrDefault(s => string.Equals(s.Path, folder.Path, StringComparison.OrdinalIgnoreCase));
            var items = Order(pool.Where(i => string.Equals(i.RootFolder, folder.Path, StringComparison.OrdinalIgnoreCase))).ToList();
            if (items.Count == 0 && (searching || _filter != TypeFilter.All || status?.Error == null)) continue;
            var key = "folder:" + folder.Path;
            groups.Add(new GroupViewModel(this, _app, key, folder.Path, false, folder.Path,
                items.Select(i => new ItemViewModel(_app, this, i, key)).ToList(),
                searching || !collapsed.Contains(key), status?.Error));
        }

        Groups.Clear();
        foreach (var g in groups) Groups.Add(g);

        // Auswahl wiederherstellen
        var selected = AllItems.FirstOrDefault(i => i.Key == _selectedKey);
        if (selected != null) selected.IsSelected = true;
        else _selectedKey = null;

        var errors = repo.FolderStatuses.Where(s => s.Error != null).ToList();
        FolderWarning = errors.Count == 0 ? null
            : errors.Count == 1 ? $"{errors[0].Path}: {errors[0].Error}"
            : $"{errors.Count} Ordner nicht erreichbar";

        if (repo.IsScanning && repo.Items.Count == 0) Footer = "Ordner werden eingelesen …";
        else if (searching) Footer = $"{pool.Count} Treffer für „{query}“";
        else Footer = $"{visible.Count} Einträge aus {settings.Folders.Count} Ordnern · eingelesen {repo.LastScan:HH:mm}";

        OnPropertyChanged(nameof(HasNoFolders));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HotkeyHint));
        OnPropertyChanged(nameof(HasHotkey));
        OnPropertyChanged(nameof(IsPinned));
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

    public IEnumerable<ItemViewModel> AllItems => Groups.SelectMany(g => g.Items);

    public IEnumerable<ItemViewModel> VisibleItems => Groups.Where(g => g.IsExpanded).SelectMany(g => g.Items);

    public ItemViewModel? SelectedItem => AllItems.FirstOrDefault(i => i.IsSelected);

    public void Select(ItemViewModel item)
    {
        bool wasSelected = item.IsSelected;
        foreach (var i in AllItems) i.IsSelected = false;
        item.IsSelected = !wasSelected;
        _selectedKey = item.IsSelected ? item.Key : null;
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
        _filter = _app.Settings.DefaultFilter;
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
        foreach (var g in Groups) g.SetExpandedSilently(g == keep);
        PersistAll();
    }

    internal void SetAllExpanded(bool expanded)
    {
        foreach (var g in Groups) g.SetExpandedSilently(expanded);
        PersistAll();
    }

    private void PersistAll()
    {
        if (IsSearching) return;
        _app.Settings.CollapsedGroups = Groups.Where(g => !g.IsExpanded).Select(g => g.Key).ToList();
        _app.SaveSettings();
    }

    public void RefreshFavorites() => Rebuild();
}
