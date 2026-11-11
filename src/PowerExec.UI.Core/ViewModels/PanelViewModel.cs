using PowerExec.Core.Localization;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Collections;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;
using PowerExec.Core.Profiles;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;

public sealed class PanelViewModel : ObservableObject
{
    private readonly IPanelHost _app;
    private readonly IPanelContext _host;
    private string _searchText = string.Empty;
    private TypeFilter _filter = TypeFilter.All;
    private string? _selectedKey;
    private string _footer = string.Empty;
    private bool _updateDismissed;
    private string? _folderWarning;

    public PanelViewModel(IPanelHost app, IPanelContext host)
    {
        _app = app;
        _host = host;
        _filter = TypeFilter.All; // „Vorausgewählter Filter“ gibt es nicht mehr – Filter kommen aus dem Suchfeld
        SetLayoutCommand = new RelayCommand(p => { if (Enum.TryParse<ItemLayout>(p?.ToString(), out var l)) Layout = l; });
        SwitchWorkspaceCommand = new RelayCommand(p => { if (p is string name) _app.SwitchWorkspace(name); });
        SettingsCommand = new RelayCommand(() => _app.ShowSettings());
        RescanCommand = new RelayCommand(() => _ = _app.Repository.ScanAsync());
        UpdateNowCommand = new RelayCommand(() => _ = _app.InstallUpdateAsync(), () => !UpdateBusy);
        UpdateLaterCommand = new RelayCommand(() => { _updateDismissed = true; OnPropertyChanged(nameof(UpdateVisible)); });
        _app.Update.PropertyChanged += OnUpdateChanged;
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        AddFolderCommand = new RelayCommand(() => _app.ShowSettings(section: "groups"));
        _app.HistoryChanged += OnHistoryChanged;
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
                Rebuild(reuse: true);
            }
        }
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(_searchText);

    public TypeFilter Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value)) Rebuild(reuse: true);
        }
    }

    public PanelConfig Config => _host.Config;

    /// <summary>Dunkle Panelfläche (Design bzw. eigene Panelfarbe) – für lesbare Gruppenfarben.</summary>
    public bool IsDarkSurface => !_app.Theme.IsHighContrast && PanelColors.TryParse(_host.Config.PanelColor, out var c)
        ? !PanelColors.IsLight(c)
        : _app.Theme.IsDark;
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

    /// <summary>Stand der Darstellung bei der letzten Meldung (Liste/Raster, Elementgröße).</summary>
    private string? _raisedLayoutKey;
    private string LayoutKey => $"{Layout}|{ItemSize}|{IsGrid}|{IsCompact}|{IsLarge}";

    /// <summary>
    /// Nur melden, wenn sich die Darstellung geändert hat: <c>IsGrid</c> steuert über einen DataTrigger ItemTemplate und
    /// ItemsPanel jeder Gruppe – jede Meldung lässt WPF alle Eintrags-Container neu erzeugen (bis 0.16.2 bei jedem Tastendruck).
    /// </summary>
    private void RaiseLayoutIfChanged()
    {
        if (LayoutKey != _raisedLayoutKey) RaiseLayout();
    }

    private void RaiseLayout()
    {
        _raisedLayoutKey = LayoutKey;
        foreach (var p in new[] { nameof(Layout), nameof(IsCompact), nameof(IsLarge), nameof(ItemSize), nameof(ItemSizeText), nameof(IsGrid), nameof(IsList), nameof(LayoutGlyph), nameof(LayoutToolTip) }) OnPropertyChanged(p);
        GroupLayoutsChanged();
    }

    /// <summary>Darstellung einer oder aller Gruppen geändert (Liste/Raster): Gruppen und Detailbereich nachziehen.</summary>
    internal void GroupLayoutsChanged()
    {
        foreach (var g in AllGroups) g.RefreshLayout();
        OnPropertyChanged(nameof(ShowTileDetails));
    }

    /// <summary>Elementgröße: Klein (Liste einzeilig, kleine Kacheln), Mittel, Groß.</summary>
    public ItemSize ItemSize
    {
        get => _host.Config.ItemSize;
        set
        {
            if (_host.Config.ItemSize == value) return;
            _host.Config.ItemSize = value;
            _app.SaveSettings();
            RaiseLayout();
        }
    }

    public bool IsCompact => ItemSize == ItemSize.Small;
    public bool IsLarge => ItemSize == ItemSize.Large;
    public string ItemSizeText => ItemSize switch { ItemSize.Small => L.T("Small"), ItemSize.Large => L.T("Large"), _ => L.T("Medium") };
    public ICommand SetItemSizeCommand => new RelayCommand(p => { if (Enum.TryParse<ItemSize>(p?.ToString(), out var size)) ItemSize = size; });

    public bool IsGrid
    {
        get => Layout == ItemLayout.Grid;
        set => Layout = value ? ItemLayout.Grid : ItemLayout.List;
    }

    public bool IsList => !IsGrid;

    /// <summary>Symbol des Umschalters Liste/Raster im Panel-Kopf (zeigt die aktuelle Darstellung).</summary>
    public string LayoutGlyph => IsGrid ? "\uE8A9" : "\uE8FD";
    public string LayoutToolTip => IsGrid ? L.T("Grid – click for list") : L.T("List – click for grid");

    public ICommand SetLayoutCommand { get; }
    public ICommand ToggleGridCommand => new RelayCommand(() => IsGrid = !IsGrid);

    // Rechtsklick auf freie Fläche
    public ICommand NewEntryCommand => new RelayCommand(p => _app.NewCustomEntry(p?.ToString(), null));
    public ICommand AddFromCatalogCommand => new RelayCommand(() => _app.AddFromCatalog(null));
    public ICommand AddFromClipboardCommand => new RelayCommand(() => _app.AddFromClipboard(SelectedItem is { } s ? GroupManager.GroupOf(_app.Settings, s.Item.FullPath) is { Length: > 0 } g ? g : null : null));
    public ICommand ImportEntriesCommand => new RelayCommand(() => _app.ImportEntries());
    public ICommand AddFilesCommand => new RelayCommand(() => _app.AddFilesDialog(null));

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

    public string HotkeyHint => DisplayText.Hotkey(_host.Config.Hotkey);
    public bool HasHotkey => !string.IsNullOrWhiteSpace(_host.Config.Hotkey);

    public string Footer { get => _footer; private set => Set(ref _footer, value); }

    /// <summary>Laufende und installierte Version (z. B. „Version 0.20.0 · installiert“).</summary>
    public string VersionText => _app.VersionText;
    public string? FolderWarning { get => _folderWarning; private set => Set(ref _folderWarning, value); }
    public bool HasNoFolders => _app.Settings.Folders.Count == 0 && _app.Settings.CustomEntries.Count == 0 && GroupManager.All(_app.Settings).Count == 0;
    public string EmptyHint => L.T("No groups yet. Create a group – with a folder it shows its scripts and programs – or add custom entries.");
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
    public void Detach()
    {
        _app.Update.PropertyChanged -= OnUpdateChanged;
        _app.HistoryChanged -= OnHistoryChanged;
    }

    public ICommand SettingsCommand { get; }
    public ICommand RescanCommand { get; }
    public ICommand UpdateNowCommand { get; }
    public ICommand UpdateLaterCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand AddFolderCommand { get; }


    // ---- Vorschläge beim Tippen (#, typ:, gruppe:, ist:) -----------------------------

    private SuggestionSource? _suggestionSource;
    private IReadOnlyList<SearchSuggestion> _suggestions = Array.Empty<SearchSuggestion>();
    private int _suggestionIndex;

    public IReadOnlyList<SearchSuggestion> Suggestions
    {
        get => _suggestions;
        private set { if (Set(ref _suggestions, value)) { OnPropertyChanged(nameof(HasSuggestions)); SuggestionIndex = 0; } }
    }
    public bool HasSuggestions => _suggestions.Count > 0;

    /// <summary>Ausgewählter Vorschlag (Pfeiltasten im Operator-Modus).</summary>
    public int SuggestionIndex
    {
        get => _suggestionIndex;
        set => Set(ref _suggestionIndex, _suggestions.Count == 0 ? 0 : Math.Clamp(value, 0, _suggestions.Count - 1));
    }
    public SearchSuggestion? SelectedSuggestion => _suggestions.Count == 0 ? null : _suggestions[Math.Clamp(_suggestionIndex, 0, _suggestions.Count - 1)];

    /// <summary>
    /// Das aktuelle Wort ist eindeutig ein Filter („#…“, „typ:…“): dann steuern ↑ ↓ und Enter die Vorschläge.
    /// Bei bloßen Schlüssel-Vorschlägen („ty“ → typ:) übernimmt nur Tab, damit normales Suchen nicht gestört wird.
    /// </summary>
    public bool SuggestionsTakeKeys { get; private set; }

    /// <summary>Was die sichtbaren Einträge hergeben (Schlagworte, Arten, Gruppen, Merkmale) – nur das wird vorgeschlagen.</summary>
    private SuggestionSource BuildSuggestionSource() => WithLookup(BuildSuggestionSourceCore);

    private SuggestionSource BuildSuggestionSourceCore()
    {
        var src = new SuggestionSource();
        var settings = _app.Settings;
        foreach (var i in CollectVisible())
        {
            foreach (var t in TagsOf(i.FullPath)) src.Tags[t] = src.Tags.GetValueOrDefault(t) + 1;
            var kind = SearchQuery.KindOf(i);
            src.Kinds[kind] = src.Kinds.GetValueOrDefault(kind) + 1;
            var g = GroupOfItem(i);
            if (g.Length > 0)
                for (var p = g; p != null; p = GroupManager.ParentOf(p)) src.Groups[p] = src.Groups.GetValueOrDefault(p) + 1;
            if (IsFav(i.FullPath)) src.Favorites++;
            if (i.RequiresAdmin) src.Admin++;
            if (i.SupportsRemote) src.Remote++;
            if (i.ParseError != null || i.IsBlocked || (i.Source != null && _app.Health?.Get(i.Source.Key) is { Ok: false })) src.Problems++;
        }
        return src;
    }

    /// <summary>Vorschläge zum Wort vor dem Cursor neu bestimmen.</summary>
    public void UpdateSuggestions(string text, int caret)
    {
        var (_, token) = SearchSuggestions.CurrentToken(text, caret);
        if (token.Length == 0) { Suggestions = Array.Empty<SearchSuggestion>(); return; }
        _suggestionSource ??= BuildSuggestionSource();
        var list = SearchSuggestions.For(text, caret, _suggestionSource);
        // Schon vollständig getippt und eindeutig: nicht mehr anbieten
        if (list.Count == 1 && list[0].Insert.Equals(token, StringComparison.CurrentCultureIgnoreCase)) list = Array.Empty<SearchSuggestion>();
        SuggestionsTakeKeys = token.StartsWith('#') || token.Contains(':');
        Suggestions = list;
    }

    /// <summary>
    /// Klick ins Suchfeld: steht der Cursor nicht in einem Wort, die möglichen Filterarten anbieten
    /// (Schlagwort, Typ, Gruppe, Merkmal); die Auswahl zeigt danach deren Werte.
    /// </summary>
    public void ShowFilterCategories(string text, int caret)
    {
        var (_, token) = SearchSuggestions.CurrentToken(text, caret);
        if (token.Length > 0) { UpdateSuggestions(text, caret); return; }
        _suggestionSource ??= BuildSuggestionSource();
        SuggestionsTakeKeys = true;
        Suggestions = SearchSuggestions.Categories(_suggestionSource);
    }

    public void CloseSuggestions() => Suggestions = Array.Empty<SearchSuggestion>();

    // ---- Suche als Bausteine (Panel und Spotlight-Modus) ---------------------------

    /// <summary>Gruppe eines Eintrags: ausdrücklich zugeordnet, sonst smarte Gruppe seines Ordners (ggf. mit Unterordner).</summary>
    public string GroupOfItem(LaunchItem i) =>
        _lookup?.EffectiveGroup(i.FullPath, i.RootFolder) ?? SmartGroups.EffectiveGroup(_app.Settings, i.FullPath, i.RootFolder);

    // ---- Nachschlagen: während Rebuild, Spotlight und Suchvorschlägen über eine Momentaufnahme (O(1)) ----------

    private SettingsLookup? _lookup;

    /// <summary>Führt <paramref name="work"/> mit einer frischen Momentaufnahme der Einstellungen aus.</summary>
    private T WithLookup<T>(Func<T> work)
    {
        var previous = _lookup;
        _lookup = new SettingsLookup(_app.Settings);
        try { return work(); }
        finally { _lookup = previous; }
    }

    private bool IsFav(string path) => _lookup?.IsFavorite(path) ?? _app.IsFavorite(path);
    private IReadOnlyList<string> TagsOf(string path) => _lookup?.TagsOf(path) ?? Tags.Of(_app.Settings, path);
    private EntryConfig? EntryOf(string path) => _lookup != null ? _lookup.Entry(path) : _app.Settings.FindEntry(path);
    private double FrecencyOf(string path) => _lookup?.Frecency(path) ?? RunStats.Frecency(_app.Settings, path);
    private bool ShowsGroup(string groupPath) => _lookup?.ShowsGroup(groupPath) ?? WorkspaceManager.ShowsGroup(_app.Settings, groupPath);

    /// <summary>
    /// Alle Einträge, die das Panel zeigt: Haken in den Ordner-Einstellungen, freigegebene neue Dateien,
    /// eigene Einträge, im Arbeitsbereich eingeblendete Gruppen.
    /// </summary>
    public List<LaunchItem> CollectVisible(Dictionary<string, int>? pending = null)
    {
        var settings = _app.Settings;
        var folderByPath = settings.Folders.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var visible = new List<LaunchItem>();
        bool CategoryOk(LaunchItem i) => ShowsGroup(GroupOfItem(i));
        foreach (var i in _app.Repository.Items)
        {
            if (!folderByPath.TryGetValue(i.RootFolder, out var folder)) continue;
            var state = FolderVisibility.State(folder, FolderVisibility.RelativePath(folder, i.FullPath));
            if (state == EntryVisibility.Visible) { if (CategoryOk(i)) visible.Add(i); }
            else if (state == EntryVisibility.Pending && pending != null) pending[folder.Path] = pending.GetValueOrDefault(folder.Path) + 1;
        }
        // Eigene Einträge ohne Datei (Webseiten, Ordner, RDP, Befehle)
        var seen = new HashSet<string>(visible.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
        foreach (var c in settings.CustomEntries)
        {
            // Snippets haben kein Ziel, nur Code
            if (c.Kind == CustomEntryKind.Workflow ? c.Steps.Count == 0 : c.Target.Trim().Length == 0 && (c.Kind != CustomEntryKind.Snippet || c.Code.Trim().Length == 0)) continue;
            var li = _app.DescribeCustom(c);
            if (!seen.Add(li.FullPath)) continue; // Datei liegt bereits in einem durchsuchten Ordner
            if (CategoryOk(li)) visible.Add(li);
        }
        return visible;
    }

    /// <summary>Bewertung: Name zählt am meisten, dann Dateiname und Schlagworte, dann Beschreibung, Notiz und Code; null = kein Treffer.</summary>
    public int? ScoreItem(LaunchItem i, SearchQuery query)
    {
        var e = EntryOf(i.FullPath);
        var fields = new List<(string?, int)>
        {
            (i.DisplayName, 3), (e?.DisplayName, 3), (i.FileName, 2), (e == null ? null : string.Join(' ', e.Tags), 2),
            (i.Synopsis, 1), (i.Description, 1), (e?.Description, 1), (e?.Notes, 1), (e?.Category, 1), (i.Source?.Code, 1)
        };
        return SearchScoring.Score(fields, query.Terms);
    }

    public bool FilterItem(LaunchItem i, SearchQuery query, TypeFilter filter)
    {
        var fav = IsFav(i.FullPath);
        return ItemFilter.MatchesType(i, filter, fav) &&
               query.MatchesFilters(i, GroupOfItem(i), fav, TagsOf(i.FullPath),
                   () => i.Source != null && _app.Health?.Get(i.Source.Key) is { Ok: false });
    }

    /// <summary>
    /// Flache Trefferliste (Spotlight-Modus): beste Treffer nach Bewertung und Häufigkeit;
    /// ohne Suchtext die meistgenutzten Einträge, dann Favoriten.
    /// </summary>
    public IReadOnlyList<LaunchItem> SearchFlat(string text, int max) => WithLookup(() => SearchFlatCore(text, max));

    private IReadOnlyList<LaunchItem> SearchFlatCore(string text, int max)
    {
        var query = SearchQuery.Parse(text);
        var settings = _app.Settings;
        var scored = new List<(LaunchItem Item, double Score)>();
        foreach (var i in CollectVisible())
        {
            if (!FilterItem(i, query, TypeFilter.All)) continue;
            var frecency = FrecencyOf(i.FullPath);
            if (query.HasTerms)
            {
                if (ScoreItem(i, query) is not { } score) continue;
                scored.Add((i, score + SearchScoring.FrecencyBonus(frecency)));
            }
            else
            {
                var fav = IsFav(i.FullPath);
                if (frecency <= 0 && !fav && !query.HasFilters) continue;
                scored.Add((i, frecency * 10 + (fav ? 5 : 0)));
            }
        }
        return scored.OrderByDescending(x => x.Score).ThenBy(x => x.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Take(max).Select(x => x.Item).ToList();
    }

    // ---- Tastatur ------------------------------------------------------------

    private List<LaunchItem> _quickItems = new();
    private List<LaunchItem> _favoriteItems = new();

    /// <summary>Favoriten 1 … 9 in Panel-Reihenfolge (Alt+1 … 9).</summary>
    public IReadOnlyList<LaunchItem> QuickItems => _quickItems;

    /// <summary>Favoriten fürs Mini-Dock (Anzahl einstellbar).</summary>
    public IReadOnlyList<LaunchItem> DockItems => _favoriteItems.Take(_host.Config.DockCount).ToList();

    /// <summary>Die Favoriten haben sich geändert (Mini-Dock neu aufbauen).</summary>
    public event Action? QuickItemsChanged;

    /// <summary>Alt+1 … 9: n-ten Favoriten mit seinem Standardprofil starten.</summary>
    public bool RunQuickItem(int number)
    {
        if (number < 1 || number > _quickItems.Count) return false;
        var item = _quickItems[number - 1];
        _app.RunProfile(item, null); // Standardprofil bzw. „Nach Profil fragen“
        return true;
    }

    /// <summary>Ziel der Tastenkürzel: ausgewählter Eintrag, sonst der erste sichtbare.</summary>
    public ItemViewModel? CurrentItem => SelectedItem ?? VisibleItems.FirstOrDefault();

    private bool _showShortcuts;
    /// <summary>Spickzettel mit allen Tastenkürzeln (F1 oder „?“).</summary>
    public bool ShowShortcuts { get => _showShortcuts; set => Set(ref _showShortcuts, value); }
    public ICommand ToggleShortcutsCommand => _toggleShortcutsCommand ??= new RelayCommand(() => ShowShortcuts = !ShowShortcuts);
    private ICommand? _toggleShortcutsCommand;

    /// <summary>Einträge des Spickzettels (Taste, Wirkung).</summary>
    public IReadOnlyList<ShortcutInfo> Shortcuts { get; } = new ShortcutInfo[]
    {
        new("↑ ↓", L.T("Select entry")),
        new("Enter", L.T("Run (default profile)")),
        new(L.T("Ctrl+Enter"), L.T("Run as administrator")),
        new(L.T("Ctrl+Shift+Enter"), L.T("Run as different user")),
        new(L.T("Ctrl+R"), L.T("Run remotely …")),
        new("→ / Tab", L.T("All actions of the entry")),
        new("Alt+1 … 9", L.T("Run favorite 1 … 9")),
        new(L.T("Ctrl+D"), L.T("Favorite on/off")),
        new("F2", L.T("Edit entry / launch profiles")),
        new(L.T("Space"), L.T("Preview: code, parameters, signature")),
        new(L.T("Ctrl+E"), L.T("Open in editor")),
        new(L.T("Ctrl+Shift+C"), L.T("Copy path")),
        new(L.T("Ctrl+H"), L.T("History: run again, failed only")),
        new(L.T("Ctrl+click"), L.T("Select several: run one after another, save as workflow")),
        new(L.T("Ctrl+Z"), L.T("Undo last change")),
        new(L.T("Ctrl+V"), L.T("Entry from clipboard")),
        new("Esc", L.T("Clear search / close panel")),
        new(L.T("F1 or ?"), L.T("This overview")),
    };

    // ---- Navigation: fixierter Gruppenkopf, Sprungleiste, Strg+K ------------------

    private GroupViewModel? _stickyGroup;
    /// <summary>Gruppe, deren Kopf oben aus dem sichtbaren Bereich gescrollt ist (wird oben fixiert angezeigt).</summary>
    public GroupViewModel? StickyGroup
    {
        get => _stickyGroup;
        set { if (Set(ref _stickyGroup, value)) { OnPropertyChanged(nameof(HasStickyGroup)); OnPropertyChanged(nameof(StickyTopKey)); } }
    }
    public bool HasStickyGroup => _stickyGroup != null;
    /// <summary>Oberste Gruppe zur fixierten Gruppe (für die Markierung in der Sprungleiste).</summary>
    public string? StickyTopKey => _stickyGroup == null ? null
        : Groups.FirstOrDefault(g => g.SelfAndDescendants.Contains(_stickyGroup))?.Key;

    /// <summary>Sprungleiste rechts: ab 2 Gruppen auf oberster Ebene (bei einer gäbe es kein Ziel), nicht während der Suche.</summary>
    public bool ShowJumpRail => _host.Config.ShowJumpRail && Groups.Count >= 2 && !IsSearching;

    /// <summary>Gruppen als Fluent-Karten mit Akzentstreifen (sonst getönte Karten in Gruppenfarbe).</summary>
    public bool IsAccentGroups => _host.Config.GroupStyle == GroupCardStyle.Accent;

    /// <summary>Scrollbalken der Liste anzeigen (ausgeblendet scrollt das Mausrad weiter).</summary>
    /// <summary>Scrollbalken der Liste anzeigen (sonst nur per Mausrad/Tastatur scrollen).</summary>
    public bool ShowListScrollBar => _host.Config.ShowScrollBar;

    private string? _lastRailKey;

    /// <summary>
    /// Klick auf einen Punkt der Sprungleiste: Gruppe aufklappen und hinspringen;
    /// ein zweiter Klick auf denselben Punkt klappt sie wieder zu.
    /// </summary>
    public ICommand RailClickCommand => _railClickCommand ??= new RelayCommand(p =>
    {
        if (p is not GroupViewModel g) return;
        if (g.IsExpanded && _lastRailKey == g.Key)
        {
            g.IsExpanded = false;
            _lastRailKey = null;
            ScrollToGroupRequested?.Invoke(g);
            return;
        }
        _lastRailKey = g.Key;
        JumpTo(g);
    });
    private ICommand? _railClickCommand;

    /// <summary>Wird ausgelöst, wenn die Ansicht zu einer Gruppe scrollen soll.</summary>
    public event Action<GroupViewModel>? ScrollToGroupRequested;

    public ICommand JumpToGroupCommand => _jumpToGroupCommand ??= new RelayCommand(p => { if (p is GroupViewModel g) JumpTo(g); });
    private ICommand? _jumpToGroupCommand;

    /// <summary>Gruppe samt übergeordneten Gruppen aufklappen und hinscrollen.</summary>
    public void JumpTo(GroupViewModel group)
    {
        for (var parent = group.GroupPath == null ? null : GroupManager.ParentOf(group.GroupPath); parent != null; parent = GroupManager.ParentOf(parent))
            if (AllGroups.FirstOrDefault(g => parent.Equals(g.GroupPath, StringComparison.CurrentCultureIgnoreCase)) is { IsExpanded: false } p)
                p.IsExpanded = true;
        if (!group.IsExpanded) group.IsExpanded = true;
        ShowGroupJump = false;
        ScrollToGroupRequested?.Invoke(group);
    }

    private bool _showGroupJump;
    /// <summary>Strg+K: Gruppe per Suche anspringen.</summary>
    public bool ShowGroupJump
    {
        get => _showGroupJump;
        set
        {
            if (!Set(ref _showGroupJump, value)) return;
            if (value) { _groupJumpText = string.Empty; OnPropertyChanged(nameof(GroupJumpText)); UpdateGroupJump(); }
        }
    }

    private string _groupJumpText = string.Empty;
    public string GroupJumpText
    {
        get => _groupJumpText;
        set { if (Set(ref _groupJumpText, value ?? string.Empty)) UpdateGroupJump(); }
    }

    private IReadOnlyList<GroupJumpItem> _groupJumpResults = Array.Empty<GroupJumpItem>();
    public IReadOnlyList<GroupJumpItem> GroupJumpResults { get => _groupJumpResults; private set => Set(ref _groupJumpResults, value); }

    private GroupJumpItem? _groupJumpSelected;
    public GroupJumpItem? GroupJumpSelected { get => _groupJumpSelected; set => Set(ref _groupJumpSelected, value); }

    private void UpdateGroupJump()
    {
        var terms = _groupJumpText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var list = AllGroups
            .Select(g => (Group: g, Score: terms.Length == 0 ? 0 : SearchScoring.Score(new (string?, int)[] { (g.Title, 2), (g.Breadcrumb, 1) }, terms)))
            .Where(x => x.Score != null)
            .OrderByDescending(x => x.Score)
            .Select(x => new GroupJumpItem(x.Group, TextPart.Split(x.Group.Title, terms),
                x.Group.Depth > 0 ? x.Group.Breadcrumb : string.Empty, x.Group.AccentColor, x.Group.CountText))
            .ToList();
        GroupJumpResults = list;
        GroupJumpSelected = list.FirstOrDefault();
    }

    /// <summary>Pfeiltasten in der Gruppensuche.</summary>
    public void MoveGroupJumpSelection(int delta)
    {
        if (_groupJumpResults.Count == 0) return;
        var idx = _groupJumpSelected == null ? -1 : _groupJumpResults.ToList().IndexOf(_groupJumpSelected);
        GroupJumpSelected = _groupJumpResults[Math.Clamp(idx + delta, 0, _groupJumpResults.Count - 1)];
    }

    public void ConfirmGroupJump()
    {
        if (_groupJumpSelected != null) JumpTo(_groupJumpSelected.Group);
        else ShowGroupJump = false;
    }

    // ---- Mehrfachauswahl und Abläufe (Strg+Klick) -----------------------------------

    private readonly List<string> _checked = new();

    /// <summary>Ausgewählte Einträge in Reihenfolge der Auswahl (Strg+Klick).</summary>
    public IReadOnlyList<LaunchItem> CheckedItems => _checked
        .Select(p => AllItems.FirstOrDefault(i => i.Item.FullPath.Equals(p, StringComparison.OrdinalIgnoreCase))?.Item ?? _app.FindItem(p))
        .Where(i => i != null).Cast<LaunchItem>().ToList();
    public bool HasMultiSelection => _checked.Count > 0;
    public string MultiSelectionText => _checked.Count == 1 ? L.T("1 selected") : L.F("{0} selected", _checked.Count);

    public void ToggleChecked(ItemViewModel item)
    {
        var path = item.Item.FullPath;
        var i = _checked.FindIndex(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) _checked.RemoveAt(i);
        else if (item.Item.Source?.Kind != CustomEntryKind.Workflow) _checked.Add(path); // keine verschachtelten Abläufe
        ApplyChecked();
    }

    public void ClearChecked()
    {
        _checked.Clear();
        ApplyChecked();
    }

    private void ApplyChecked()
    {
        foreach (var vm in AllItems) vm.IsChecked = _checked.Contains(vm.Item.FullPath, StringComparer.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(MultiSelectionText));
    }

    public ICommand ClearCheckedCommand => _clearCheckedCommand ??= new RelayCommand(ClearChecked);
    public ICommand RunCheckedCommand => _runCheckedCommand ??= new RelayCommand(() =>
    {
        var items = CheckedItems;
        if (items.Count == 0) return;
        var steps = items.Select(i => new WorkflowStep { Path = i.FullPath, Name = i.DisplayName }).ToList();
        ClearChecked();
        _ = _app.RunWorkflowAsync(L.F("{0} entries one after another", steps.Count), steps, stopOnError: true);
    });
    public ICommand SaveCheckedAsWorkflowCommand => _saveWorkflowCommand ??= new RelayCommand(() =>
    {
        var items = CheckedItems;
        if (items.Count == 0) return;
        var name = _app.Dialogs.Ask(L.T("Save as workflow"),
            L.F("{0} steps: {1}. The workflow appears as its own entry – also in search, favorites and the mini dock.", items.Count, string.Join(" → ", items.Select(i => i.DisplayName))),
            L.T("New workflow"), L.T("Save"), n => n.Trim().Length == 0 ? L.T("Please enter a name.") : null);
        if (name == null) return;
        ClearChecked();
        _app.CreateWorkflow(name, items, GroupOfItem(items[0]) is { Length: > 0 } g && GroupManager.Exists(_app.Settings, g) ? g : null);
    });
    private ICommand? _clearCheckedCommand, _runCheckedCommand, _saveWorkflowCommand;

    // ---- Verlauf (Strg+H) ---------------------------------------------------------

    private bool _showHistory;
    public bool ShowHistory
    {
        get => _showHistory;
        set { if (Set(ref _showHistory, value) && value) RefreshHistory(); }
    }
    public ICommand ToggleHistoryCommand => _toggleHistoryCommand ??= new RelayCommand(() => ShowHistory = !ShowHistory);
    private ICommand? _toggleHistoryCommand;

    private string _historyFilter = "all";
    /// <summary>„all“, „today“, „errors“ oder „remote“.</summary>
    public string HistoryFilter { get => _historyFilter; set { if (Set(ref _historyFilter, value ?? "all")) RefreshHistory(); } }

    private string _historySearch = string.Empty;
    public string HistorySearch { get => _historySearch; set { if (Set(ref _historySearch, value ?? string.Empty)) RefreshHistory(); } }

    private IReadOnlyList<HistoryRowViewModel> _historyRows = Array.Empty<HistoryRowViewModel>();
    public IReadOnlyList<HistoryRowViewModel> HistoryRows { get => _historyRows; private set { if (Set(ref _historyRows, value)) OnPropertyChanged(nameof(HistoryEmpty)); } }
    public bool HistoryEmpty => _historyRows.Count == 0;

    private void OnHistoryChanged() { if (_showHistory) RefreshHistory(); }

    public void RefreshHistory() =>
        HistoryRows = _app.History.Filter(_historyFilter, _historySearch).Take(200).Select(e => new HistoryRowViewModel(_app, e)).ToList();

    // ---- Vorschau (Leertaste) -----------------------------------------------------

    private PeekViewModel? _peek;
    /// <summary>Schnellansicht des ausgewählten Eintrags; null = geschlossen.</summary>
    public PeekViewModel? Peek { get => _peek; private set { if (Set(ref _peek, value)) OnPropertyChanged(nameof(ShowPeek)); } }
    public bool ShowPeek => _peek != null;
    public ICommand ClosePeekCommand => _closePeekCommand ??= new RelayCommand(() => Peek = null);
    private ICommand? _closePeekCommand;

    /// <summary>Vorschau für den ausgewählten (sonst ersten) Eintrag öffnen bzw. schließen.</summary>
    public bool TogglePeek()
    {
        if (_peek != null) { Peek = null; return true; }
        var item = CurrentItem;
        if (item == null) return false;
        if (!item.IsSelected && !item.IsSingleClick) Select(item);
        Peek = new PeekViewModel(_app, item);
        return true;
    }

    /// <summary>Pfeiltasten bei offener Vorschau: zum nächsten Eintrag wechseln.</summary>
    public void MovePeek(int delta)
    {
        var next = MoveSelection(delta);
        if (next != null) Peek = new PeekViewModel(_app, next);
    }

    public void ClosePeek() => Peek = null;

    // ---- Team-Kataloge -------------------------------------------------------------

    /// <summary>Gemeldete, noch nicht übernommene Katalog-Änderungen.</summary>
    public string? CatalogPending => _app.Settings.Catalogs.Where(c => c.PendingSummary.Length > 0)
        .Select(c => L.F("Team catalog “{0}”: {1}", c.Name, c.PendingSummary)).FirstOrDefault();
    public ICommand ApplyCatalogsCommand => _applyCatalogsCommand ??= new RelayCommand(() => _ = _app.ApplyPendingCatalogsAsync());
    private ICommand? _applyCatalogsCommand;
    public void RefreshCatalogs() => OnPropertyChanged(nameof(CatalogPending));

    // ---- Neu in dieser Version -----------------------------------------------------

    public bool WhatsNewVisible => _app.WhatsNew != null;
    public string WhatsNewTitle => _app.WhatsNew is { } n ? L.F("New in {0}: {1}", n.Version.ToString(2), n.Title) : string.Empty;
    /// <summary>Die ersten Neuerungen (alle über „Alle Neuerungen“).</summary>
    public IReadOnlyList<string> WhatsNewItems => _app.WhatsNew?.Highlights.Take(4).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
    public ICommand DismissWhatsNewCommand => _dismissWhatsNewCommand ??= new RelayCommand(() => _app.DismissWhatsNew());
    public ICommand ShowAllWhatsNewCommand => _showAllWhatsNewCommand ??= new RelayCommand(() =>
    {
        if (_app.WhatsNew is { } n)
            _app.Shell.OpenUrl($"https://github.com/{Core.Updates.UpdateChecker.Owner}/{Core.Updates.UpdateChecker.Repo}/releases/tag/v{n.Version.ToString(3)}");
    });
    private ICommand? _dismissWhatsNewCommand, _showAllWhatsNewCommand;

    public void RefreshWhatsNew()
    {
        foreach (var p in new[] { nameof(WhatsNewVisible), nameof(WhatsNewTitle), nameof(WhatsNewItems) }) OnPropertyChanged(p);
    }

    // ---- Aktivitäten -----------------------------------------------------------

    /// <summary>Laufende und gerade beendete Starts (für alle Panels gleich).</summary>
    public System.Collections.ObjectModel.ObservableCollection<ActivityViewModel> Activities => _app.Activities.Items;
    public bool HasActivities => _app.Activities.Items.Count > 0;
    public string ActivitiesHeader => _app.Activities.RunningCount is var n and > 0 ? L.F("Running · {0}", n) : L.T("Last finished");

    public void RefreshActivities()
    {
        OnPropertyChanged(nameof(HasActivities));
        OnPropertyChanged(nameof(ActivitiesHeader));
        RefreshRunning();
    }

    /// <summary>„läuft“ an den Einträgen, die gerade laufen (auch nach einem Neuaufbau der Liste).</summary>
    private void RefreshRunning()
    {
        var running = _app.Activities.Items.Where(a => a.IsRunning && a.ItemPath != null)
            .Select(a => a.ItemPath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var i in AllItems) i.IsRunning = running.Count > 0 && running.Contains(i.Item.FullPath);
    }

    // ---- Rückgängig ---------------------------------------------------------

    /// <summary>Hinweis nach einer Änderung im Panel („„X“ ausgeblendet · Rückgängig“).</summary>
    public string? UndoToast => _app.UndoToast;
    public bool HasUndoToast => _app.UndoToast != null;
    public ICommand UndoCommand => _undoCommand ??= new RelayCommand(() => _app.Undo());
    public ICommand DismissUndoCommand => _dismissUndoCommand ??= new RelayCommand(() => _app.DismissUndoToast());
    private ICommand? _undoCommand, _dismissUndoCommand;

    public void RefreshUndo()
    {
        OnPropertyChanged(nameof(UndoToast));
        OnPropertyChanged(nameof(HasUndoToast));
    }

    /// <summary>Strg+Z im Panel.</summary>
    public bool TryUndo()
    {
        if (!_app.CanUndo) return false;
        _app.Undo();
        return true;
    }

    public void PauseUndoToast(bool paused) => _app.PauseUndoToast(paused);

    // ---- Suche: Hervorhebung und Filter-Chips --------------------------------

    private const int BestHitsCount = 5;

    /// <summary>Suchbegriffe ohne Filter – für die Hervorhebung in Name und Info-Zeile.</summary>
    public IReadOnlyList<string> HighlightTerms { get; private set; } = Array.Empty<string>();

    private IReadOnlyList<SearchChip> _searchChips = Array.Empty<SearchChip>();
    /// <summary>Erkannte Filter (typ:, gruppe:, ist:, #schlagwort) als Chips unter dem Suchfeld.</summary>
    public IReadOnlyList<SearchChip> SearchChips { get => _searchChips; private set { if (Set(ref _searchChips, value)) OnPropertyChanged(nameof(HasSearchChips)); } }
    public bool HasSearchChips => _searchChips.Count > 0;
    public string SearchHelp => SearchQuery.Help;

    private void UpdateSearchChips(SearchQuery query)
    {
        var chips = query.Operators
            .Select(t => new SearchChip(t.Label, t.Kind == SearchTokenKind.Invalid,
                t.Kind == SearchTokenKind.Invalid ? SearchQuery.Help : L.T("Remove filter"),
                new RelayCommand(() => SearchText = SearchQuery.Remove(_searchText, t))))
            .ToList();
        // Voreingestellter Filter des Panels (Einstellungen): als Chip sichtbar und mit ✕ für dieses Öffnen aufhebbar
        if (_filter != TypeFilter.All)
            chips.Insert(0, new SearchChip(_filter switch { TypeFilter.Scripts => L.T("Scripts"), TypeFilter.Programs => L.T("Programs"), _ => L.T("Favorites") },
                false, L.T("Preset filter of this panel – remove"), new RelayCommand(() => Filter = TypeFilter.All)));
        SearchChips = chips; // immer neu: die Befehle verweisen auf die aktuelle Position im Text
    }

    /// <summary>Plan einer Gruppe für den Abgleich (erst vollständig aufbauen und kürzen, dann mit vorhandenen ViewModels abgleichen).</summary>
    private sealed class GroupSpec
    {
        public required string Key { get; init; }
        public required string Title { get; init; }
        public required string Kind { get; init; }
        public bool IsFavorites { get; init; }
        public string? FolderPath { get; init; }
        public string? Error { get; init; }
        public bool Expanded { get; init; }
        public bool AllowReorder { get; init; }
        public required List<ItemViewModel> Items { get; init; }
        public List<GroupSpec> Children { get; } = new();
        public string? GroupPath => Kind is "group" or "subfolder" ? Title : null;
        public int TotalCount => Items.Count + Children.Sum(c => c.TotalCount);
        public bool HasError => !string.IsNullOrEmpty(Error);
    }

    // Wiederverwendung beim Tippen und beim Filterwechsel: ViewModels je Gruppe + Eintrag bzw. je Gruppe.
    // Jeder andere Rebuild (Einstellungen, Scan, Favoriten, Erreichbarkeit …) verwirft beide.
    private readonly Dictionary<string, ItemViewModel> _itemCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GroupViewModel> _groupCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nach jedem Rebuild (das Fenster aktualisiert Sticky-Header und Sprungleiste).</summary>
    public event Action? Rebuilt;

    /// <summary>Nur für die Leistungsmessung im Selbsttest: Wiederverwendung abschalten, um im selben Lauf zu vergleichen.</summary>
    internal bool ReuseForSearch { get; set; } = true;

    /// <summary>Änderungen an den Listen beim letzten Rebuild (Leistungsmessung).</summary>
    internal CollectionSync.Stats LastSyncStats { get; } = new();

    /// <summary>Baut die Gruppen anhand von Suche, Filter und Favoriten neu auf.</summary>
    public void Rebuild() => Rebuild(reuse: false);

    /// <summary>Nur für die Diagnose im Selbsttest: Rebuild wie beim Tippen, ohne dass sich etwas ändert.</summary>
    internal void RebuildUnchangedForTest() => Rebuild(reuse: true);

    /// <summary>Nur für die Diagnose im Selbsttest.</summary>
    internal void RaiseLayoutForTest() => RaiseLayout();

    /// <param name="reuse">
    /// true bei Suche und Filter: vorhandene ViewModels weiterverwenden und die Listen nur abgleichen,
    /// damit WPF nur für neue Treffer Container erzeugt. false: alles neu (Einstellungen haben sich geändert).
    /// </param>
    private void Rebuild(bool reuse) => WithLookup(() => { RebuildCore(reuse); return true; });

    private void RebuildCore(bool reuse)
    {
        reuse &= ReuseForSearch;
        if (!reuse) { _itemCache.Clear(); _groupCache.Clear(); }
        LastSyncStats.Reset();
        var settings = _app.Settings;
        var repo = _app.Repository;
        // Suchbegriffe (unscharf) und Filter: #schlagwort, typ:, gruppe:, ist:
        var query = SearchQuery.Parse(_searchText);
        HighlightTerms = query.Terms;
        UpdateSearchChips(query);
        bool searching = !query.IsEmpty;
        var collapsed = new HashSet<string>(settings.CollapsedGroups, StringComparer.OrdinalIgnoreCase);

        var pending = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var cfg = _host.Config;
        string GroupOf(LaunchItem i) => GroupOfItem(i);
        var visible = CollectVisible(pending);
        UpdatePending(pending);
        int? Score(LaunchItem i) => ScoreItem(i, query);
        bool FiltersOk(LaunchItem i) => FilterItem(i, query, _filter);

        var scores = new Dictionary<LaunchItem, int>(ReferenceEqualityComparer.Instance);
        var pool = new List<LaunchItem>();
        foreach (var i in visible)
        {
            if (!FiltersOk(i)) continue;
            if (query.HasTerms)
            {
                if (Score(i) is not { } score) continue;
                // Häufig und kürzlich gestartete Einträge nach oben
                scores[i] = score + SearchScoring.FrecencyBonus(FrecencyOf(i.FullPath));
            }
            pool.Add(i);
        }

        IEnumerable<LaunchItem> Order(IEnumerable<LaunchItem> items) => query.HasTerms
            ? items.OrderByDescending(i => scores.GetValueOrDefault(i)).ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            : items.OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase);

        // Eigene Reihenfolge (Drag & Drop) gilt nur ohne Suche
        List<LaunchItem> Arrange(string key, IEnumerable<LaunchItem> items)
        {
            var ordered = Order(items).ToList();
            return searching ? ordered : ItemOrdering.Apply(ordered, i => i.FullPath, settings.CustomOrder.GetValueOrDefault(key));
        }

        ItemViewModel ItemVm(LaunchItem i, string groupKey)
        {
            var cacheKey = groupKey + "|" + i.FullPath;
            if (_itemCache.TryGetValue(cacheKey, out var cached) && cached.Represents(i))
            {
                cached.RefreshHighlight();
                return cached;
            }
            var vm = new ItemViewModel(_app, this, i, groupKey);
            _itemCache[cacheKey] = vm;
            return vm;
        }

        GroupSpec MakeGroup(string key, string title, string kind, string? folderPath, IEnumerable<LaunchItem> items, string? error = null) => new()
        {
            Key = key,
            Title = title,
            Kind = kind,
            IsFavorites = kind == "fav",
            FolderPath = folderPath,
            Error = error,
            Expanded = searching || !collapsed.Contains(key),
            AllowReorder = !searching,
            Items = Arrange(key, items).Select(i => ItemVm(i, key)).ToList()
        };

        var groups = new List<GroupSpec>();
        // Bei vielen Treffern: die besten oben gesammelt (Enter startet den ersten)
        if (query.HasTerms && pool.Count > BestHitsCount)
            groups.Add(new GroupSpec
            {
                Key = "best",
                Title = L.T("Best matches"),
                Kind = "best",
                Expanded = true,
                AllowReorder = false,
                Items = Order(pool).Take(BestHitsCount).Select(i => ItemVm(i, "best")).ToList()
            });
        // Feste Gruppen (Zuletzt ausgeführt, Favoriten, Eigene Einträge): Position und Sichtbarkeit wie eigene Gruppen
        var fixedSpecs = new Dictionary<string, GroupSpec>();
        var topLevel = new List<GroupSpec>();
        if (!searching && _filter == TypeFilter.All)
        {
            // Zuletzt ausgeführt (mit dem jeweils verwendeten Profil)
            if (settings.RecentRunsCount > 0 && ShowsGroup(FixedGroups.Recent))
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
                        // Zeit rechts in der Zeile; darunter das Profil (falls gewählt), sonst die normale Info-Zeile
                        var subtitle = profile.IsImplicit ? null : L.T("Profile ") + profile.Name;
                        return new ItemViewModel(_app, this, item, "recent", profile, subtitle, RecentRunList.Ago(r.TimeUtc));
                    })
                    .ToList();
                if (recent.Count > 0)
                    fixedSpecs[FixedGroups.Recent] = new GroupSpec
                    {
                        Key = "recent",
                        Title = L.T("Recently run"),
                        Kind = "recent",
                        Expanded = !collapsed.Contains("recent"),
                        AllowReorder = false,
                        Items = recent
                    };
            }

            var favs = pool.Where(i => IsFav(i.FullPath)).ToList();
            if (favs.Count > 0 && ShowsGroup(FixedGroups.Favorites)) fixedSpecs[FixedGroups.Favorites] = MakeGroup("fav", L.T("Favorites"), "fav", null, favs);
        }

        // Gruppenbaum: eigene und smarte Gruppen (Ordner), bei „Unterordner als Untergruppen“ zusätzlich die Unterordner
        bool showEmpty = !searching && _filter == TypeFilter.All;
        var byGroup = pool.GroupBy(GroupOf, StringComparer.CurrentCultureIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.CurrentCultureIgnoreCase);
        var paths = GroupManager.All(settings).ToList();
        var virtualPaths = new List<string>();
        foreach (var g in byGroup.Keys.Where(k => k.Length > 0))
            for (var p = g; p != null; p = GroupManager.ParentOf(p))
                if (!paths.Contains(p, StringComparer.CurrentCultureIgnoreCase) && !virtualPaths.Contains(p, StringComparer.CurrentCultureIgnoreCase)) virtualPaths.Add(p);
        paths.AddRange(virtualPaths.OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase));

        (string? Folder, string? Error) FolderInfo(string path)
        {
            if (SmartGroups.FolderOf(settings, path) is { } f)
            {
                var status = repo.FolderStatuses.FirstOrDefault(s => string.Equals(s.Path, f.Path, StringComparison.OrdinalIgnoreCase));
                return (f.Path, status?.Error);
            }
            // Unterordner einer smarten Gruppe
            for (var parent = GroupManager.ParentOf(path); parent != null; parent = GroupManager.ParentOf(parent))
                if (SmartGroups.FolderOf(settings, parent) is { SubfoldersAsGroups: true } pf)
                    return (Path.Combine(pf.Path, path[(parent.Length + 1)..].Replace(GroupManager.Separator, '\\')), null);
            return (null, null);
        }

        var nodes = new Dictionary<string, GroupSpec>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var path in paths)
        {
            if (!ShowsGroup(path)) continue;
            var items = byGroup.GetValueOrDefault(path) ?? new List<LaunchItem>();
            var (folderPath, error) = FolderInfo(path);
            var kind = virtualPaths.Contains(path, StringComparer.CurrentCultureIgnoreCase) ? "subfolder" : "group";
            var node = MakeGroup(GroupManager.GroupKey(path), path, kind, folderPath, items, error);
            nodes[path] = node;
            if (GroupManager.ParentOf(path) is { } parentPath)
            {
                if (nodes.TryGetValue(parentPath, out var parentNode)) parentNode.Children.Add(node);
            }
            else topLevel.Add(node);
        }
        // Leere Zweige bei Suche/Filter weglassen (fehlerhafte Ordner bleiben sichtbar)
        bool Prune(GroupSpec g)
        {
            g.Children.RemoveAll(c => !Prune(c));
            return showEmpty || g.TotalCount > 0 || g.HasError;
        }
        topLevel.RemoveAll(g => g.GroupPath != null && !Prune(g));

        // Eigene Einträge ohne Gruppe (bei der Suche immer, damit Treffer nicht verschwinden)
        var rest = byGroup.GetValueOrDefault(string.Empty) ?? new List<LaunchItem>();
        if (rest.Count > 0 && (searching || ShowsGroup(FixedGroups.Custom)))
            fixedSpecs[FixedGroups.Custom] = MakeGroup(SmartGroups.CustomFallbackKey, L.T("Custom entries"), "custom", null, rest);

        // Oberste Ebene: eigene Gruppen und feste Gruppen in der eingestellten Reihenfolge
        var topByPath = topLevel.ToDictionary(g => g.GroupPath ?? g.Key, StringComparer.CurrentCultureIgnoreCase);
        foreach (var key in FixedGroups.Compose(settings, topLevel.Select(g => g.GroupPath ?? g.Key).ToList()))
        {
            if (FixedGroups.IsFixed(key)) { if (fixedSpecs.TryGetValue(key, out var f)) groups.Add(f); }
            else if (topByPath.TryGetValue(key, out var g)) groups.Add(g);
        }

        // Alt+1 … 9: Favoriten in der Reihenfolge der Gruppe „Favoriten“ (unabhängig von Suche und Filter)
        var favorites = Arrange("fav", visible.Where(i => IsFav(i.FullPath))).ToList();
        var quickChanged = !favorites.Select(q => q.FullPath).SequenceEqual(_favoriteItems.Select(q => q.FullPath), StringComparer.OrdinalIgnoreCase);
        _favoriteItems = favorites;
        _quickItems = favorites.Take(9).ToList();
        if (quickChanged) QuickItemsChanged?.Invoke();
        foreach (var favGroup in groups.Where(x => x.Kind == "fav"))
            foreach (var favVm in favGroup.Items)
            {
                var quickIndex = _quickItems.FindIndex(q => q.FullPath.Equals(favVm.Item.FullPath, StringComparison.OrdinalIgnoreCase));
                favVm.QuickKeyText = quickIndex >= 0 ? $"Alt+{quickIndex + 1}" : null;
            }

        GroupViewModel Materialize(GroupSpec spec)
        {
            if (_groupCache.TryGetValue(spec.Key, out var g) &&
                g.CanReuseFor(spec.Title, spec.Kind, spec.IsFavorites, spec.FolderPath, spec.Error))
            {
                // Suche an/aus: aufklappen und Drag & Drop umschalten, ohne die Gruppe neu zu erzeugen
                if (g.IsExpanded != spec.Expanded) g.SetExpandedSilently(spec.Expanded);
                g.AllowReorder = spec.AllowReorder;
            }
            else
            {
                g = new GroupViewModel(this, _app, spec.Key, spec.Title, spec.IsFavorites, spec.FolderPath,
                    Array.Empty<ItemViewModel>(), spec.Expanded, spec.Error, spec.Kind, spec.AllowReorder);
                _groupCache[spec.Key] = g;
            }
            CollectionSync.SyncTo(g.Items, spec.Items, LastSyncStats);
            CollectionSync.SyncTo(g.Children, spec.Children.Select(Materialize).ToList(), LastSyncStats);
            g.RaiseContentChanged();
            return g;
        }
        CollectionSync.SyncTo(Groups, groups.Select(Materialize).ToList(), LastSyncStats);
        // Wiederverwendete Einträge behalten ihren Haken – mit der Mehrfachauswahl abgleichen
        if (_checked.Count > 0 || reuse) ApplyChecked();
        if (!reuse) StickyGroup = null;
        OnPropertyChanged(nameof(ShowJumpRail));
        OnPropertyChanged(nameof(SelectedItem)); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(ShowTileDetails));

        // Auswahl wiederherstellen
        ItemViewModel? selected = null;
        foreach (var i in AllItems)
        {
            if (i.Key == _selectedKey && selected == null) selected = i;
            else if (i.IsSelected) i.IsSelected = false; // wiederverwendet, aber nicht mehr ausgewählt
        }
        if (selected != null) selected.IsSelected = true;
        else _selectedKey = null;

        var errors = repo.FolderStatuses.Where(s => s.Error != null).ToList();
        FolderWarning = errors.Count == 0 ? null
            : errors.Count == 1 ? $"{errors[0].Path}: {errors[0].Error}"
            : L.F("{0} folders unreachable", errors.Count);

        if (repo.IsScanning && repo.Items.Count == 0) Footer = L.T("Scanning folders …");
        else if (searching) Footer = L.F("{0} matches for “{1}”", pool.Count, _searchText.Trim());
        else Footer = L.F("{0} entries · scanned {1:t}", visible.Count, repo.LastScan) + (HasWorkspaces ? $" · {settings.ActiveWorkspace}" : string.Empty);

        OnPropertyChanged(nameof(HasNoFolders));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HotkeyHint));
        OnPropertyChanged(nameof(HasHotkey));
        OnPropertyChanged(nameof(IsPinned));
        RaiseLayoutIfChanged(); // neue Gruppen lesen ihre Darstellung selbst
        OnPropertyChanged(nameof(Workspaces));
        OnPropertyChanged(nameof(ActiveWorkspace));
        OnPropertyChanged(nameof(HasWorkspaces));
        OnPropertyChanged(nameof(PanelName));
        OnPropertyChanged(nameof(IsMultiPanel));
        if (_app.Activities.HasRunning) RefreshRunning(); // neu erzeugte Zeilen: „läuft“ übernehmen
        Rebuilt?.Invoke();
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
                ? (total == 1 ? L.F("1 new entry in {0} is waiting for approval", _pendingFolder) : L.F("{0} new entries in {1} are waiting for approval", total, _pendingFolder))
                : L.F("{0} new entries in {1} folders are waiting for approval", total, pending.Count);
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
        _app.RememberForUndo(L.T("Order changed"));
        _app.Settings.CustomOrder[group.Key] = ItemOrdering.Move(current, dragged.Item.FullPath, target.Item.FullPath, after);
        _app.SaveSettings();
        Rebuild();
        _app.ShowUndoToast(L.F("“{0}” moved", dragged.DisplayName));
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
        // Zurück in die eigene smarte Gruppe bzw. ihren Unterordner: ausdrückliche Zuordnung entfernen
        if (_app.FindItem(path) is { } found && SmartGroups.ImplicitGroup(_app.Settings, found.FullPath, found.RootFolder)
                .Equals(wanted, StringComparison.CurrentCultureIgnoreCase)) wanted = string.Empty;
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

    // ---- Gruppen ziehen: Lücke an der Zielstelle (Optionen unter Panel › Verhalten) ----------------

    private GroupViewModel? _dragGroup;
    private GroupViewModel? _gapGroup;
    private GroupDropPlan? _dropPlan;
    private string _dragLabel = string.Empty;
    private ArgbColor? _dragColor;
    private bool _groupDragCompact;

    /// <summary>Text im Platzhalter und im Ziehbild: Name der Gruppe (+ Zahl der Untergruppen).</summary>
    public string DragLabel { get => _dragLabel; private set => Set(ref _dragLabel, value); }
    /// <summary>Farbe der gezogenen Gruppe (Punkt im Platzhalter).</summary>
    public ArgbColor? DragColor { get => _dragColor; private set => Set(ref _dragColor, value); }
    /// <summary>Während des Ziehens nur Gruppenköpfe zeigen (Option „Inhalte beim Ziehen zuklappen“).</summary>
    public bool GroupDragCompact { get => _groupDragCompact; private set => Set(ref _groupDragCompact, value); }
    /// <summary>Lücke weich öffnen (Option und Einstellung „Animation“).</summary>
    public bool AnimateDropGap => _app.Settings.GroupDragAnimate && _app.Settings.Animation != AnimationMode.Off;
    public bool IsDraggingGroup => _dragGroup != null;
    public GroupViewModel? DraggedGroup => _dragGroup;
    public GroupDropPlan? CurrentGroupDropPlan => _dropPlan;

    /// <summary>Ziehen einer eigenen Gruppe beginnt: Quelle macht Platz, Inhalte ggf. zuklappen.</summary>
    public void BeginGroupDrag(GroupViewModel group)
    {
        EndGroupDrag();
        if (!group.CanDrag) return;
        _dragGroup = group;
        var sub = group.SelfAndDescendants.Count() - 1;
        DragLabel = group.Title + (sub > 0 ? (sub == 1 ? L.T("  + 1 subgroup") : L.F("  + {0} subgroups", sub)) : string.Empty);
        DragColor = group.AccentColor;
        OnPropertyChanged(nameof(AnimateDropGap));
        group.IsDragSource = true;
        GroupDragCompact = _app.Settings.GroupDragCollapse;
    }

    /// <summary>Eigene Gruppen in Anzeigereihenfolge, soweit sichtbar (eingeklappte Untergruppen nicht).</summary>
    internal List<string> VisibleUserGroupOrder()
    {
        var order = new List<string>();
        void Walk(GroupViewModel g)
        {
            if (g.IsUserGroup && g.GroupPath != null) order.Add(g.GroupPath);
            if (g.IsExpanded) foreach (var c in g.Children) Walk(c);
        }
        foreach (var g in Groups) Walk(g);
        return order;
    }

    /// <summary>
    /// Ziel für die Mausposition bestimmen und die Lücke dort öffnen. <paramref name="levelOffset"/> = seitlicher Versatz in Ebenen.
    /// </summary>
    public GroupDropPlan? PreviewGroupDrop(GroupViewModel hovered, GroupDropZone zone, int levelOffset)
    {
        if (_dragGroup == null) return null;
        if (_dragGroup.FixedKey != null || hovered.FixedKey != null) return PreviewTopLevelDrop(hovered, zone);
        _topDrop = null;
        if (_dragGroup.GroupPath is not { } dragged) return null;
        var plan = !hovered.IsUserGroup || hovered.GroupPath == null
            ? new GroupDropPlan { IsValid = false, Reason = L.T("Groups can only be dropped between your own groups.") }
            : GroupDropPlanner.Plan(VisibleUserGroupOrder(), dragged, hovered.GroupPath, zone, levelOffset, siblingsOnly: false,
                path => GroupManager.Exists(_app.Settings, path));
        _dropPlan = plan;
        GroupViewModel? target = null;
        GroupDropSlot slot = GroupDropSlot.None;
        if (plan.IsValid && !(plan.IsNoOp && GroupManager.IsSameOrBelow(hovered.GroupPath ?? string.Empty, dragged)))
        {
            target = AllGroups.FirstOrDefault(g => string.Equals(g.GroupPath, plan.Target, StringComparison.CurrentCultureIgnoreCase));
            slot = plan.Position switch
            {
                GroupDropPosition.Before => GroupDropSlot.Before,
                GroupDropPosition.After => GroupDropSlot.After,
                _ => GroupDropSlot.Inside
            };
        }
        if (!ReferenceEquals(_gapGroup, target) && _gapGroup != null) _gapGroup.DropSlot = GroupDropSlot.None;
        _gapGroup = target;
        if (target != null) target.DropSlot = slot;
        return plan;
    }

    /// <summary>Feste Gruppe beteiligt: oberste Ebene, Lücke vor oder hinter der Gruppe der obersten Ebene unter der Maus.</summary>
    private (string Anchor, bool After)? _topDrop;

    private static string? TopKeyOf(GroupViewModel g) => g.FixedKey ?? g.GroupPath;

    private GroupDropPlan PreviewTopLevelDrop(GroupViewModel hovered, GroupDropZone zone)
    {
        var dragged = _dragGroup!;
        var anchor = Groups.FirstOrDefault(t => ReferenceEquals(t, hovered) || t.SelfAndDescendants.Contains(hovered)) ?? hovered;
        GroupDropPlan plan;
        GroupDropSlot slot = GroupDropSlot.None;
        if (anchor.Kind == "best" || TopKeyOf(anchor) is not { } anchorKey)
            plan = new GroupDropPlan { IsValid = false, Reason = L.T("Nothing can be dropped here.") };
        else if (ReferenceEquals(anchor, dragged))
            plan = new GroupDropPlan { IsValid = true, IsNoOp = true, Target = anchorKey };
        else if (zone == GroupDropZone.Into && dragged.FixedKey != null && anchor.FixedKey == null)
            plan = new GroupDropPlan { IsValid = false, Reason = L.T("Fixed groups stay at the top level – drag them before or after a group.") };
        else
        {
            var after = zone != GroupDropZone.Before;
            slot = after ? GroupDropSlot.After : GroupDropSlot.Before;
            _topDrop = (anchorKey, after);
            plan = new GroupDropPlan { IsValid = true, Target = anchorKey, Position = after ? GroupDropPosition.After : GroupDropPosition.Before };
        }
        if (!plan.IsValid || plan.IsNoOp) _topDrop = null;
        _dropPlan = plan;
        var target = slot == GroupDropSlot.None ? null : anchor;
        if (!ReferenceEquals(_gapGroup, target) && _gapGroup != null) _gapGroup.DropSlot = GroupDropSlot.None;
        _gapGroup = target;
        if (target != null) target.DropSlot = slot;
        return plan;
    }

    /// <summary>Über der leeren Fläche unter allen Gruppen: hinter die letzte sichtbare Gruppe.</summary>
    public GroupDropPlan? PreviewGroupDropAtEnd(int levelOffset)
    {
        if (_dragGroup?.FixedKey != null)
            return Groups.LastOrDefault(g => !ReferenceEquals(g, _dragGroup) && TopKeyOf(g) != null) is { } lastTop ? PreviewGroupDrop(lastTop, GroupDropZone.After, levelOffset) : null;
        var last = VisibleUserGroupOrder().LastOrDefault(p => _dragGroup?.GroupPath == null || !GroupManager.IsSameOrBelow(p, _dragGroup.GroupPath));
        var lastVm = last == null ? null : AllGroups.FirstOrDefault(g => string.Equals(g.GroupPath, last, StringComparison.CurrentCultureIgnoreCase));
        return lastVm == null ? null : PreviewGroupDrop(lastVm, GroupDropZone.After, levelOffset);
    }

    /// <summary>Loslassen: geplante Verschiebung ausführen. true, wenn sich etwas geändert hat.</summary>
    public bool CommitGroupDrop()
    {
        var plan = _dropPlan;
        var path = _dragGroup?.GroupPath;
        var topKey = _dragGroup == null ? null : TopKeyOf(_dragGroup);
        var top = _topDrop;
        EndGroupDrag();
        if (top is { } t && topKey != null && plan is { IsValid: true, IsNoOp: false })
            return _app.MoveTopLevel(topKey, t.Anchor, t.After);
        if (plan is not { IsValid: true, IsNoOp: false } || path == null) return false;
        _app.MoveGroupTo(path, plan.Target, plan.Position);
        return true;
    }

    /// <summary>Ziehen beendet oder abgebrochen: Lücke schließen, Quelle wieder zeigen.</summary>
    public void EndGroupDrag()
    {
        if (_gapGroup != null) _gapGroup.DropSlot = GroupDropSlot.None;
        _gapGroup = null;
        if (_dragGroup != null) _dragGroup.IsDragSource = false;
        _dragGroup = null;
        _dropPlan = null;
        _topDrop = null;
        GroupDragCompact = false;
    }

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

    /// <summary>
    /// Details unten im Panel zeigen: der ausgewählte Eintrag steht als Kachel da – das Panel oder seine Gruppe ist auf
    /// „Raster“ gestellt (in der Liste klappen die Details direkt am Eintrag auf).
    /// </summary>
    public bool ShowTileDetails => SelectedItem is { } selected &&
        (Groups.SelectMany(g => g.SelfAndDescendants).FirstOrDefault(g => g.Items.Contains(selected)) is { } group ? group.IsGrid : IsGrid);

    /// <summary>Einstellungen des Panels wurden ersetzt: alle Anzeigen neu lesen (Darstellung, Größe, Sprungleiste …).</summary>
    public void ConfigReplaced()
    {
        OnPropertyChanged(string.Empty);
        foreach (var g in Groups.SelectMany(g => g.SelfAndDescendants)) g.RefreshLayout();
    }

    /// <summary>Schlüssel des ausgewählten Eintrags (bleibt über einen Neuaufbau erhalten).</summary>
    public string? SelectedKey => _selectedKey;

    /// <summary>Vor dem ersten <see cref="Rebuild"/>: diesen Eintrag wieder auswählen (Details bleiben aufgeklappt).</summary>
    public void PreselectKey(string? key) => _selectedKey = key;
    public bool HasSelection => SelectedItem != null;

    public void Select(ItemViewModel item)
    {
        bool wasSelected = item.IsSelected;
        foreach (var i in AllItems) i.IsSelected = false;
        item.IsSelected = !wasSelected;
        _selectedKey = item.IsSelected ? item.Key : null;
        OnPropertyChanged(nameof(SelectedItem)); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(ShowTileDetails));
        // Offene Vorschau folgt dem angeklickten Eintrag
        if (_peek != null && item.IsSelected && !ReferenceEquals(_peek.Item, item)) Peek = new PeekViewModel(_app, item);
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
        OnPropertyChanged(nameof(SelectedItem)); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(ShowTileDetails));
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
        _filter = TypeFilter.All;
        _selectedKey = null;
        ShowShortcuts = false;
        ShowGroupJump = false;
        Peek = null;
        _suggestionSource = null; // beim nächsten Öffnen frisch (neue Schlagworte, Gruppen …)
        Suggestions = Array.Empty<SearchSuggestion>();
        ShowHistory = false;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(Filter));
        Rebuild();
    }

    public void ClearSelection()
    {
        foreach (var i in AllItems) i.IsSelected = false;
        _selectedKey = null;
        OnPropertyChanged(nameof(SelectedItem)); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(ShowTileDetails));
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

/// <summary>Ein erkannter Filter unter dem Suchfeld; ✕ entfernt ihn aus dem Suchtext.</summary>
public sealed record SearchChip(string Label, bool IsInvalid, string ToolTip, ICommand RemoveCommand);

/// <summary>Eine Zeile im Spickzettel der Tastenkürzel.</summary>
public sealed record ShortcutInfo(string Keys, string Action);

/// <summary>Treffer in der Gruppensuche (Strg+K).</summary>
public sealed record GroupJumpItem(GroupViewModel Group, IReadOnlyList<TextPart> TitleParts, string PathText, ArgbColor? Color, string CountText)
{
    public bool HasPath => PathText.Length > 0;
}
