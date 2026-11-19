using PowerExec.Core.Localization;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;

/// <summary>
/// Einstellungsseite „Marktplatz“: Ordner der Quellen (Git-Repositorys) als aufklappbare Liste mit Tabs nach Art
/// (Einträge, Scripte, Designs, Installiert). Einzelne Dateien oder ganze Ordner per Häkchen herunterladen bzw.
/// deinstallieren; Einträge und Scripte landen in den eigenen Einträgen der gewählten Gruppe.
/// </summary>
public sealed class MarketplaceViewModel : ObservableObject
{
    private readonly ISettingsHost _app;
    private readonly AppSettings _s;
    private readonly Action _changed;
    private VariablesViewModel? _variables;
    private string _query = string.Empty;
    private string? _error;
    private bool _loading;

    // Über den Neuaufbau des Einstellungsfensters hinweg: geladene Quellen, gelesene Dateien, Ansicht
    private static readonly Dictionary<string, (MarketplaceRepo Repo, IReadOnlyList<MarketplaceFile> Files, DateTime At, string? Error)> Trees =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (string Payload, MarketplaceDetails Details)> Payloads = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Vom Benutzer auf- bzw. zugeklappte Ordner (sonst: oberste Ebene offen).</summary>
    private static readonly Dictionary<string, bool> FolderState = new(StringComparer.OrdinalIgnoreCase);
    private static string _tab = "Entry";
    private static string _sourceFilter = "*";
    private static readonly TimeSpan TreeLifetime = TimeSpan.FromMinutes(10);

    /// <param name="changed">Nach Installation/Deinstallation: Einstellungen übernehmen und Seiten neu lesen.</param>
    /// <param name="variables">Seite „Variablen“, über die fehlende Variablen angelegt werden (sonst eine eigene).</param>
    public MarketplaceViewModel(ISettingsHost app, AppSettings workingCopy, Action changed, VariablesViewModel? variables = null)
    {
        _app = app;
        _s = workingCopy;
        _changed = changed;
        _variables = variables;
        RefreshCommand = new RelayCommand(async () => await LoadAsync(force: true), () => !_loading);
        AddSourceCommand = new RelayCommand(async () => await AddSourceAsync(), () => !_loading);
        InstallSelectedCommand = new RelayCommand(async () => await InstallAsync(CheckedItems().Where(i => !i.IsInstalled || i.HasUpdate).ToList()),
            () => !_loading && CheckedItems().Any(i => !i.IsInstalled || i.HasUpdate));
        UninstallSelectedCommand = new RelayCommand(() => Uninstall(CheckedItems().Where(i => i.IsInstalled).ToList()),
            () => !_loading && CheckedItems().Any(i => i.IsInstalled));
        ClearSelectionCommand = new RelayCommand(() => { foreach (var i in AllItems) i.SetChecked(false); SelectionChanged(); });
        RebuildSources();
        if (Trees.Count > 0) Rebuild();
    }

    /// <summary>Zwischenspeicher leeren (Tests).</summary>
    internal static void ResetCache()
    {
        Trees.Clear();
        Payloads.Clear();
        FolderState.Clear();
        _tab = "Entry";
        _sourceFilter = "*";
    }

    internal AppSettings Settings => _s;
    internal ISettingsHost App => _app;

    public ObservableCollection<MarketplaceRow> Rows { get; } = new();
    /// <summary>Alle Treffer als Karten der Galerie (ohne Ordner; Reihenfolge wie in <see cref="Rows"/>).</summary>
    public ObservableCollection<MarketplaceItemRow> Cards { get; } = new();
    /// <summary>Kategorien (oberster Ordner) der aktuellen Liste – Filter in der Navigation.</summary>
    public ObservableCollection<MarketplaceCategory> Categories { get; } = new();

    private string _category = string.Empty;
    /// <summary>Gewählte Kategorie; leer = alle.</summary>
    public string Category
    {
        get => _category;
        set
        {
            CloseDetail(); // Kategorie in der Navigation gewählt: zurück zur Galerie
            if (_category == (value ?? string.Empty)) return;
            _category = value ?? string.Empty;
            OnPropertyChanged();
            Rebuild();
        }
    }

    /// <summary>„Entdecken“ bei Einträgen und Scripten (Designs haben ihren eigenen Bereich).</summary>
    public int AllCount => AllFiles().Count(f => f.File.Type != MarketplaceItemType.Design);
    public int EntryCount => Count(MarketplaceItemType.Entry);
    public int ScriptCount => Count(MarketplaceItemType.Script);
    public int DesignCount => Count(MarketplaceItemType.Design);
    public int InstalledCount => AllFiles().Count(f => f.File.Type != MarketplaceItemType.Design && Marketplace.FindInstall(_s, f.Source, f.File.Path) != null);
    public int DesignInstalledCount => AllFiles().Count(f => f.File.Type == MarketplaceItemType.Design && Marketplace.FindInstall(_s, f.Source, f.File.Path) != null);

    /// <summary>Bereich „Designs“ (Entdecken oder Meine Designs) statt Einträge und Scripte.</summary>
    public bool IsDesignArea => _tab is "Design" or "DesignInstalled";

    /// <summary>Passt die Datei zum Bereich?</summary>
    private bool InTab(string source, MarketplaceFile f) => _tab switch
    {
        "All" => f.Type != MarketplaceItemType.Design,
        "Installed" => f.Type != MarketplaceItemType.Design && Marketplace.FindInstall(_s, source, f.Path) != null,
        "DesignInstalled" => f.Type == MarketplaceItemType.Design && Marketplace.FindInstall(_s, source, f.Path) != null,
        _ => f.Type.ToString() == _tab
    };

    // ---- Detailseite --------------------------------------------------------------------

    private MarketplaceItemRow? _openItem;

    /// <summary>Auf der Detailseite geöffneter Eintrag; null = Galerie.</summary>
    public MarketplaceItemRow? OpenItem => _openItem;
    /// <summary>Statt der Galerie ist die Detailseite eines Eintrags zu sehen.</summary>
    public bool IsDetailOpen => _openItem != null;

    /// <summary>Zurück zur Galerie, z. B. „← Entdecken“ (Name des Bereichs, aus dem der Eintrag geöffnet wurde).</summary>
    public string BackText => "← " + (_tab switch
    {
        "Entry" => L.T("Entries"),
        "Script" => L.T("Scripts"),
        "Installed" => L.T("Installed"),
        "DesignInstalled" => L.T("My designs"),
        _ => L.T("Discover")
    });

    public ICommand BackCommand => _backCommand ??= new RelayCommand(CloseDetail, () => IsDetailOpen);
    private ICommand? _backCommand;

    /// <summary>Eintrag auf der Detailseite öffnen (Designs zeigen dort zuerst die Variante, in der die App gerade läuft).</summary>
    internal void Open(MarketplaceItemRow row)
    {
        if (ReferenceEquals(_openItem, row)) return;
        row.PrepareDetail(_app.Theme.IsDark);
        SetOpen(row);
    }

    /// <summary>Detailseite schließen: zurück zur Galerie.</summary>
    public void CloseDetail() => SetOpen(null);

    private void SetOpen(MarketplaceItemRow? row)
    {
        if (_openItem != null) _openItem.IsPreviewOpen = false;
        var wasOpen = _openItem != null;
        _openItem = row;
        if (row != null) row.IsPreviewOpen = true;
        OnPropertyChanged(nameof(OpenItem));
        if (wasOpen != (row != null)) OnPropertyChanged(nameof(IsDetailOpen));
        CommandRequery.Invalidate();
    }

    public ObservableCollection<MarketplaceSourceViewModel> SourceList { get; } = new();
    public ICommand RefreshCommand { get; }
    public ICommand AddSourceCommand { get; }
    public ICommand InstallSelectedCommand { get; }
    public ICommand UninstallSelectedCommand { get; }
    public ICommand ClearSelectionCommand { get; }

    public bool IsLoading { get => _loading; private set { if (Set(ref _loading, value)) { CommandRequery.Invalidate(); OnPropertyChanged(nameof(IsEmpty)); } } }
    public bool IsEmpty => !_loading && Rows.Count == 0 && _tab != "Sources";
    public bool HasRows => Rows.Count > 0;
    public bool HasCategories => Categories.Count > 0;
    /// <summary>Fehler (bleibt stehen, bis er behoben ist); Erfolg kommt als Snackbar.</summary>
    public string? Error { get => _error; private set => Set(ref _error, value); }

