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
        AddFolderCommand = new RelayCommand(() => _app.ShowSettings(section: "groups"));
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
        foreach (var p in new[] { nameof(Layout), nameof(IsCompact), nameof(IsLarge), nameof(ItemSize), nameof(ItemSizeText), nameof(IsGrid), nameof(IsList), nameof(LayoutGlyph), nameof(LayoutToolTip) }) OnPropertyChanged(p);
        foreach (var g in AllGroups) g.RefreshLayout();
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
    public string ItemSizeText => ItemSize switch { ItemSize.Small => "Klein", ItemSize.Large => "Groß", _ => "Mittel" };
    public ICommand SetItemSizeCommand => new RelayCommand(p => { if (Enum.TryParse<ItemSize>(p?.ToString(), out var size)) ItemSize = size; });

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
    public ICommand NewSmartGroupCommand => new RelayCommand(() => _app.CreateSmartGroup());

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

    public string HotkeyHint => HotkeyService.Display(_host.Config.Hotkey);
    public bool HasHotkey => !string.IsNullOrWhiteSpace(_host.Config.Hotkey);

    public string Footer { get => _footer; private set => Set(ref _footer, value); }
    public string? FolderWarning { get => _folderWarning; private set => Set(ref _folderWarning, value); }
    public bool HasNoFolders => _app.Settings.Folders.Count == 0 && _app.Settings.CustomEntries.Count == 0 && GroupManager.All(_app.Settings).Count == 0;
    public string EmptyHint => "Noch keine Gruppen. Lege eine smarte Gruppe mit einem Ordner an oder füge eigene Einträge hinzu.";
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

    // ---- Tastatur ------------------------------------------------------------

    private List<LaunchItem> _quickItems = new();

    /// <summary>Alt+1 … 9: n-ten Favoriten mit seinem Standardprofil starten.</summary>
    public bool RunQuickItem(int number)
    {
        if (number < 1 || number > _quickItems.Count) return false;
        var item = _quickItems[number - 1];
        _app.RunProfile(item, ProfileResolver.GetDefault(_app.Settings, item));
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
        new("↑ ↓", "Eintrag auswählen"),
        new("Enter", "Starten (Standardprofil)"),
        new("Strg+Enter", "Als Administrator starten"),
        new("Strg+Umschalt+Enter", "Als anderer Benutzer starten"),
        new("Strg+R", "Remote ausführen …"),
        new("→ / Tab", "Alle Aktionen des Eintrags"),
        new("Alt+1 … 9", "Favorit 1 … 9 starten"),
        new("Strg+D", "Favorit an/aus"),
        new("F2", "Eintrag bearbeiten / Startprofile"),
        new("Leertaste", "Vorschau: Code, Parameter, Signatur"),
        new("Strg+E", "Im Editor öffnen"),
        new("Strg+Umschalt+C", "Pfad kopieren"),
        new("Strg+Z", "Letzte Änderung rückgängig"),
        new("Strg+V", "Eintrag aus Zwischenablage"),
        new("Esc", "Suche leeren / Panel schließen"),
        new("F1 oder ?", "Diese Übersicht"),
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

    /// <summary>Sprungleiste rechts: ab 4 Gruppen auf oberster Ebene, nicht während der Suche.</summary>
    public bool ShowJumpRail => Groups.Count >= 4 && !IsSearching;

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
                x.Group.Depth > 0 ? x.Group.Breadcrumb : string.Empty, x.Group.AccentBrush, x.Group.CountText))
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

    // ---- Neu in dieser Version -----------------------------------------------------

    public bool WhatsNewVisible => _app.WhatsNew != null;
    public string WhatsNewTitle => _app.WhatsNew is { } n ? $"Neu in {n.Version.ToString(2)}: {n.Title}" : string.Empty;
    /// <summary>Die ersten Neuerungen (alle über „Alle Neuerungen“).</summary>
    public IReadOnlyList<string> WhatsNewItems => _app.WhatsNew?.Highlights.Take(4).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
    public ICommand DismissWhatsNewCommand => _dismissWhatsNewCommand ??= new RelayCommand(() => _app.DismissWhatsNew());
    public ICommand ShowAllWhatsNewCommand => _showAllWhatsNewCommand ??= new RelayCommand(() =>
    {
        if (_app.WhatsNew is { } n)
            Services.Launcher.OpenUrl($"https://github.com/{Core.Updates.UpdateChecker.Owner}/{Core.Updates.UpdateChecker.Repo}/releases/tag/v{n.Version.ToString(3)}");
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
    public string ActivitiesHeader => _app.Activities.RunningCount is var n and > 0 ? $"Läuft · {n}" : "Zuletzt beendet";

    public void RefreshActivities()
    {
        OnPropertyChanged(nameof(HasActivities));
        OnPropertyChanged(nameof(ActivitiesHeader));
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
                t.Kind == SearchTokenKind.Invalid ? SearchQuery.Help : "Filter entfernen",
                new RelayCommand(() => SearchText = SearchQuery.Remove(_searchText, t))))
            .ToList();
        SearchChips = chips; // immer neu: die Befehle verweisen auf die aktuelle Position im Text
    }

    /// <summary>Baut die Gruppen anhand von Suche, Filter und Favoriten neu auf.</summary>
    public void Rebuild()
    {
        var settings = _app.Settings;
        var repo = _app.Repository;
        // Suchbegriffe (unscharf) und Filter: #schlagwort, typ:, gruppe:, ist:
        var query = SearchQuery.Parse(_searchText);
        HighlightTerms = query.Terms;
        UpdateSearchChips(query);
        bool searching = !query.IsEmpty;
        var collapsed = new HashSet<string>(settings.CollapsedGroups, StringComparer.OrdinalIgnoreCase);

        // Nur sichtbare Einträge (Haken in den Ordner-Einstellungen, neue Dateien ggf. erst nach Freigabe)
        var folderByPath = settings.Folders.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var pending = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var visible = new List<LaunchItem>();
        var cfg = _host.Config;
        // Gruppe eines Eintrags: ausdrücklich zugeordnet, sonst smarte Gruppe seines Ordners (ggf. mit Unterordner)
        string GroupOf(LaunchItem i) => SmartGroups.EffectiveGroup(settings, i.FullPath, i.RootFolder);
        // Arbeitsbereich: ausgeblendete Gruppen (samt Untergruppen) erscheinen nicht
        bool CategoryOk(LaunchItem i) => WorkspaceManager.ShowsGroup(settings, GroupOf(i));
        foreach (var i in repo.Items)
        {
            if (!folderByPath.TryGetValue(i.RootFolder, out var folder)) continue;
            var state = FolderVisibility.State(folder, FolderVisibility.RelativePath(folder, i.FullPath));
            if (state == EntryVisibility.Visible) { if (CategoryOk(i)) visible.Add(i); }
            else if (state == EntryVisibility.Pending) pending[folder.Path] = pending.GetValueOrDefault(folder.Path) + 1;
        }
        // Eigene Einträge ohne Datei (Webseiten, Ordner, RDP, Befehle)
        var customItems = new List<LaunchItem>();
        {
            var seen = new HashSet<string>(visible.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
            foreach (var c in settings.CustomEntries)
            {
                // Snippets haben kein Ziel, nur Code
                if (c.Target.Trim().Length == 0 && (c.Kind != CustomEntryKind.Snippet || c.Code.Trim().Length == 0)) continue;
                var li = _app.DescribeCustom(c);
                if (!seen.Add(li.FullPath)) continue; // Datei liegt bereits in einem durchsuchten Ordner
                if (CategoryOk(li)) customItems.Add(li);
            }
        }
        visible.AddRange(customItems);
        UpdatePending(pending);

        // Bewertung: Name zählt am meisten, dann Dateiname und Schlagworte, dann Beschreibung, Notiz und Code
        int? Score(LaunchItem i)
        {
            var e = settings.FindEntry(i.FullPath);
            var fields = new List<(string?, int)>
            {
                (i.DisplayName, 3), (e?.DisplayName, 3), (i.FileName, 2), (e == null ? null : string.Join(' ', e.Tags), 2),
                (i.Synopsis, 1), (i.Description, 1), (e?.Description, 1), (e?.Notes, 1), (e?.Category, 1), (i.Source?.Code, 1)
            };
            return SearchScoring.Score(fields, query.Terms);
        }
        bool FiltersOk(LaunchItem i)
        {
            var fav = _app.IsFavorite(i.FullPath);
            return ItemFilter.MatchesType(i, _filter, fav) &&
                   query.MatchesFilters(i, GroupOf(i), fav, Tags.Of(settings, i.FullPath),
                       () => i.Source != null && _app.Health?.Get(i.Source.Key) is { Ok: false });
        }

        var scores = new Dictionary<LaunchItem, int>(ReferenceEqualityComparer.Instance);
        var pool = new List<LaunchItem>();
        foreach (var i in visible)
        {
            if (!FiltersOk(i)) continue;
            if (query.HasTerms)
            {
                if (Score(i) is not { } score) continue;
                // Häufig und kürzlich gestartete Einträge nach oben
                scores[i] = score + SearchScoring.FrecencyBonus(RunStats.Frecency(settings, i.FullPath));
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

        GroupViewModel MakeGroup(string key, string title, string kind, string? folderPath, IEnumerable<LaunchItem> items, string? error = null) =>
            new(this, _app, key, title, kind == "fav", folderPath,
                Arrange(key, items).Select(i => new ItemViewModel(_app, this, i, key)).ToList(),
                searching || !collapsed.Contains(key), error, kind, allowReorder: !searching);

        var groups = new List<GroupViewModel>();
        // Bei vielen Treffern: die besten oben gesammelt (Enter startet den ersten)
        if (query.HasTerms && pool.Count > BestHitsCount)
            groups.Add(new GroupViewModel(this, _app, "best", "Beste Treffer", false, null,
                Order(pool).Take(BestHitsCount).Select(i => new ItemViewModel(_app, this, i, "best")).ToList(),
                true, null, "best", allowReorder: false));
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

        var nodes = new Dictionary<string, GroupViewModel>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var path in paths)
        {
            if (!WorkspaceManager.ShowsGroup(settings, path)) continue;
            var items = byGroup.GetValueOrDefault(path) ?? new List<LaunchItem>();
            var (folderPath, error) = FolderInfo(path);
            var kind = virtualPaths.Contains(path, StringComparer.CurrentCultureIgnoreCase) ? "subfolder" : "group";
            var node = MakeGroup(GroupManager.GroupKey(path), path, kind, folderPath, items, error);
            nodes[path] = node;
            if (GroupManager.ParentOf(path) is { } parentPath)
            {
                if (nodes.TryGetValue(parentPath, out var parentNode)) parentNode.Children.Add(node);
            }
            else groups.Add(node);
        }
        // Leere Zweige bei Suche/Filter weglassen (fehlerhafte Ordner bleiben sichtbar)
        bool Prune(GroupViewModel g)
        {
            g.Children.RemoveAll(c => !Prune(c));
            return showEmpty || g.TotalCount > 0 || g.HasError;
        }
        groups.RemoveAll(g => g.GroupPath != null && !Prune(g));

        // Eigene Einträge ohne Gruppe
        var rest = byGroup.GetValueOrDefault(string.Empty) ?? new List<LaunchItem>();
        if (rest.Count > 0) groups.Add(MakeGroup(SmartGroups.CustomFallbackKey, "Eigene Einträge", "custom", null, rest));

        // Alt+1 … 9: Favoriten in der Reihenfolge der Gruppe „Favoriten“ (unabhängig von Suche und Filter)
        _quickItems = Arrange("fav", visible.Where(i => _app.IsFavorite(i.FullPath))).Take(9).ToList();
        foreach (var favGroup in groups.Where(x => x.Kind == "fav"))
            foreach (var favVm in favGroup.Items)
            {
                var quickIndex = _quickItems.FindIndex(q => q.FullPath.Equals(favVm.Item.FullPath, StringComparison.OrdinalIgnoreCase));
                if (quickIndex >= 0) favVm.QuickKeyText = $"Alt+{quickIndex + 1}";
            }

        Groups.Clear();
        foreach (var g in groups) Groups.Add(g);
        StickyGroup = null;
        OnPropertyChanged(nameof(ShowJumpRail));
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
        _app.RememberForUndo("Reihenfolge geändert");
        _app.Settings.CustomOrder[group.Key] = ItemOrdering.Move(current, dragged.Item.FullPath, target.Item.FullPath, after);
        _app.SaveSettings();
        Rebuild();
        _app.ShowUndoToast($"„{dragged.DisplayName}“ verschoben");
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
        ShowShortcuts = false;
        ShowGroupJump = false;
        Peek = null;
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

/// <summary>Ein erkannter Filter unter dem Suchfeld; ✕ entfernt ihn aus dem Suchtext.</summary>
public sealed record SearchChip(string Label, bool IsInvalid, string ToolTip, ICommand RemoveCommand);

/// <summary>Eine Zeile im Spickzettel der Tastenkürzel.</summary>
public sealed record ShortcutInfo(string Keys, string Action);

/// <summary>Treffer in der Gruppensuche (Strg+K).</summary>
public sealed record GroupJumpItem(GroupViewModel Group, IReadOnlyList<TextPart> TitleParts, string PathText, System.Windows.Media.Brush? Color, string CountText)
{
    public bool HasPath => PathText.Length > 0;
}
