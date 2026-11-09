using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Infrastructure;

namespace PowerExec.ViewModels;

/// <summary>
/// Einstellungsseite „Marktplatz“: Einträge, Pakete, Designs und Scripte aus der mitgelieferten und aus eigenen Quellen
/// (Repository-URL) herunterladen. Inhalte einer fremden Quelle müssen einmal als vertrauenswürdig bestätigt werden.
/// </summary>
public sealed class MarketplaceViewModel : ObservableObject
{
    private readonly ISettingsHost _app;
    private readonly AppSettings _s;
    private readonly Action _changed;
    private List<MarketplaceItemViewModel> _all = new();
    private string _query = string.Empty;
    private string _typeFilter = "all";
    private string _sourceFilter = "*";
    private string? _status;
    private string _statusSeverity = "Info";
    private bool _loading;
    private bool _loaded;

    /// <summary>Geladene Quellen über einen Neuaufbau des Einstellungsfensters hinweg (URL → Index, Name, Fehler).</summary>
    private static readonly Dictionary<string, (Uri Index, string Name, IReadOnlyList<MarketplaceItem> Items, string? Error)> Cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static (string? Text, string Severity) _lastStatus = (null, "Info");

    /// <param name="changed">Nach einer Installation: andere Seiten (Gruppen, Designs, Variablen) neu lesen.</param>
    public MarketplaceViewModel(ISettingsHost app, AppSettings workingCopy, Action changed)
    {
        _app = app;
        _s = workingCopy;
        _changed = changed;
        RefreshCommand = new RelayCommand(async () => await LoadAsync(), () => !_loading);
        AddSourceCommand = new RelayCommand(async () => await AddSourceAsync(), () => !_loading);
        RebuildSources();
        if (Cache.Count > 0)
        {
            BuildItems();
            _loaded = true;
            (_status, _statusSeverity) = (_lastStatus.Text, _lastStatus.Severity);
            _lastStatus = (null, "Info");
            Filter();
        }
    }

    internal AppSettings Settings => _s;
    internal ISettingsHost App => _app;

    public ObservableCollection<MarketplaceItemViewModel> Items { get; } = new();
    public ObservableCollection<MarketplaceSourceViewModel> SourceList { get; } = new();
    public ICommand RefreshCommand { get; }
    public ICommand AddSourceCommand { get; }
    public bool IsLoading { get => _loading; private set { if (Set(ref _loading, value)) CommandRequery.Invalidate(); } }
    public bool IsEmpty => !_loading && _loaded && Items.Count == 0;
    public string? Status { get => _status; private set => Set(ref _status, value); }
    public string StatusSeverity { get => _statusSeverity; private set => Set(ref _statusSeverity, value); }
    public string UpdateSummary => _all.Count(i => i.HasUpdate) is var n and > 0 ? (n == 1 ? "1 Update verfügbar" : $"{n} Updates verfügbar") : string.Empty;

    public string Query { get => _query; set { if (Set(ref _query, value ?? string.Empty)) Filter(); } }
    public string TypeFilter { get => _typeFilter; set { if (Set(ref _typeFilter, value ?? "all")) Filter(); } }

    /// <summary>Quellenfilter: „*“ = alle, sonst die URL einer Quelle.</summary>
    public string SourceFilter { get => _sourceFilter; set { if (Set(ref _sourceFilter, value ?? "*")) Filter(); } }
    public IReadOnlyList<KeyValuePair<string, string>> SourceChoices =>
        new[] { new KeyValuePair<string, string>("*", "Alle Quellen") }
            .Concat(Marketplace.Sources(_s).Select(s => new KeyValuePair<string, string>(s.Url, DisplayName(s)))).ToList();
    public bool HasSeveralSources => Marketplace.Sources(_s).Count > 1;

    private static string DisplayName(MarketplaceSource s) =>
        Cache.TryGetValue(s.Url, out var c) && c.Name.Length > 0 && !Marketplace.IsDefault(s.Url) ? c.Name
        : s.Name.Length > 0 ? s.Name : Marketplace.NameFromUrl(s.Url);