    public string Query { get => _query; set { if (Set(ref _query, value ?? string.Empty)) Rebuild(); } }

    /// <summary>Bereich: All (Entdecken), Entry, Script, Design, Installed oder Sources.</summary>
    public string Tab
    {
        get => _tab;
        set
        {
            if (_tab == value) return;
            CloseDetail();
            _tab = value ?? "Entry";
            _category = string.Empty; // Kategorien gehören zum Bereich
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackText));
            OnPropertyChanged(nameof(Category));
            OnPropertyChanged(nameof(IsSourcesPage));
            OnPropertyChanged(nameof(IsListPage));
            OnPropertyChanged(nameof(IsDesignArea));
            Rebuild();
        }
    }
    public bool IsSourcesPage => _tab == "Sources";
    public bool IsListPage => _tab != "Sources";

    /// <summary>Kurzinfo über der Liste, z. B. „12 Einträge in 4 Gruppen · 2 installiert“.</summary>
    public string SummaryText
    {
        get
        {
            var items = _items;
            var installed = items.Count(i => i.IsInstalled);
            if (IsDesignArea)
                return (items.Count == 1 ? L.T("1 design") : L.F("{0} designs", items.Count)) + (installed > 0 && _tab != "DesignInstalled" ? L.F(" · {0} installed", installed) : string.Empty);
            var groups = items.Select(i => i.Source + "|" + (_tab == "Installed" ? i.File.Folder : i.File.GroupFolder)).Distinct().Count();
            return L.F("{0} files in {1} groups", items.Count, groups) + (installed > 0 && _tab != "Installed" ? L.F(" · {0} installed", installed) : string.Empty);
        }
    }

    public string SourceFilter { get => _sourceFilter; set { if (_sourceFilter == value) return; _sourceFilter = value ?? "*"; OnPropertyChanged(); Rebuild(); } }
    public IReadOnlyList<KeyValuePair<string, string>> SourceChoices =>
        new[] { new KeyValuePair<string, string>("*", L.T("All sources")) }
            .Concat(Marketplace.Sources(_s).Select(s => new KeyValuePair<string, string>(s.Url, SourceName(s.Url)))).ToList();
    public bool HasSeveralSources => Marketplace.Sources(_s).Count > 1;

    public string EntryTabText => L.F("Entries ({0})", Count(MarketplaceItemType.Entry));
    public string ScriptTabText => L.F("Scripts ({0})", Count(MarketplaceItemType.Script));
    public string DesignTabText => L.F("Designs ({0})", Count(MarketplaceItemType.Design));
    public string InstalledTabText => L.F("Installed ({0})", AllFiles().Count(f => Marketplace.FindInstall(_s, f.Source, f.File.Path) != null))
                                      + (UpdateCount > 0 ? L.F(" · {0} Updates", UpdateCount) : string.Empty);
    private int UpdateCount => AllFiles().Count(f => Marketplace.HasUpdate(_s, f.Source, f.File));
    private int Count(MarketplaceItemType t) => AllFiles().Count(f => f.File.Type == t);

    public int CheckedCount => CheckedItems().Count();
    public bool HasSelection => CheckedCount > 0;
    public string SelectionText => CheckedCount == 1 ? L.T("1 selected") : L.F("{0} selected", CheckedCount);
    public string InstallSelectedText => L.F("Download ({0})", CheckedItems().Count(i => !i.IsInstalled || i.HasUpdate));
    public string UninstallSelectedText => L.F("Uninstall ({0})", CheckedItems().Count(i => i.IsInstalled));

    private readonly List<MarketplaceItemRow> _items = new();
    internal IEnumerable<MarketplaceItemRow> AllItems => _items;
    private IEnumerable<MarketplaceItemRow> CheckedItems() => _items.Where(i => i.IsChecked);

    private IEnumerable<(string Source, MarketplaceRepo Repo, MarketplaceFile File)> AllFiles() =>
        Marketplace.Sources(_s).SelectMany(src => Trees.TryGetValue(Key(src.Url), out var t)
            ? t.Files.Select(f => (src.Url, t.Repo, f)) : Enumerable.Empty<(string, MarketplaceRepo, MarketplaceFile)>());

    private static string Key(string url) => url.Trim().TrimEnd('/');

    internal string SourceName(string url) =>
        Marketplace.IsDefault(url) ? "Power-Exec" : _s.MarketplaceSources.FirstOrDefault(x => Marketplace.SameSource(x.Url, url)) is { Name.Length: > 0 } x ? x.Name : Marketplace.NameFromUrl(url);

    // ---- Laden ---------------------------------------------------------------------

    public async Task EnsureLoadedAsync()
    {
        if (!_loading && Marketplace.Sources(_s).Any(s => !Trees.TryGetValue(Key(s.Url), out var t) || DateTime.UtcNow - t.At > TreeLifetime))
            await LoadAsync(force: false);
    }

    /// <summary>Dateilisten aller Quellen laden (zwischengespeichert), dann die Dateien selbst für Name und Beschreibung.</summary>
    public async Task LoadAsync(bool force)
    {
        IsLoading = true;
        try
        {
            await Task.WhenAll(Marketplace.Sources(_s).Select(s => LoadTreeAsync(s.Url, force)));
            Rebuild();
            await Task.WhenAll(AllFiles().Where(f => !Payloads.ContainsKey(PayloadKey(f.Source, f.File)))
                .Select(f => LoadPayloadAsync(f.Source, f.Repo, f.File)).ToList());
            var errors = Marketplace.Sources(_s).Select(s => Trees.TryGetValue(Key(s.Url), out var t) && t.Error != null ? $"{SourceName(s.Url)}: {t.Error}" : null).OfType<string>().ToList();
            Error = errors.Count > 0 ? string.Join("\n", errors) : null;
        }
        finally
        {
            IsLoading = false;
            RebuildSources();
            Rebuild();
        }
    }

    private async Task LoadTreeAsync(string source, bool force)
    {
        var key = Key(source);
        if (!force && Trees.TryGetValue(key, out var cached) && cached.Error == null && DateTime.UtcNow - cached.At < TreeLifetime) return;
        MarketplaceRepo repo;
        try { repo = Marketplace.ParseSource(source); }
        catch (Exception ex) { Trees[key] = (new MarketplaceRepo(true, "", "", "", ""), Array.Empty<MarketplaceFile>(), DateTime.UtcNow, ex.Message); return; }
        try
        {
            var files = new List<MarketplaceFile>();
            if (repo.IsGitHub) files.AddRange(Marketplace.ParseTree(await _app.DownloadTextAsync(repo.TreeUri()), repo));
            else
                for (int page = 1; page <= 20; page++)
                {
                    var text = await _app.DownloadTextAsync(repo.TreeUri(page));
                    var part = Marketplace.ParseTree(text, repo with { Folder = string.Empty });
                    int json;
                    using (var doc = System.Text.Json.JsonDocument.Parse(text)) json = doc.RootElement.GetArrayLength();
                    files.AddRange(repo.Folder.Length > 0
                        ? part.Where(f => f.Path.StartsWith(repo.Folder + "/", StringComparison.Ordinal)).Select(f => f with { Path = f.Path[(repo.Folder.Length + 1)..] })
                        : part);
                    if (json < 100) break;
                }
            Trees[key] = (repo, files, DateTime.UtcNow, null);
        }
        catch (Exception ex)
        {
            // Abruflimit oder Netzwerkfehler: letzten Stand weiter zeigen
            var old = Trees.TryGetValue(key, out var o) ? o.Files : Array.Empty<MarketplaceFile>();
            Trees[key] = (repo, old, DateTime.UtcNow, ex.Message.Contains("403") ? L.T("GitHub rate limit reached – please reload later.") : ex.Message);
        }
    }

    private static string PayloadKey(string source, MarketplaceFile f) => $"{Key(source)}|{f.Path}|{f.Sha}";

    private async Task<(string Payload, MarketplaceDetails Details)?> LoadPayloadAsync(string source, MarketplaceRepo repo, MarketplaceFile file)
    {
        var key = PayloadKey(source, file);
        if (Payloads.TryGetValue(key, out var p)) return p;
        try
        {
            var payload = await _app.DownloadTextAsync(repo.RawUri(file.Path));
            var result = (payload, Marketplace.Describe(file, payload));
            Payloads[key] = result;
            foreach (var row in _items.Where(r => r.Matches(source, file))) row.SetDetails(result.Item2);
            return result;
        }
        catch { return null; }
    }

    // ---- Liste aufbauen ----------------------------------------------------------------

    /// <summary>Ordnerbaum für Tab, Quelle und Suche als flache Zeilenliste (Ordner mit Einrückung, aufklappbar).</summary>
    internal void Rebuild()
    {
        var checkedKeys = _items.Where(i => i.IsChecked).Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var open = _openItem;
        _items.Clear();
        Rows.Clear();
        var categories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var terms = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sources = Marketplace.Sources(_s).Where(s => _sourceFilter == "*" || Marketplace.SameSource(s.Url, _sourceFilter)).ToList();
        var bySource = sources.Count > 1;
        foreach (var src in sources)
        {
            if (!Trees.TryGetValue(Key(src.Url), out var tree)) continue;
            if (_tab == "Sources") continue;
            var files = tree.Files.Where(f => InTab(src.Url, f)).ToList();
            var items = files.Select(f => new MarketplaceItemRow(this, src.Url, tree.Repo, f, Payloads.TryGetValue(PayloadKey(src.Url, f), out var p) ? p.Details : null))
                .Where(i => terms.All(t => i.SearchText.Contains(t, StringComparison.CurrentCultureIgnoreCase))).ToList();
            foreach (var i in items.Where(i => i.CategoryName.Length > 0))
                categories[i.CategoryName] = categories.TryGetValue(i.CategoryName, out var n) ? n + 1 : 1;
            if (_category.Length > 0) items = items.Where(i => i.CategoryName.Equals(_category, StringComparison.OrdinalIgnoreCase)).ToList();
            if (items.Count == 0) continue;
            foreach (var i in items)
                if (checkedKeys.Contains(i.Id)) i.SetChecked(true);
            _items.AddRange(items);
            var root = new MarketplaceFolderRow(this, Key(src.Url) + "|", SourceName(src.Url), 0, isSource: true);
            foreach (var item in items)
            {
                var parent = root;
                var path = string.Empty;
                foreach (var part in (_tab is "Installed" or "DesignInstalled" ? item.File.Folder : item.File.GroupFolder).Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    path = path.Length == 0 ? part : path + "/" + part;
                    parent = parent.Folder(Key(src.Url) + "|" + path, part);
                }
                parent.Children.Add(item);
            }
            if (bySource) AddRows(root, 0, terms.Length > 0);
            else foreach (var child in root.Children) AddRow(child, 0, terms.Length > 0);
        }
        Cards.Clear();
        foreach (var i in _items) Cards.Add(i);
        // Offene Detailseite bleibt: mit der neuen Zeile (neuer Stand nach Installieren, Anwenden …), sonst mit der alten
        if (open != null)
        {
            if (_items.FirstOrDefault(i => i.Id == open.Id) is { } fresh && !ReferenceEquals(fresh, open))
            {
                fresh.TakeDetailState(open);
                SetOpen(fresh);
            }
            else open.RaiseState();
        }
        Categories.Clear();
        foreach (var c in categories.OrderBy(c => c.Key, StringComparer.CurrentCultureIgnoreCase))
            Categories.Add(new MarketplaceCategory(this, c.Key, c.Value, c.Key.Equals(_category, StringComparison.OrdinalIgnoreCase)));
        foreach (var p in new[] { nameof(IsEmpty), nameof(EntryTabText), nameof(ScriptTabText), nameof(DesignTabText), nameof(InstalledTabText), nameof(SummaryText), nameof(HasRows),
                     nameof(AllCount), nameof(EntryCount), nameof(ScriptCount), nameof(DesignCount), nameof(InstalledCount), nameof(DesignInstalledCount), nameof(HasCategories) }) OnPropertyChanged(p);
        SelectionChanged();
    }

    private void AddRow(MarketplaceRow row, int depth, bool expandAll)
    {
        if (row is MarketplaceFolderRow folder) AddRows(folder, depth, expandAll);
        else { row.Depth = depth; Rows.Add(row); }
    }

    private void AddRows(MarketplaceFolderRow folder, int depth, bool expandAll)
    {
        folder.Depth = depth;
        folder.SetExpanded(expandAll || (FolderState.TryGetValue(folder.Id, out var open) ? open : depth == 0), rebuild: false);
        Rows.Add(folder);
        if (!folder.IsExpanded) return;
        foreach (var child in folder.Children) AddRow(child, depth + 1, expandAll);
    }

    internal void ToggleFolder(MarketplaceFolderRow folder)
    {
        FolderState[folder.Id] = !folder.IsExpanded;
        Rebuild();
    }

    internal void SelectionChanged()
    {
        foreach (var p in new[] { nameof(CheckedCount), nameof(HasSelection), nameof(SelectionText), nameof(InstallSelectedText), nameof(UninstallSelectedText) })
            OnPropertyChanged(p);
        foreach (var f in Rows.OfType<MarketplaceFolderRow>()) f.RaiseChecked();
        CommandRequery.Invalidate();
    }

    // ---- Quellen -------------------------------------------------------------------

    private void RebuildSources()
    {
        SourceList.Clear();
        foreach (var s in Marketplace.Sources(_s))
        {
            Trees.TryGetValue(Key(s.Url), out var t);
            SourceList.Add(new MarketplaceSourceViewModel(this, s, SourceName(s.Url), t.Files?.Count ?? 0, t.Error));
        }
        OnPropertyChanged(nameof(SourceChoices));
        OnPropertyChanged(nameof(HasSeveralSources));
    }

    private async Task AddSourceAsync()
    {
        var url = _app.Dialogs.Ask(L.T("Add source"),
            L.T("Address of a public repository (GitHub or GitLab), e.g. https://github.com/company/px-catalog\nA subfolder works too: …/tree/main/marketplace. For the layout see marketplace/README.md in the Power-Exec repository."),
            string.Empty, L.T("Add"), u => Marketplace.ValidateSource(_s, u));
        if (url == null) return;
        url = Key(url);
        IsLoading = true;
        try
        {
            await LoadTreeAsync(url, force: true);
            var t = Trees[url];
            if (t.Error != null) { Error = L.T("Source not added: ") + t.Error; Trees.Remove(url); return; }
            _s.MarketplaceSources.Add(new MarketplaceSource { Url = url, Name = Marketplace.NameFromUrl(url), AddedUtc = DateTime.UtcNow });
            Error = null;
            Snackbar.Show(L.F("Source “{0}” with {1} files added. Before the first installation, Power-Exec asks whether you trust it.", Marketplace.NameFromUrl(url), t.Files.Count));
            _changed();
        }
        finally
        {
            IsLoading = false;
            RebuildSources();
            Rebuild();
        }
        await LoadAsync(force: false);
    }

    internal void RemoveSource(MarketplaceSourceViewModel source)
    {
        if (source.IsDefault) return;
        if (!_app.Dialogs.Confirm(L.F("Remove source “{0}”? Entries, designs and scripts already installed are kept.", source.Name),
                L.T("Remove source"), owner: _app.SettingsDialogOwner)) return;
        _s.MarketplaceSources.RemoveAll(x => Marketplace.SameSource(x.Url, source.Url));
        Trees.Remove(Key(source.Url));
        if (Marketplace.SameSource(_sourceFilter, source.Url)) _sourceFilter = "*";
        RebuildSources();
        Rebuild();
        Snackbar.Show(L.F("Source “{0}” removed.", source.Name));
        _changed();
    }

    // ---- Installieren und deinstallieren ---------------------------------------------------

    /// <summary>Fremde Quellen: einmal bestätigen, dass ihre Inhalte installiert werden dürfen.</summary>
    private bool EnsureTrusted(IEnumerable<string> sources)
    {
        foreach (var src in sources.Distinct(StringComparer.OrdinalIgnoreCase).Where(x => !Marketplace.IsTrusted(_s, x)))
        {
            if (!_app.Dialogs.Confirm(
                    L.F("“{0}” is a third-party source:\n{1}\n\n", SourceName(src), src) +
                    L.T("Entries, commands and scripts from it can start programs and run code on this PC. Only trust sources that you or your team control.\n\nTrust this source?"),
                    L.T("Trust source?"), destructive: true, owner: _app.SettingsDialogOwner, defaultNo: true))
                return false;
            foreach (var s in _s.MarketplaceSources.Where(x => Marketplace.SameSource(x.Url, src))) s.Trusted = true;
        }
        return true;
    }

    internal Task InstallOneAsync(MarketplaceItemRow row) => InstallAsync(new List<MarketplaceItemRow> { row });

    /// <summary>Design herunterladen (falls nötig) und gleich anwenden – ohne Rückfrage.</summary>
    internal async Task InstallAndApplyAsync(MarketplaceItemRow row)
    {
        if (row.IsInstalled && !row.HasUpdate)
        {
            if (InstalledDesign(row) is { } design) ApplyInstalled(design);
            return;
        }
        await InstallAsync(new List<MarketplaceItemRow> { row }, applyDesign: true);
    }

    private DesignPreset? InstalledDesign(MarketplaceItemRow row) =>
        Marketplace.FindInstall(_s, row.Source, row.File.Path) is { DesignKey.Length: > 0 } installed
            ? _s.CustomDesigns.FirstOrDefault(d => d.Key == installed.DesignKey) : null;

    private void ApplyInstalled(DesignPreset design)
    {
        _tryBefore = null; // ausprobiert und übernommen: kein Zurück mehr
        RaiseTry();
        Appearance.Apply(_s, design);
        Snackbar.Show(L.F("“{0}” is now active.", design.DisplayName));
        _changed();
        Rebuild();
    }

    // ---- Ausprobieren ------------------------------------------------------------------

    /// <summary>Stand vor dem Ausprobieren (null = es wird nichts ausprobiert).</summary>
    private AppSettings? _tryBefore;
    private MarketplaceItemRow? _tryRow;

    public bool IsTrying => _tryBefore != null;
    public string TryText => _tryRow == null ? string.Empty
        : L.F("“{0}” is being tried out – the panel and all windows show the design. Without “Keep”, Power-Exec goes back to “{1}”.",
            _tryRow.Name, _tryBefore is { } b && Appearance.Active(b) is { } d ? d.DisplayName : L.T("the previous look"));
    public ICommand KeepTryCommand => _keepTry ??= new RelayCommand(async () => { if (_tryRow != null) await InstallAndApplyAsync(_tryRow); }, () => IsTrying && !_loading);
    public ICommand RevertTryCommand => _revertTry ??= new RelayCommand(EndTry, () => IsTrying);
    private ICommand? _keepTry, _revertTry;

    /// <summary>Design vorübergehend anwenden, ohne es herunterzuladen – bis „Übernehmen“, „Zurück“ oder dem Schließen.</summary>
    internal void TryDesign(MarketplaceItemRow row)
    {
        if (row.DesignPreset is not { } design) return;
        _tryBefore ??= SettingsStore.Clone(_s);
        _tryRow = row;
        Appearance.Apply(_s, design);
        _changed();
        RaiseTry();
    }

    /// <summary>Ausprobieren beenden: zurück zum vorherigen Aussehen.</summary>
    public void EndTry()
    {
        if (_tryBefore is not { } before) return;
        _s.DesignKey = before.DesignKey;
        _s.Theme = before.Theme;
        _s.UseWindowsAccent = before.UseWindowsAccent;
        _s.Corners = before.Corners;
        _s.Look = before.Look.Clone();
        for (int i = 0; i < _s.Panels.Count && i < before.Panels.Count; i++)
        {
            var (p, b) = (_s.Panels[i], before.Panels[i]);
            (p.PanelColor, p.PanelOpacity, p.Backdrop, p.Frost, p.AccentColor, p.GlassBlur, p.GlassSaturation, p.GroupStyle) =
                (b.PanelColor, b.PanelOpacity, b.Backdrop, b.Frost, b.AccentColor, b.GlassBlur, b.GlassSaturation, b.GroupStyle);
        }
        _tryBefore = null;
        _tryRow = null;
        _changed();
        RaiseTry();
    }

    private void RaiseTry()
    {
        if (_tryBefore == null) _tryRow = null;
        foreach (var p in new[] { nameof(IsTrying), nameof(TryText) }) OnPropertyChanged(p);
        foreach (var i in _items) i.RaiseTrying();
        if (_openItem != null && !_items.Contains(_openItem)) _openItem.RaiseTrying();
        CommandRequery.Invalidate();
    }

    internal bool IsTryingRow(MarketplaceItemRow row) => _tryRow != null && _tryRow.Id == row.Id;

    internal Task InstallFolderAsync(MarketplaceFolderRow folder) =>
        InstallAsync(folder.AllItems().Where(i => !i.IsInstalled || i.HasUpdate).ToList());

    /// <summary>Vertrauen, Vorschau, fehlende Variablen, Gruppe je Ordner – dann alles installieren.</summary>
    internal async Task InstallAsync(IReadOnlyList<MarketplaceItemRow> rows, bool applyDesign = false)
    {
        if (rows.Count == 0 || !EnsureTrusted(rows.Select(r => r.Source))) return;
        IsLoading = true;
        try
        {
            var loaded = new List<(MarketplaceItemRow Row, string Payload, MarketplaceDetails Details)>();
            foreach (var r in rows)
            {
                var p = await LoadPayloadAsync(r.Source, r.Repo, r.File);
                if (p == null) { Error = L.F("“{0}” couldn't be loaded.", r.Name); return; }
                loaded.Add((r, p.Value.Payload, p.Value.Details));
            }
            var preview = string.Join("\n\n", loaded.Take(8).Select(l => $"• {l.Details.Name} ({l.Row.TypeText}, {SourceName(l.Row.Source)})\n  {l.Details.Preview.Replace("\n", "\n  ")}"))
                          + (loaded.Count > 8 ? L.F("\n\n… and {0} more", loaded.Count - 8) : string.Empty);
            if (preview.Length > 2500) preview = preview[..2500] + "\n…";
            var heading = loaded.Count == 1 ? L.F("Download “{0}”?", loaded[0].Details.Name) : L.F("Download {0} items?", loaded.Count);
            // Designs zeigen ihre Vorschau schon auf der Karte und ändern nichts, bevor man sie anwendet: keine Rückfrage
            var onlyDesigns = loaded.All(l => l.Row.File.Type == MarketplaceItemType.Design);
            if (!onlyDesigns && !_app.Dialogs.Confirm($"{heading}\n\n{preview}", L.T("Marketplace – preview"), owner: _app.SettingsDialogOwner)) return;

            foreach (var v in Marketplace.MissingVariables(_s, loaded.SelectMany(l => l.Details.Variables)))
            {
                // Variablen-Dialog mit vorgegebener Art (Text, geheim, Umgebungsvariable, PowerShell …) und Vorschlag
                _variables ??= new VariablesViewModel(_app, _s);
                if (!_variables.AddFor(v.Name, v.Kind, v.Description, v.Example)) return;
            }

            // Gruppe je Ordner abfragen (Updates bleiben in ihrer Gruppe, Designs brauchen keine)
            var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in loaded.Where(l => l.Row.File.Type != MarketplaceItemType.Design && !l.Row.IsInstalled)
                         .Select(l => l.Row.File.GroupFolder).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var suggestion = folder.Length > 0 ? folder.Split('/')[^1] : L.T("Marketplace");
                var count = loaded.Count(l => l.Row.File.GroupFolder == folder && l.Row.File.Type != MarketplaceItemType.Design);
                var group = _app.Dialogs.PickGroup(L.T("Into which group?"),
                    L.F("{0} from “{1}” are added to your custom entries in this group.",
                        count == 1 ? L.T("1 entry") : L.F("{0} entries", count), folder.Length > 0 ? folder : L.T("top level")),
                    GroupManager.All(_s).ToList(), suggestion, _app.SettingsDialogOwner);
                if (group == null) return;
                groups[folder] = group;
            }

            int entries = 0, scripts = 0, designs = 0;
            foreach (var (row, payload, _) in loaded)
            {
                var record = Marketplace.FindInstall(_s, row.Source, row.File.Path);
                var group = record != null ? Marketplace.InstalledGroup(_s, record) ?? string.Empty : groups.GetValueOrDefault(row.File.GroupFolder, string.Empty);
                Marketplace.Install(_s, row.Source, row.File, payload, group, _app.MarketplaceScriptFolder);
                switch (row.File.Type)
                {
                    case MarketplaceItemType.Entry: entries++; break;
                    case MarketplaceItemType.Script: scripts++; break;
                    default: designs++; break;
                }
            }
            Error = null;
            // Genau ein Design geladen: gleich anwenden („Installieren und anwenden“) bzw. fragen (Mehrere: Auswahl unter Darstellung)
            var newDesigns = loaded.Where(l => l.Row.File.Type == MarketplaceItemType.Design).ToList();
            if (newDesigns.Count == 1 && InstalledDesign(newDesigns[0].Row) is { } design
                && (applyDesign || _s.DesignKey == design.Key
                    || _app.Dialogs.Confirm(L.F("Apply “{0}” now?", design.DisplayName), L.T("Design downloaded"), owner: _app.SettingsDialogOwner)))
            {
                foreach (var i in _items) i.SetChecked(false);
                ApplyInstalled(design);
                return;
            }
            Snackbar.Show(L.T("Downloaded: ") + string.Join(", ", new[]
            {
                entries > 0 ? (entries == 1 ? L.T("1 entry") : L.F("{0} entries", entries)) : null,
                scripts > 0 ? (scripts == 1 ? L.T("1 script") : L.F("{0} scripts", scripts)) : null,
                designs > 0 ? (designs == 1 ? L.T("1 design (under My designs)") : L.F("{0} designs (under My designs)", designs)) : null
            }.OfType<string>()) + (entries + scripts > 0 ? L.T(" – find them under Custom entries.") : "."));
            foreach (var i in _items) i.SetChecked(false);
            _changed();
        }
        catch (Exception ex)
        {
            Error = L.T("Download failed: ") + ex.Message;
        }
        finally
        {
            IsLoading = false;
            Rebuild();
        }
    }

    /// <summary>Deinstallieren; selbst geänderte Einträge nur nach Rückfrage.</summary>
    internal void Uninstall(IReadOnlyList<MarketplaceItemRow> rows)
    {
        rows = rows.Where(r => r.IsInstalled).ToList();
        if (rows.Count == 0) return;
        if (!_app.Dialogs.Confirm(rows.Count == 1 ? L.F("Uninstall “{0}”?", rows[0].Name) : L.F("Uninstall {0} items?", rows.Count),
                L.T("Marketplace – uninstall"), destructive: true, owner: _app.SettingsDialogOwner)) return;
        int removed = 0, kept = 0;
        foreach (var row in rows)
        {
            if (Marketplace.FindInstall(_s, row.Source, row.File.Path) is not { } record) continue;
            if (Marketplace.IsModified(_s, record) &&
                !_app.Dialogs.Confirm(L.F("“{0}” was changed after downloading. Remove it anyway?", row.Name), L.T("Changed entry"),
                    destructive: true, owner: _app.SettingsDialogOwner, defaultNo: true))
            {
                kept++;
                continue;
            }
            Marketplace.Uninstall(_s, record);
            removed++;
        }
        foreach (var i in _items) i.SetChecked(false);
        if (removed > 0) Snackbar.Show((removed == 1 ? L.T("1 item") : L.F("{0} items", removed)) + L.T(" uninstalled") + (kept > 0 ? L.F(", {0} changed ones kept.", kept) : "."));
        Rebuild();
        if (removed > 0) _changed();
    }
}

