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
        set { if (_category == (value ?? string.Empty)) return; _category = value ?? string.Empty; OnPropertyChanged(); Rebuild(); }
    }

    public int AllCount => AllFiles().Count();
    public int EntryCount => Count(MarketplaceItemType.Entry);
    public int ScriptCount => Count(MarketplaceItemType.Script);
    public int DesignCount => Count(MarketplaceItemType.Design);
    public int InstalledCount => AllFiles().Count(f => Marketplace.FindInstall(_s, f.Source, f.File.Path) != null);

    /// <summary>Details einer Karte auf- bzw. zuklappen; es ist immer höchstens eine offen.</summary>
    internal void ToggleDetails(MarketplaceItemRow row)
    {
        var open = !row.IsPreviewOpen;
        foreach (var i in _items) if (!ReferenceEquals(i, row)) i.IsPreviewOpen = false;
        row.IsPreviewOpen = open;
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
            _tab = value ?? "Entry";
            _category = string.Empty; // Kategorien gehören zum Bereich
            OnPropertyChanged();
            OnPropertyChanged(nameof(Category));
            OnPropertyChanged(nameof(IsSourcesPage));
            OnPropertyChanged(nameof(IsListPage));
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
            var groups = items.Select(i => i.Source + "|" + (_tab == "Installed" ? i.File.Folder : i.File.GroupFolder)).Distinct().Count();
            var installed = items.Count(i => i.IsInstalled);
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
        var openKey = _items.FirstOrDefault(i => i.IsPreviewOpen)?.Id;
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
            var files = tree.Files.Where(f => _tab == "Installed" ? Marketplace.FindInstall(_s, src.Url, f.Path) != null : _tab == "All" || f.Type.ToString() == _tab).ToList();
            var items = files.Select(f => new MarketplaceItemRow(this, src.Url, tree.Repo, f, Payloads.TryGetValue(PayloadKey(src.Url, f), out var p) ? p.Details : null))
                .Where(i => terms.All(t => i.SearchText.Contains(t, StringComparison.CurrentCultureIgnoreCase))).ToList();
            foreach (var i in items.Where(i => i.CategoryName.Length > 0))
                categories[i.CategoryName] = categories.TryGetValue(i.CategoryName, out var n) ? n + 1 : 1;
            if (_category.Length > 0) items = items.Where(i => i.CategoryName.Equals(_category, StringComparison.OrdinalIgnoreCase)).ToList();
            if (items.Count == 0) continue;
            foreach (var i in items)
            {
                if (checkedKeys.Contains(i.Id)) i.SetChecked(true);
                if (i.Id == openKey) i.IsPreviewOpen = true;
            }
            _items.AddRange(items);
            var root = new MarketplaceFolderRow(this, Key(src.Url) + "|", SourceName(src.Url), 0, isSource: true);
            foreach (var item in items)
            {
                var parent = root;
                var path = string.Empty;
                foreach (var part in (_tab == "Installed" ? item.File.Folder : item.File.GroupFolder).Split('/', StringSplitOptions.RemoveEmptyEntries))
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
        Categories.Clear();
        foreach (var c in categories.OrderBy(c => c.Key, StringComparer.CurrentCultureIgnoreCase))
            Categories.Add(new MarketplaceCategory(this, c.Key, c.Value, c.Key.Equals(_category, StringComparison.OrdinalIgnoreCase)));
        foreach (var p in new[] { nameof(IsEmpty), nameof(EntryTabText), nameof(ScriptTabText), nameof(DesignTabText), nameof(InstalledTabText), nameof(SummaryText), nameof(HasRows),
                     nameof(AllCount), nameof(EntryCount), nameof(ScriptCount), nameof(DesignCount), nameof(InstalledCount), nameof(HasCategories) }) OnPropertyChanged(p);
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

    internal Task InstallFolderAsync(MarketplaceFolderRow folder) =>
        InstallAsync(folder.AllItems().Where(i => !i.IsInstalled || i.HasUpdate).ToList());

    /// <summary>Vertrauen, Vorschau, fehlende Variablen, Gruppe je Ordner – dann alles installieren.</summary>
    internal async Task InstallAsync(IReadOnlyList<MarketplaceItemRow> rows)
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
            if (!_app.Dialogs.Confirm($"{heading}\n\n{preview}", L.T("Marketplace – preview"), owner: _app.SettingsDialogOwner)) return;

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
            Snackbar.Show(L.T("Downloaded: ") + string.Join(", ", new[]
            {
                entries > 0 ? (entries == 1 ? L.T("1 entry") : L.F("{0} entries", entries)) : null,
                scripts > 0 ? (scripts == 1 ? L.T("1 script") : L.F("{0} scripts", scripts)) : null,
                designs > 0 ? (designs == 1 ? L.T("1 design (under design templates)") : L.F("{0} designs (under design templates)", designs)) : null
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
    /// <summary>Kategorie: oberster Ordner der Gruppe („Links“); leer, wenn die Datei in keiner Gruppe liegt.</summary>
    public string CategoryName => File.GroupFolder.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
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
    internal string SearchText => $"{Name} {Description} {File.Path} {TypeText}";

    public bool IsInstalled => Marketplace.FindInstall(_page.Settings, Source, File.Path) != null;
    public bool HasUpdate => Marketplace.HasUpdate(_page.Settings, Source, File);
    public string StateText => HasUpdate ? L.T("Update available") : IsInstalled ? L.T("✓ Installed") : string.Empty;
    public string InstallText => HasUpdate ? L.T("Update") : L.T("Download");
    public bool CanInstall => !IsInstalled || HasUpdate;

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
        foreach (var p in new[] { nameof(Name), nameof(Description), nameof(Needs), nameof(PreviewText), nameof(PreviewLine), nameof(EntryKindText), nameof(VariablesText),
                     nameof(HasVariables), nameof(DesignPanelColor), nameof(DesignAccentColor), nameof(DesignTextColor), nameof(DesignSubtleColor),
                     nameof(DesignBackdropText), nameof(HasDesign), nameof(DesignPreset) })
            OnPropertyChanged(p);
    }

    // ---- Vorschau ----------------------------------------------------------------------

    private bool _previewOpen;
    /// <summary>Vorschau unter der Zeile aufgeklappt (Einträge und Scripte; Designs zeigen sie immer).</summary>
    public bool IsPreviewOpen { get => _previewOpen; set => Set(ref _previewOpen, value); }
    public ICommand TogglePreviewCommand => _togglePreview ??= new RelayCommand(() => _page.ToggleDetails(this));
    private ICommand? _togglePreview;

    public bool IsDesignFile => File.Type == MarketplaceItemType.Design;
    public bool IsEntryOrScript => File.Type != MarketplaceItemType.Design;

    /// <summary>Ziel, Befehl oder Code (Einträge) bzw. Anfang des Scripts.</summary>
    public string PreviewText => _details?.Preview ?? L.T("Loading …");
    public string EntryKindText => _details?.Entry is { } e ? CustomEntry.KindLabel(e.Kind) : TypeText;
    public bool HasVariables => _details?.Variables.Count > 0;
    /// <summary>Benötigte Variablen mit ihrer Art, je Zeile.</summary>
    public string VariablesText => _details == null ? string.Empty : string.Join("\n", _details.Variables.Select(v =>
        "{{$" + Variables.Normalize(v.Name) + "}} · " + Variables.Label(v.Kind) + (v.Description.Length > 0 ? " – " + v.Description : string.Empty)));

    // Design: Panelfläche, Akzent, Schrift – als #RRGGBB für die Mini-Vorschau
    public bool HasDesign => _details?.Design != null;
    /// <summary>Design für die gemeinsame Vorschau (wie unter Darstellung).</summary>
    public DesignPreset? DesignPreset => _details?.Design;
    private bool DesignIsLight => _details?.Design is { } d &&
        (d.PanelColor.Length > 0 ? ArgbColor.FromHex(d.PanelColor, 255) is { } c && (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150 : d.Theme == ThemeMode.Light);
    public string DesignPanelColor => _details?.Design is { } d
        ? (d.PanelColor.Length > 0 ? d.PanelColor : d.Theme == ThemeMode.Light ? "#F3F3F3" : "#202020") : "#202020";
    public string DesignAccentColor => _details?.Design is { AccentColor.Length: > 0 } d ? d.AccentColor : "#0078D4";
    public string DesignTextColor => DesignIsLight ? "#1B1B1B" : "#FFFFFF";
    /// <summary>Panelfarbe leicht zur Schriftfarbe gemischt (Zeilen und Knöpfe der Mini-Vorschau).</summary>
    public string DesignSubtleColor
    {
        get
        {
            var p = ArgbColor.FromHex(DesignPanelColor) ?? new ArgbColor(255, 32, 32, 32);
            var t = DesignIsLight ? 0 : 255;
            byte Mix(byte c) => (byte)Math.Round(c + (t - c) * 0.16);
            return $"#{Mix(p.R):X2}{Mix(p.G):X2}{Mix(p.B):X2}";
        }
    }
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

/// <summary>Eine Quelle in der Liste „Quellen“.</summary>
/// <summary>Eine Kategorie in der Navigation des Marktplatzes (Filter nach oberstem Ordner).</summary>
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
