using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Infrastructure;

namespace PowerExec.ViewModels;

/// <summary>Einstellungsseite „Marktplatz“: Einträge, Pakete, Designs und Scripte herunterladen.</summary>
public sealed class MarketplaceViewModel : ObservableObject
{
    private readonly ISettingsHost _app;
    private readonly AppSettings _s;
    private readonly Action _changed;
    private List<MarketplaceItemViewModel> _all = new();
    private string _query = string.Empty;
    private string _typeFilter = "all";
    private string? _status;
    private string _statusSeverity = "Info";
    private bool _loading;
    private bool _loaded;
    private Uri? _index;

    /// <param name="changed">Nach einer Installation: andere Seiten (Gruppen, Designs, Variablen) neu lesen.</param>
    public MarketplaceViewModel(ISettingsHost app, AppSettings workingCopy, Action changed)
    {
        _app = app;
        _s = workingCopy;
        _changed = changed;
        RefreshCommand = new RelayCommand(async () => await LoadAsync(), () => !_loading);
        // Nach einer Installation baut das Einstellungsfenster seine Seiten neu: geladenen Index und Meldung übernehmen
        if (_cache is { } c)
        {
            _index = c.Index;
            _all = c.Items.Select(i => new MarketplaceItemViewModel(this, i)).ToList();
            _loaded = true;
            (_status, _statusSeverity) = (_lastStatus.Text, _lastStatus.Severity);
            _lastStatus = (null, "Info");
            Filter();
        }
    }

    private static (Uri Index, IReadOnlyList<MarketplaceItem> Items)? _cache;
    private static (string? Text, string Severity) _lastStatus = (null, "Info");

    public string Source => Marketplace.DefaultSource;
    public ObservableCollection<MarketplaceItemViewModel> Items { get; } = new();
    public ICommand RefreshCommand { get; }
    public bool IsLoading { get => _loading; private set { if (Set(ref _loading, value)) CommandRequery.Invalidate(); } }
    public bool IsEmpty => !_loading && _loaded && Items.Count == 0;
    public string? Status { get => _status; private set => Set(ref _status, value); }
    public string StatusSeverity { get => _statusSeverity; private set => Set(ref _statusSeverity, value); }
    public string UpdateSummary => _all.Count(i => i.HasUpdate) is var n and > 0 ? (n == 1 ? "1 Update verfügbar" : $"{n} Updates verfügbar") : string.Empty;

    public string Query { get => _query; set { if (Set(ref _query, value ?? string.Empty)) Filter(); } }
    public string TypeFilter { get => _typeFilter; set { if (Set(ref _typeFilter, value ?? "all")) Filter(); } }

    /// <summary>Beim ersten Öffnen der Seite laden.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (!_loaded && !_loading) await LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        Status = "Lade den Marktplatz …";
        StatusSeverity = "Info";
        try
        {
            _index = Marketplace.IndexUri(Source);
            var index = Marketplace.ParseIndex(await _app.DownloadTextAsync(_index));
            _all = index.Items.Select(i => new MarketplaceItemViewModel(this, i)).ToList();
            _cache = (_index, index.Items);
            _loaded = true;
            Status = null;
            Filter();
        }
        catch (Exception ex)
        {
            Status = "Marktplatz nicht erreichbar: " + ex.Message;
            StatusSeverity = "Error";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(UpdateSummary));
        }
    }

    private void Filter()
    {
        var terms = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Items.Clear();
        foreach (var i in _all.Where(i => (_typeFilter == "all" || (_typeFilter == "updates" ? i.HasUpdate : i.Item.Type.ToString() == _typeFilter))
                                          && terms.All(t => i.SearchText.Contains(t, StringComparison.CurrentCultureIgnoreCase))))
            Items.Add(i);
        OnPropertyChanged(nameof(IsEmpty));
    }

    internal AppSettings Settings => _s;

    /// <summary>Installieren: Vorschau bestätigen, fehlende Variablen abfragen, Inhalt übernehmen.</summary>
    internal async Task InstallAsync(MarketplaceItemViewModel vm)
    {
        if (_index == null) return;
        var item = vm.Item;
        vm.IsBusy = true;
        try
        {
            var payload = await _app.DownloadTextAsync(Marketplace.FileUri(_index, item.File));
            var preview = Marketplace.Preview(item, payload);
            var verb = vm.IsInstalled ? "aktualisieren" : "installieren";
            if (!_app.Dialogs.Confirm($"„{item.Name}“ {verb}?\n\nQuelle: {Source}\n\n{preview}", "Marktplatz – Vorschau", owner: _app.SettingsDialogOwner))
                return;
            foreach (var v in Marketplace.MissingVariables(_s, item))
            {
                var value = _app.Dialogs.Ask($"Variable {{{{${Variables.Normalize(v.Name)}}}}}",
                    (v.Description.Length > 0 ? v.Description : $"Wert für {{{{${v.Name}}}}}") + (v.Example.Length > 0 ? $"\nz. B. {v.Example}" : string.Empty),
                    v.Example, "Übernehmen");
                if (value == null) return;
                _s.Variables.Add(new UserVariable { Name = Variables.Normalize(v.Name), Value = value.Trim(), Description = v.Description });
            }
            Status = Marketplace.Install(_s, Source, item, payload, _app.MarketplaceScriptFolder);
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

/// <summary>Ein Element im Marktplatz.</summary>
public sealed class MarketplaceItemViewModel : ObservableObject
{
    private readonly MarketplaceViewModel _page;
    private bool _busy;

    public MarketplaceItemViewModel(MarketplaceViewModel page, MarketplaceItem item)
    {
        _page = page;
        Item = item;
        InstallCommand = new RelayCommand(async () => await _page.InstallAsync(this), () => !_busy);
    }

    public MarketplaceItem Item { get; }
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
                             + (Item.Variables.Count > 0 ? " · braucht " + string.Join(", ", Item.Variables.Select(v => "{{$" + Variables.Normalize(v.Name) + "}}")) : string.Empty);
    internal string SearchText => $"{Item.Name} {Item.Description} {Item.Category} {TypeText} {string.Join(' ', Item.Tags)}";

    public bool IsInstalled => Marketplace.FindInstall(_page.Settings, _page.Source, Item.Id) != null;
    public bool HasUpdate => Marketplace.HasUpdate(_page.Settings, _page.Source, Item);
    public string ActionText => HasUpdate ? "Aktualisieren" : IsInstalled ? "Erneut installieren" : "Installieren";
    public string StateText => HasUpdate ? "Update verfügbar" : IsInstalled ? "✓ Installiert" : string.Empty;

    public ICommand InstallCommand { get; }
    public bool IsBusy { get => _busy; set { if (Set(ref _busy, value)) CommandRequery.Invalidate(); } }

    internal void Refresh()
    {
        foreach (var p in new[] { nameof(IsInstalled), nameof(HasUpdate), nameof(ActionText), nameof(StateText) }) OnPropertyChanged(p);
    }
}