/// <summary>Zeile der Marktplatz-Liste: Ordner oder Datei.</summary>
public abstract class MarketplaceRow : ObservableObject
{
    private int _depth;
    public int Depth { get => _depth; set { if (Set(ref _depth, value)) OnPropertyChanged(nameof(Indent)); } }
    /// <summary>Einrückung in Pixeln (je Ebene 20).</summary>
    public double Indent => _depth * 20;
    public abstract string Id { get; }
    public abstract bool IsFolder { get; }
    public abstract string Name { get; }
}

/// <summary>Ordner (oder Quelle) mit seinen Dateien und Unterordnern.</summary>
public sealed class MarketplaceFolderRow : MarketplaceRow
{
    private readonly MarketplaceViewModel _page;
    private readonly string _id;
    private readonly string _name;
    private bool _expanded;

    public MarketplaceFolderRow(MarketplaceViewModel page, string id, string name, int depth, bool isSource = false)
    {
        _page = page;
        _id = id;
        _name = name;
        Depth = depth;
        IsSource = isSource;
        ToggleCommand = new RelayCommand(() => _page.ToggleFolder(this));
        InstallCommand = new RelayCommand(async () => await _page.InstallFolderAsync(this), () => AllItems().Any(i => !i.IsInstalled || i.HasUpdate));
    }