    /// <summary>Beim ersten Öffnen der Seite laden.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (!_loaded && !_loading) await LoadAsync();
    }

    /// <summary>Alle Quellen (neu) laden; eine nicht erreichbare Quelle hält die anderen nicht auf.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        Status = "Lade den Marktplatz …";
        StatusSeverity = "Info";
        try
        {
            await Task.WhenAll(Marketplace.Sources(_s).Select(LoadSourceAsync));
            BuildItems();
            _loaded = true;
            var errors = Cache.Where(c => Marketplace.Sources(_s).Any(s => Marketplace.SameSource(s.Url, c.Key)) && c.Value.Error != null).ToList();
            (Status, StatusSeverity) = errors.Count == 0 ? (null, "Info")
                : (string.Join("\n", errors.Select(e => $"{Marketplace.NameFromUrl(e.Key)} nicht erreichbar: {e.Value.Error}")), "Warning");
            Filter();
        }
        finally
        {
            IsLoading = false;
            RebuildSources();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(UpdateSummary));
        }
    }

    private async Task LoadSourceAsync(MarketplaceSource source)
    {
        Uri index;
        try { index = Marketplace.IndexUri(source.Url); }
        catch (Exception ex) { Cache[source.Url] = (new Uri("about:blank"), source.Name, Array.Empty<MarketplaceItem>(), ex.Message); return; }
        try
        {
            var parsed = Marketplace.ParseIndex(await _app.DownloadTextAsync(index));
            Cache[source.Url] = (index, parsed.Name, parsed.Items, null);
        }
        catch (Exception ex)
        {
            Cache[source.Url] = (index, source.Name, Cache.TryGetValue(source.Url, out var old) ? old.Items : Array.Empty<MarketplaceItem>(), ex.Message);
        }
    }

    private void BuildItems()
    {
        _all = Marketplace.Sources(_s)
            .SelectMany(src => Cache.TryGetValue(src.Url, out var c)
                ? c.Items.Select(i => new MarketplaceItemViewModel(this, i, src.Url, c.Index, DisplayName(src)))
                : Enumerable.Empty<MarketplaceItemViewModel>())
            .ToList();
    }

    private void RebuildSources()
    {
        SourceList.Clear();
        foreach (var s in Marketplace.Sources(_s))
        {
            Cache.TryGetValue(s.Url, out var c);
            SourceList.Add(new MarketplaceSourceViewModel(this, s, DisplayName(s), c.Items?.Count ?? 0, c.Error));
        }
        OnPropertyChanged(nameof(SourceChoices));
        OnPropertyChanged(nameof(HasSeveralSources));
    }

    private void Filter()
    {
        var terms = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Items.Clear();
        foreach (var i in _all.Where(i => (_typeFilter == "all" || (_typeFilter == "updates" ? i.HasUpdate : i.Item.Type.ToString() == _typeFilter))
                                          && (_sourceFilter == "*" || Marketplace.SameSource(i.Source, _sourceFilter))
                                          && terms.All(t => i.SearchText.Contains(t, StringComparison.CurrentCultureIgnoreCase))))
            Items.Add(i);
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Quelle per Repository-URL hinzufügen: Adresse prüfen, Index laden, dann übernehmen.</summary>
    private async Task AddSourceAsync()
    {
        var url = _app.Dialogs.Ask("Quelle hinzufügen",
            "Adresse des Repositorys (GitHub oder GitLab), z. B. https://github.com/firma/power-exec-katalog\n" +
            "Ein Unterordner geht auch: …/tree/main/marktplatz. Das Repository muss eine index.json enthalten (siehe marketplace/README.md).",
            string.Empty, "Hinzufügen", u => Marketplace.ValidateSource(_s, u));
        if (url == null) return;
        url = url.Trim().TrimEnd('/');
        IsLoading = true;
        Status = "Prüfe die Quelle …";
        StatusSeverity = "Info";
        try
        {
            var index = Marketplace.IndexUri(url);
            var parsed = Marketplace.ParseIndex(await _app.DownloadTextAsync(index));
            _s.MarketplaceSources.Add(new MarketplaceSource
            {
                Url = url, Name = parsed.Name.Length > 0 && parsed.Name != "Marktplatz" ? parsed.Name : Marketplace.NameFromUrl(url), AddedUtc = DateTime.UtcNow
            });
            Cache[url] = (index, parsed.Name, parsed.Items, null);
            BuildItems();
            Status = $"Quelle „{Marketplace.NameFromUrl(url)}“ mit {parsed.Items.Count} Elementen hinzugefügt. Vor der ersten Installation fragt Power-Exec, ob du ihr vertraust.";
            StatusSeverity = "Success";
            _lastStatus = (Status, StatusSeverity);
            Filter();
            _changed();
        }
        catch (Exception ex)
        {
            Status = "Quelle nicht hinzugefügt: " + ex.Message;
            StatusSeverity = "Error";
        }
        finally
        {
            IsLoading = false;
            RebuildSources();
        }
    }

    internal void RemoveSource(MarketplaceSourceViewModel source)
    {
        if (source.IsDefault) return;
        if (!_app.Dialogs.Confirm($"Quelle „{source.Name}“ entfernen? Bereits installierte Einträge, Designs und Scripte bleiben erhalten.",
                "Quelle entfernen", owner: _app.SettingsDialogOwner)) return;
        _s.MarketplaceSources.RemoveAll(x => Marketplace.SameSource(x.Url, source.Url));
        Cache.Remove(source.Url);
        if (Marketplace.SameSource(_sourceFilter, source.Url)) _sourceFilter = "*";
        BuildItems();
        Filter();
        RebuildSources();
        Status = $"Quelle „{source.Name}“ entfernt.";
        StatusSeverity = "Info";
        _lastStatus = (Status, StatusSeverity);
        _changed();
    }

    /// <summary>Fremde Quelle: einmal bestätigen, dass ihre Inhalte installiert werden dürfen.</summary>
    private bool EnsureTrusted(MarketplaceItemViewModel vm)
    {
        if (Marketplace.IsTrusted(_s, vm.Source)) return true;
        if (!_app.Dialogs.Confirm(
                $"„{vm.SourceName}“ ist eine fremde Quelle:\n{vm.Source}\n\n" +
                "Einträge, Befehle und Scripte daraus können auf diesem PC Programme starten und Code ausführen. " +
                "Vertraue nur Quellen, die du oder dein Team kontrolliert.\n\nDieser Quelle vertrauen?",
                "Quelle vertrauen?", destructive: true, owner: _app.SettingsDialogOwner, defaultNo: true))
            return false;
        foreach (var s in _s.MarketplaceSources.Where(x => Marketplace.SameSource(x.Url, vm.Source))) s.Trusted = true;
        RebuildSources();
        return true;
    }

    /// <summary>Installieren: Vertrauen (fremde Quelle), Vorschau bestätigen, fehlende Variablen abfragen, Inhalt übernehmen.</summary>
    internal async Task InstallAsync(MarketplaceItemViewModel vm)
    {
        var item = vm.Item;
        if (!EnsureTrusted(vm)) return;
        vm.IsBusy = true;
        try
        {
            var payload = await _app.DownloadTextAsync(Marketplace.FileUri(vm.Index, item.File));
            var preview = Marketplace.Preview(item, payload);
            var verb = vm.IsInstalled ? "aktualisieren" : "installieren";
            if (!_app.Dialogs.Confirm($"„{item.Name}“ {verb}?\n\nQuelle: {vm.SourceName} ({vm.Source})\n\n{preview}", "Marktplatz – Vorschau", owner: _app.SettingsDialogOwner))
                return;
            foreach (var v in Marketplace.MissingVariables(_s, item))
            {
                var value = _app.Dialogs.Ask($"Variable {{{{${Variables.Normalize(v.Name)}}}}}",
                    (v.Description.Length > 0 ? v.Description : $"Wert für {{{{${v.Name}}}}}") + (v.Example.Length > 0 ? $"\nz. B. {v.Example}" : string.Empty),
                    v.Example, "Übernehmen");
                if (value == null) return;
                _s.Variables.Add(new UserVariable { Name = Variables.Normalize(v.Name), Value = value.Trim(), Description = v.Description });
            }
            Status = Marketplace.Install(_s, vm.Source, item, payload, _app.MarketplaceScriptFolder);
            StatusSeverity = "Success";
            _lastStatus = (Status, StatusSeverity);
            _changed();
        }
        catch (Exception ex)
        {
            Status = $"„{item.Name}“ ließ sich nicht installieren: {ex.Message}";
            StatusSeverity = "Error";
        }
        finally
        {
            vm.IsBusy = false;
            vm.Refresh();
            OnPropertyChanged(nameof(UpdateSummary));
        }
    }
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
        Summary = error != null ? "Nicht erreichbar: " + error
            : (IsDefault ? "Mitgeliefert" : Trusted ? "Vertrauenswürdig" : "Noch nicht bestätigt – vor der ersten Installation wird gefragt")
              + (count > 0 ? $" · {count} Elemente" : string.Empty);
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

/// <summary>Ein Element im Marktplatz.</summary>
public sealed class MarketplaceItemViewModel : ObservableObject
{
    private readonly MarketplaceViewModel _page;
    private bool _busy;

    public MarketplaceItemViewModel(MarketplaceViewModel page, MarketplaceItem item, string source, Uri index, string sourceName)
    {
        _page = page;
        Item = item;
        Source = source;
        Index = index;
        SourceName = sourceName;
        InstallCommand = new RelayCommand(async () => await _page.InstallAsync(this), () => !_busy);
    }

    public MarketplaceItem Item { get; }
    public string Source { get; }
    internal Uri Index { get; }
    public string SourceName { get; }
    public string Name => Item.Name;
    public string Description => Item.Description;
    public string TypeText => Item.Type switch
    {
        MarketplaceItemType.Package => "Paket",
        MarketplaceItemType.Design => "Design",
        MarketplaceItemType.Script => "Script",
        _ => "Eintrag"
    };
    public string Glyph => Item.Type switch
    {
        MarketplaceItemType.Package => "",
        MarketplaceItemType.Design => "",
        MarketplaceItemType.Script => "",
        _ => ""
    };
    public string Details => TypeText + (Item.Category.Length > 0 && Item.Category != TypeText + "s" ? " · " + Item.Category : string.Empty)
                             + (Marketplace.IsDefault(Source) ? string.Empty : " · aus " + SourceName)
                             + (Item.Variables.Count > 0 ? " · braucht " + string.Join(", ", Item.Variables.Select(v => "{{$" + Variables.Normalize(v.Name) + "}}")) : string.Empty);
    internal string SearchText => $"{Item.Name} {Item.Description} {Item.Category} {TypeText} {SourceName} {string.Join(' ', Item.Tags)}";

    public bool IsInstalled => Marketplace.FindInstall(_page.Settings, Source, Item.Id) != null;
    public bool HasUpdate => Marketplace.HasUpdate(_page.Settings, Source, Item);
    public string ActionText => HasUpdate ? "Aktualisieren" : IsInstalled ? "Erneut installieren" : "Installieren";
    public string StateText => HasUpdate ? "Update verfügbar" : IsInstalled ? "✓ Installiert" : string.Empty;

    public ICommand InstallCommand { get; }
    public bool IsBusy { get => _busy; set { if (Set(ref _busy, value)) CommandRequery.Invalidate(); } }

    internal void Refresh()
    {
        foreach (var p in new[] { nameof(IsInstalled), nameof(HasUpdate), nameof(ActionText), nameof(StateText) }) OnPropertyChanged(p);
    }
}