    public override string Id => _id;
    public override bool IsFolder => true;
    public override string Name => _name;
    public bool IsSource { get; }
    public List<MarketplaceRow> Children { get; } = new();
    public bool IsExpanded => _expanded;
    public ICommand ToggleCommand { get; }
    /// <summary>Ganzen Ordner herunterladen.</summary>
    public ICommand InstallCommand { get; }
    public string Glyph => IsSource ? "" : _expanded ? "" : "";

    public string CountText
    {
        get
        {
            var items = AllItems().ToList();
            var installed = items.Count(i => i.IsInstalled);
            return items.Count + (items.Count == 1 ? L.T(" file") : L.T(" files")) + (installed > 0 ? L.F(" · {0} installed", installed) : string.Empty);
        }
    }

    public IEnumerable<MarketplaceItemRow> AllItems() =>
        Children.SelectMany(c => c is MarketplaceFolderRow f ? f.AllItems() : new[] { (MarketplaceItemRow)c });

    internal MarketplaceFolderRow Folder(string id, string name)
    {
        if (Children.OfType<MarketplaceFolderRow>().FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } f) return f;
        var folder = new MarketplaceFolderRow(_page, id, name, Depth + 1);
        // Unterordner vor den Dateien
        Children.Insert(Children.Count(c => c is MarketplaceFolderRow), folder);
        return folder;
    }

    internal void SetExpanded(bool value, bool rebuild)
    {
        _expanded = value;
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(Glyph));
    }

    /// <summary>Häkchen: alle Dateien des Ordners; teilweise = null.</summary>
    public bool? IsChecked
    {
        get
        {
            var items = AllItems().ToList();
            return items.Count == 0 || items.All(i => !i.IsChecked) ? false : items.All(i => i.IsChecked) ? true : null;
        }
        set
        {
            var check = value != false;
            foreach (var i in AllItems()) i.SetChecked(check);
            _page.SelectionChanged();
        }
    }

    internal void RaiseChecked() => OnPropertyChanged(nameof(IsChecked));
}

/// <summary>Eine Datei (Eintrag, Script, Design) im Marktplatz.</summary>
public sealed class MarketplaceItemRow : MarketplaceRow
{
    private readonly MarketplaceViewModel _page;
    private MarketplaceDetails? _details;
    private bool _checked;

    public MarketplaceItemRow(MarketplaceViewModel page, string source, MarketplaceRepo repo, MarketplaceFile file, MarketplaceDetails? details)
    {
        _page = page;
        Source = source;
        Repo = repo;
        File = file;
        _details = details;
        InstallCommand = new RelayCommand(async () => await _page.InstallOneAsync(this));
        UninstallCommand = new RelayCommand(() => _page.Uninstall(new[] { this }), () => IsInstalled);
    }

    public string Source { get; }
    public MarketplaceRepo Repo { get; }
    public MarketplaceFile File { get; }
    public override string Id => Source.TrimEnd('/') + "|" + File.Path;
    public override bool IsFolder => false;
    public override string Name => _details?.Name ?? File.DefaultName;
    public string Description => _details?.Description ?? string.Empty;
    /// <summary>
    /// Kategorie: bei Designs die aus der Datei (übersetzt), sonst der oberste Ordner der Gruppe („Links“); leer, wenn es
    /// keine gibt.
    /// </summary>
    public string CategoryName => IsDesignFile
        ? _details?.Design?.DisplayCategory ?? string.Empty
        : File.GroupFolder.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    /// <summary>Fußzeile der Karte: Kategorie, bei mehreren Quellen mit Quelle.</summary>
    public string CategoryText => string.Join(" · ", new[] { CategoryName, _page.HasSeveralSources ? _page.SourceName(Source) : string.Empty }.Where(t => t.Length > 0));
    /// <summary>Erste Zeile der Vorschau – das, was der Eintrag ausführt (Kopf der Karte).</summary>
    public string PreviewLine => (_details?.Preview ?? string.Empty).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
    /// <summary>Farbe des Kartenkopfs: je Kategorie gleich bleibend, dunkel genug für weiße Schrift.</summary>
    public string BandColor
    {
        get
        {
            string[] palette = { "#1F4E8C", "#8A4A12", "#0B5C5C", "#4A3A8A", "#7A2E4A", "#2E5E2E" };
            var key = CategoryName.Length > 0 ? CategoryName : TypeText;
            return palette[key.Sum(c => char.ToLowerInvariant(c)) % palette.Length];
        }
    }
    public string TypeText => File.Type switch { MarketplaceItemType.Script => L.T("Script"), MarketplaceItemType.Design => L.T("Design"), _ => L.T("Entry") };
    public string Glyph => File.Type switch { MarketplaceItemType.Script => "", MarketplaceItemType.Design => "", _ => "" };
    public string Needs => _details?.Variables.Count > 0 ? L.T("needs ") + string.Join(", ", _details.Variables.Select(v => "{{$" + Variables.Normalize(v.Name) + "}}")) : string.Empty;
    internal string SearchText => $"{Name} {Description} {File.Path} {TypeText} {CategoryName}";

    public bool IsInstalled => Marketplace.FindInstall(_page.Settings, Source, File.Path) != null;
    public bool HasUpdate => Marketplace.HasUpdate(_page.Settings, Source, File);
    public string StateText => HasUpdate ? L.T("Update available") : IsInstalled ? L.T("✓ Installed") : string.Empty;
    public string InstallText => HasUpdate ? L.T("Update") : L.T("Download");
    public bool CanInstall => !IsInstalled || HasUpdate;
    /// <summary>Detailseite: „Herunterladen“ bzw. „Aktualisieren“ (Designs haben „Installieren und anwenden“).</summary>
    public bool ShowInstall => IsEntryOrScript && CanInstall;

    public ICommand InstallCommand { get; }
    public ICommand UninstallCommand { get; }

    public bool IsChecked { get => _checked; set { if (Set(ref _checked, value)) _page.SelectionChanged(); } }
    internal void SetChecked(bool value)
    {
        _checked = value;
        OnPropertyChanged(nameof(IsChecked));
    }

    internal bool Matches(string source, MarketplaceFile file) => Marketplace.SameSource(source, Source) && file.Path == File.Path;

    internal void SetDetails(MarketplaceDetails details)
    {
        _details = details;
        _colorRows = null;
        _groupRows = null;
        RaiseState();
    }

    /// <summary>Alles neu lesen (Stand nach Installieren, Anwenden, Ausprobieren).</summary>
    internal void RaiseState() => OnPropertyChanged(string.Empty);

    // ---- Detailseite -------------------------------------------------------------------

    private bool _previewOpen;
    /// <summary>Auf der Detailseite geöffnet.</summary>
    public bool IsPreviewOpen { get => _previewOpen; set => Set(ref _previewOpen, value); }
    /// <summary>Detailseite öffnen (Klick auf die Karte).</summary>
    public ICommand OpenCommand => _openCommand ??= new RelayCommand(() => _page.Open(this));
    private ICommand? _openCommand;

    /// <summary>Kopfzeile der Detailseite: „Beschreibung · Kategorie · von Quelle“.</summary>
    public string MetaText => string.Join(" · ", new[] { Description, CategoryName, L.F("by {0}", _page.SourceName(Source)) }.Where(t => t.Length > 0));

    /// <summary>Vor dem Öffnen: ohne eigene Wahl auf der Karte die Variante zeigen, in der die App gerade läuft.</summary>
    internal void PrepareDetail(bool appIsDark)
    {
        if (!_variantChosen && HasBothVariants) ShowDark = appIsDark;
    }

    /// <summary>Nach dem Neuaufbau der Liste: gewählte Variante der bisher geöffneten Zeile übernehmen.</summary>
    internal void TakeDetailState(MarketplaceItemRow previous)
    {
        _variantChosen = previous._variantChosen;
        ShowDark = previous._showDark;
    }

    public bool IsDesignFile => File.Type == MarketplaceItemType.Design;
    public bool IsEntryOrScript => File.Type != MarketplaceItemType.Design;

    /// <summary>Ziel, Befehl oder Code (Einträge) bzw. Anfang des Scripts.</summary>
    public string PreviewText => _details?.Preview ?? L.T("Loading …");
    public string EntryKindText => _details?.Entry is { } e ? CustomEntry.KindLabel(e.Kind) : TypeText;
    public bool HasVariables => _details?.Variables.Count > 0;
    /// <summary>Benötigte Variablen mit ihrer Art, je Zeile.</summary>
    public string VariablesText => _details == null ? string.Empty : string.Join("\n", _details.Variables.Select(v =>
        "{{$" + Variables.Normalize(v.Name) + "}} · " + Variables.Label(v.Kind) + (v.Description.Length > 0 ? " – " + v.Description : string.Empty)));

    public bool HasDesign => _details?.Design != null;
    /// <summary>Design für die gemeinsame Vorschau (wie unter Darstellung).</summary>
    public DesignPreset? DesignPreset => _details?.Design;

    // ---- Design: helle bzw. dunkle Variante (Karte und Detailseite) ------------------------

    private bool? _showDark;
    private bool _variantChosen;

    /// <summary>Gezeigte Variante: dunkel, hell oder – null – die Hauptvariante des Designs.</summary>
    public bool? ShowDark
    {
        get => _showDark;
        private set
        {
            if (!Set(ref _showDark, value)) return;
            foreach (var p in new[] { nameof(ShowsDark), nameof(IsPreviewDark), nameof(IsPreviewLight), nameof(GroupSwatches), nameof(PreviewSurface),
                         nameof(PreviewAccent), nameof(PreviewLineColor), nameof(PreviewInk) })
                OnPropertyChanged(p);
            MarkActiveRows();
        }
    }

    /// <summary>Zeigt die Vorschau gerade die dunkle Variante? Designs mit nur einer Variante zeigen immer diese.</summary>
    public bool ShowsDark => HasBothVariants ? _showDark ?? DesignPreset?.PrimaryIsDark ?? true : DesignPreset?.PrimaryIsDark ?? true;
    public bool HasBothVariants => DesignPreset?.HasBothVariants == true;
    public ICommand ShowDarkCommand => _showDarkCommand ??= new RelayCommand(() => ChooseVariant(true), () => HasBothVariants);
    public ICommand ShowLightCommand => _showLightCommand ??= new RelayCommand(() => ChooseVariant(false), () => HasBothVariants);
    private ICommand? _showDarkCommand, _showLightCommand;

    /// <summary>Umschalter „☾ Dunkel“ der Detailseite (nur Designs mit beiden Varianten).</summary>
    public bool IsPreviewDark { get => ShowsDark; set { if (value) ChooseVariant(true); } }
    /// <summary>Umschalter „☀ Hell“ der Detailseite.</summary>
    public bool IsPreviewLight { get => !ShowsDark; set { if (value) ChooseVariant(false); } }

    /// <summary>Hinweis am Umschalter, wenn das Design nur eine Variante hat.</summary>
    public string VariantHint => HasBothVariants || DesignPreset is not { } d ? string.Empty
        : d.PrimaryIsDark ? L.T("This design only has a dark variant") : L.T("This design only has a light variant");

    private void ChooseVariant(bool dark)
    {
        if (!HasBothVariants) return;
        _variantChosen = true;
        ShowDark = dark;
    }

    /// <summary>Farbreihe der Gruppen in der gezeigten Variante (Farbstreifen unter dem Namen).</summary>
    public IReadOnlyList<DesignSwatch> GroupSwatches => DesignPreset is { } d ? GroupColorsOf(d, ShowsDark).Select((h, i) => new DesignSwatch(ColorChoices.DesignColorName(i), h)).ToList() : Array.Empty<DesignSwatch>();

    private static IReadOnlyList<string> GroupColorsOf(DesignPreset d, bool dark) =>
        d.Pair != null && dark != d.PrimaryIsDark && Appearance.SplitColors(d.Pair.GroupColors) is { Count: > 0 } other ? other : Appearance.SplitColors(d.GroupColors);

    private static IEnumerable<bool> Variants(DesignPreset d) => d.HasBothVariants ? new[] { true, false } : new[] { d.PrimaryIsDark };

    private static ResolvedPalette ColorsOf(DesignPreset d, bool dark) =>
        Appearance.Resolve(d.Pair != null && dark != d.PrimaryIsDark ? new DesignColors(d.Pair.PanelColor, d.Pair.AccentColor, d.Pair.Palette) : new DesignColors(d.PanelColor, d.AccentColor, d.Palette), dark);

    private static string VariantLabel(bool dark) => dark ? L.T("☾ Dark") : L.T("☀ Light");

    // ---- Design: Farben und Gruppenpalette je Variante -----------------------------------

    private IReadOnlyList<DesignSwatchRow>? _colorRows, _groupRows;

    /// <summary>Flächenfarben je Variante (erst dunkel, dann hell): Panel, Karte, Eingabe, Linie, Text, Auswahl, Akzent.</summary>
    public IReadOnlyList<DesignSwatchRow> DesignColorRows => _colorRows ??= DesignPreset is { } d
        ? Variants(d).Select(dark =>
        {
            var c = ColorsOf(d, dark);
            return new DesignSwatchRow(VariantLabel(dark), dark, c.Surface.Hex, new[]
            {
                new DesignSwatch(L.T("Panel"), c.Surface.Hex), new DesignSwatch(L.T("Card"), c.Card.Hex), new DesignSwatch(L.T("Input"), c.Input.Hex),
                new DesignSwatch(L.T("Line"), c.Line.Hex), new DesignSwatch(L.T("Text"), c.Text.Hex), new DesignSwatch(L.T("Selection"), c.Selected.Hex),
                new DesignSwatch(d.WindowsAccent ? L.T("Accent (Windows)") : L.T("Accent"), c.Accent.Hex)
            }, Array.Empty<DesignGroupPill>(), dark == ShowsDark, ChooseVariant);
        }).ToList() : Array.Empty<DesignSwatchRow>();

    /// <summary>Beispielnamen der Gruppen in der Gruppenpalette (sechs, übersetzt).</summary>
    public static IReadOnlyList<string> SampleGroupNames => L.T("Favorites|Admin|Tools|Network|Server|Links").Split('|');

    /// <summary>
    /// Gruppenpalette je Variante: sechs Beispielgruppen auf der Panelfläche dieser Variante, der Reihe nach in den Farben
    /// des Designs (wie Gruppen mit der Farbe „Vom Design“). Ohne Farbreihe bleiben sie neutral.
    /// </summary>
    public IReadOnlyList<DesignSwatchRow> DesignGroupRows => _groupRows ??= DesignPreset is { } d
        ? Variants(d).Select(dark =>
        {
            var c = ColorsOf(d, dark);
            var colors = GroupColorsOf(d, dark);
            var pills = SampleGroupNames.Select((name, i) =>
            {
                var color = colors.Count > 0 && PanelColors.TryParse(colors[i % colors.Count], out var g) ? g : c.Text;
                return new DesignGroupPill(name, color.Hex, PanelColors.Blend(c.Surface, color, colors.Count > 0 ? 0.14 : 0.06).Hex,
                    (colors.Count > 0 ? PanelColors.Blend(c.Surface, color, 0.4) : c.Line).Hex);
            }).ToList();
            return new DesignSwatchRow(VariantLabel(dark), dark, c.Surface.Hex, Array.Empty<DesignSwatch>(), pills, dark == ShowsDark, ChooseVariant);
        }).ToList() : Array.Empty<DesignSwatchRow>();

    /// <summary>Hinweis neben „Gruppenpalette“: für wen die Farben gelten.</summary>
    public string GroupPaletteHint => DesignPreset is not { } d ? string.Empty
        : Appearance.SplitColors(d.GroupColors).Count == 0 ? L.T("no group colors – groups keep their own color")
        : d.GroupColorsForTags ? L.T("also applies to tags · groups “From the design” get them in order")
        : L.T("groups “From the design” get them in order");

    private void MarkActiveRows()
    {
        foreach (var row in (_colorRows ?? Array.Empty<DesignSwatchRow>()).Concat(_groupRows ?? Array.Empty<DesignSwatchRow>()))
            row.IsActive = row.IsDark == ShowsDark;
    }

    // ---- Design: Schrift ---------------------------------------------------------------

    private const string AppFont = "Segoe UI Variable Text";
    private const string AppFontFallback = "Segoe UI";
    private const string AppMonoFont = "Cascadia Mono";
    private const string AppMonoFallback = "Consolas";

    /// <summary>Schrift für Text (mit Ersatz, falls sie fehlt) und ihr Name samt Prüfung, ob sie installiert ist.</summary>
    public string DesignFont => DesignPreset?.FontFamily is { Length: > 0 } f ? f + ", " + AppFontFallback : AppFont + ", " + AppFontFallback;
    public string DesignFontText => DesignPreset?.FontFamily is { Length: > 0 } f
        ? FontState(f, AppFontFallback)
        : L.T("Segoe UI (font of the app)");
    public string DesignMonoFont => (DesignPreset?.MonoFontFamily is { Length: > 0 } f ? f + ", " : string.Empty) + AppMonoFont + ", " + AppMonoFallback;
    public string DesignMonoText => DesignPreset?.MonoFontFamily is { Length: > 0 } f
        ? FontState(f, _page.App.Theme.IsFontInstalled(AppMonoFont) ? AppMonoFont : AppMonoFallback)
        : FontState(AppMonoFont, AppMonoFallback, ofApp: true);
    /// <summary>Fehlt die Schrift des Designs auf diesem PC?</summary>
    public bool IsDesignFontMissing => DesignPreset?.FontFamily is { Length: > 0 } f && !_page.App.Theme.IsFontInstalled(f);
    public bool IsDesignMonoFontMissing => !_page.App.Theme.IsFontInstalled(DesignPreset?.MonoFontFamily is { Length: > 0 } f ? f : AppMonoFont);

    private string FontState(string font, string fallback, bool ofApp = false) =>
        _page.App.Theme.IsFontInstalled(font) ? (ofApp ? L.F("{0} (font of the app)", font) : font)
        : L.F("{0} · missing here, {1} is used", font, fallback);

    /// <summary>Beispieltexte der Schrift-Vorschau.</summary>
    public static string FontSample => L.T("Aa Restart server 0123");
    public static string CodeSample => L.T("Get-Service | Where Status -eq 'Running'");

    // ---- Design: Form und Hintergrund --------------------------------------------------

    private CornerRadii Radii => CornerRadii.Of(DesignPreset?.Corners ?? CornerStyle.Standard);
    /// <summary>Radius der Mini-Vorschau „Ecken“ (Karten des Designs).</summary>
    public double PreviewCornerRadius => Radii.Card;

    /// <summary>Ecken (Panel und Karten) und Gruppenstil.</summary>
    public string DesignFormText => DesignPreset is { } d ? string.Join(" · ", new[]
    {
        d.Corners == null ? L.T("Corners as set in the app") : L.F("Corners {0} px · cards {1} px", Radii.Tile, Radii.Card),
        d.GroupStyle switch { GroupCardStyle.Tinted => L.T("Colored cards"), GroupCardStyle.Accent => L.T("Accent stripe"), _ => string.Empty }
    }.Where(t => t.Length > 0)) : string.Empty;

    /// <summary>Hintergrund · Deckkraft · Milchglas (bei Glas mit Weichzeichner und Sättigung).</summary>
    public string DesignBackgroundText => DesignPreset is { } d ? string.Join(" · ", new[]
    {
        PanelBackdrops.Label(d.Backdrop),
        d.Backdrop == PanelBackdrop.Solid ? string.Empty : L.F("opacity {0} %", d.Opacity), // Vollton heißt schon „deckend“
        d.Backdrop == PanelBackdrop.Solid ? string.Empty : d.Frost > 0 ? L.F("frosted glass {0} %", d.Frost) : L.T("frosted glass off"),
        d.Backdrop == PanelBackdrop.Glass ? L.F("blur {0}", d.GlassBlur) : string.Empty,
        d.Backdrop == PanelBackdrop.Glass ? L.F("saturation {0} %", d.GlassSaturation) : string.Empty
    }.Where(t => t.Length > 0)) : string.Empty;

    /// <summary>Deckkraft der Panelfläche in der Mini-Vorschau (0,2 … 1).</summary>
    public double PreviewOpacity => DesignPreset is { } d && d.Backdrop != PanelBackdrop.Solid ? Math.Clamp(d.Opacity / 100.0, 0.2, 1.0) : 1.0;
    /// <summary>Deckkraft der Milchglas-Tönung in der Mini-Vorschau (0 = aus).</summary>
    public double PreviewFrost => DesignPreset is { } d && d.Backdrop != PanelBackdrop.Solid ? Math.Clamp(d.Frost / 100.0, 0, 1) * 0.6 : 0;

    /// <summary>Übernimmt das Design die Akzentfarbe von Windows?</summary>
    public bool UsesWindowsAccent => DesignPreset?.WindowsAccent == true;
    public string AccentText => UsesWindowsAccent ? L.T("Windows accent color is used") : L.T("Own accent color (not the one from Windows)");

    /// <summary>Farben der gezeigten Variante für die Mini-Vorschauen.</summary>
    public ArgbColor? PreviewSurface => DesignPreset is { } d ? ArgbColor.From(ColorsOf(d, ShowsDark).Surface) : null;
    public ArgbColor? PreviewAccent => DesignPreset is { } d ? ArgbColor.From(ColorsOf(d, ShowsDark).Accent) : null;
    public ArgbColor? PreviewLineColor => DesignPreset is { } d ? ArgbColor.From(ColorsOf(d, ShowsDark).Line) : null;
    /// <summary>Textfarbe der gezeigten Variante.</summary>
    public ArgbColor? PreviewInk => DesignPreset is { } d ? ArgbColor.From(ColorsOf(d, ShowsDark).Text) : null;

    // ---- Design: Aktionen --------------------------------------------------------------

    /// <summary>Design vorübergehend anwenden (bis „Übernehmen“ oder „Zurück“).</summary>
    public ICommand TryCommand => _tryCommand ??= new RelayCommand(() => _page.TryDesign(this), () => HasDesign && !IsTrying);
    /// <summary>Herunterladen (falls nötig) und anwenden.</summary>
    public ICommand ApplyCommand => _applyCommand ??= new RelayCommand(async () => await _page.InstallAndApplyAsync(this), () => HasDesign);
    private ICommand? _tryCommand, _applyCommand;
    public string ApplyText => IsInstalled && !HasUpdate ? L.T("Apply design") : L.T("Install and apply");
    public bool IsTrying => _page.IsTryingRow(this);

    /// <summary>Installiert und gerade aktiv (nicht nur ausprobiert): „✓ Aktiv“ statt „Anwenden“.</summary>
    public bool IsActiveDesign => HasDesign && !IsTrying && !HasUpdate && Marketplace.FindInstall(_page.Settings, Source, File.Path) is { DesignKey.Length: > 0 } i
                                  && i.DesignKey == _page.Settings.DesignKey;
    public bool ShowApply => HasDesign && !IsActiveDesign;

    internal void RaiseTrying()
    {
        foreach (var p in new[] { nameof(IsTrying), nameof(ApplyText), nameof(IsActiveDesign), nameof(ShowApply) }) OnPropertyChanged(p);
    }

    /// <summary>Kurzinfo der Karte (Tooltip der Vorschau).</summary>
    public string DesignBackdropText => _details?.Design is { } d
        ? string.Join(" · ", new[]
        {
            PanelBackdrops.Label(d.Backdrop), $"{d.Opacity} %",
            d.Pair != null ? L.T("light and dark variant") : string.Empty,
            d.Palette is { IsEmpty: false } ? L.T("own palette") : string.Empty,
            string.IsNullOrEmpty(d.FontFamily) ? string.Empty : L.F("font {0}", d.FontFamily),
            Appearance.SplitColors(d.GroupColors).Count is > 0 and var n ? L.F("{0} group colors", n) : string.Empty
        }.Where(t => t.Length > 0)) : string.Empty;
}

/// <summary>Ein Farbfeld in den Details eines Designs (Klick kopiert <see cref="Hex"/>).</summary>
public sealed record DesignSwatch(string Name, string Hex)
{
    public ArgbColor? Color { get; } = ArgbColor.FromHex(Hex, 255);
}

/// <summary>Beispielgruppe der Gruppenpalette: Name, Gruppenfarbe sowie Fläche und Rahmen auf dem Panel der Variante.</summary>
public sealed record DesignGroupPill(string Name, string Hex, string BackgroundHex, string BorderHex)
{
    public ArgbColor? Color { get; } = ArgbColor.FromHex(Hex, 255);
    public ArgbColor? Background { get; } = ArgbColor.FromHex(BackgroundHex, 255);
    public ArgbColor? Border { get; } = ArgbColor.FromHex(BorderHex, 255);
}

/// <summary>
/// Eine Zeile der Detailseite (Variante hell bzw. dunkel): Farbfelder oder Beispielgruppen auf der Panelfläche dieser
/// Variante. Die Zeile der gezeigten Variante ist hervorgehoben; ein Klick auf die andere schaltet um.
/// </summary>
public sealed class DesignSwatchRow : ObservableObject
{
    private bool _active;

    public DesignSwatchRow(string label, bool isDark, string surfaceHex, IReadOnlyList<DesignSwatch> swatches, IReadOnlyList<DesignGroupPill> pills, bool isActive,
        Action<bool> choose)
    {
        Label = label;
        IsDark = isDark;
        SurfaceHex = surfaceHex;
        Surface = ArgbColor.FromHex(surfaceHex, 255);
        Swatches = swatches;
        Pills = pills;
        _active = isActive;
        SelectCommand = new RelayCommand(() => choose(isDark));
    }

    public string Label { get; }
    public bool IsDark { get; }
    public string SurfaceHex { get; }
    public ArgbColor? Surface { get; }
    public IReadOnlyList<DesignSwatch> Swatches { get; }
    public IReadOnlyList<DesignGroupPill> Pills { get; }
    /// <summary>Gehört zur gerade gezeigten Variante (hervorgehoben).</summary>
    public bool IsActive { get => _active; set => Set(ref _active, value); }
    /// <summary>Diese Variante zeigen.</summary>
    public ICommand SelectCommand { get; }
}

/// <summary>Eine Kategorie in der Navigation des Marktplatzes (Filter nach oberstem Ordner bzw. Kategorie des Designs).</summary>
public sealed class MarketplaceCategory
{
    public MarketplaceCategory(MarketplaceViewModel page, string name, int count, bool isSelected)
    {
        Name = name;
        Count = count;
        IsSelected = isSelected;
        // Erneuter Klick auf die gewählte Kategorie hebt den Filter auf
        SelectCommand = new RelayCommand(() => page.Category = isSelected ? string.Empty : name);
    }

    public string Name { get; }
    public int Count { get; }
    public bool IsSelected { get; }
    public ICommand SelectCommand { get; }
}

/// <summary>Eine Quelle in der Liste „Quellen“.</summary>
public sealed class MarketplaceSourceViewModel
{
    public MarketplaceSourceViewModel(MarketplaceViewModel page, MarketplaceSource source, string name, int count, string? error)
    {
        Url = source.Url;
        Name = name;
        IsDefault = Marketplace.IsDefault(source.Url);
        Trusted = source.Trusted || IsDefault;
        Summary = error != null ? L.T("Unreachable: ") + error
            : (IsDefault ? L.T("Bundled") : Trusted ? L.T("Trusted") : L.T("Not confirmed yet – you'll be asked before the first installation"))
              + (count > 0 ? L.F(" · {0} files", count) : string.Empty);
        HasError = error != null;
        RemoveCommand = new RelayCommand(() => page.RemoveSource(this), () => !IsDefault);
    }

    public string Url { get; }
    public string Name { get; }
    public bool IsDefault { get; }
    public bool Trusted { get; }
    public bool CanRemove => !IsDefault;
    public string Summary { get; }
    public bool HasError { get; }
    public ICommand RemoveCommand { get; }
}
