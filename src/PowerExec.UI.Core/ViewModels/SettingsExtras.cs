using PowerExec.Core.Localization;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;


/// <summary>Eine Rechnergruppe („@Name“) im Einstellungsfenster.</summary>
public sealed class MachineGroupViewModel : ObservableObject
{
    private string _name;
    private string _machines;
    private string _adQuery = string.Empty;
    private string _status = string.Empty;
    private bool _searching;

    private readonly ISettingsHost _app;

    public MachineGroupViewModel(ISettingsHost app, MachineGroup group, Action<MachineGroupViewModel> remove)
    {
        _app = app;
        _name = group.Name;
        _machines = string.Join(Environment.NewLine, group.Machines);
        RemoveCommand = new RelayCommand(() => remove(this));
        ImportCommand = new RelayCommand(Import);
        AdSearchCommand = new RelayCommand(() => SearchAdAsync().Forget(), () => !_searching && _adQuery.Trim().Length > 0);
        SortCommand = new RelayCommand(() => Machines = string.Join(Environment.NewLine, Parse().OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        _isExpanded = group.Machines.Count == 0 && group.AdQuery == null; // neue Gruppen offen, vorhandene eingeklappt

        // Dynamisch (AD-Regeln)
        _isDynamic = group.AdQuery != null;
        _searchBase = group.AdQuery?.SearchBase ?? string.Empty;
        _lastSyncUtc = group.LastSyncUtc;
        _lastSyncError = group.LastSyncError;
        foreach (var b in group.AdQuery?.Blocks ?? new List<AdRuleBlock>()) Blocks.Add(new AdBlockViewModel(this, b));
        AddBlockCommand = new RelayCommand(() => { Blocks.Add(new AdBlockViewModel(this, NewBlock())); RulesChanged(); });
        SyncNowCommand = new RelayCommand(() => SyncNowAsync().Forget(), () => !_searching && (_isDynamic || _isList));
        ExportCommand = new RelayCommand(Export);
        PickOuCommand = new RelayCommand(() => PickOuAsync().Forget(), () => !_searching);

        // Dynamisch aus einer Liste (Text oder CSV mit Filtern)
        _isList = group.ListQuery != null;
        _listPath = group.ListQuery?.Path ?? string.Empty;
        _nameColumn = group.ListQuery?.NameColumn ?? string.Empty;
        foreach (var f in group.ListQuery?.Filters ?? new List<ListFilter>()) ListFilters.Add(new ListFilterViewModel(this, f));
        AddFilterCommand = new RelayCommand(() => { ListFilters.Add(new ListFilterViewModel(this, new ListFilter { Column = DefaultFilterColumn })); ListChanged(); });
        ChooseListCommand = new RelayCommand(ChooseList);
        if (_isList) LoadTable();
        RulesChanged();
    }

    // ---- Quelle: feste Liste, dynamisch aus einer Liste, dynamisch aus dem AD -------------

    public static IReadOnlyList<KeyValuePair<string, string>> SourceChoices { get; } = new[]
    {
        new KeyValuePair<string, string>("static", L.T("Fixed list")),
        new KeyValuePair<string, string>("list", L.T("Dynamic from list")),
        new KeyValuePair<string, string>("ad", L.T("Dynamic from AD"))
    };

    public IReadOnlyList<KeyValuePair<string, string>> Sources => SourceChoices;

    /// <summary>„static“, „list“ oder „ad“.</summary>
    public string Source
    {
        get => _isDynamic ? "ad" : _isList ? "list" : "static";
        set
        {
            var v = value ?? "static";
            if (v == Source) return;
            IsListSource = v == "list";
            IsDynamic = v == "ad";
            OnPropertyChanged();
        }
    }

    /// <summary>Synchronisieren in der Kopfzeile (dynamische Gruppen).</summary>
    public bool CanSync => _isDynamic || _isList;

    public ICommand ExportCommand { get; }
    public ICommand PickOuCommand { get; }

    // ---- Dynamisch aus einer Liste ---------------------------------------------------------

    private bool _isList;
    private string _listPath;
    private string _nameColumn;
    private MachineTable? _table;
    private string? _listError;

    public bool IsListSource
    {
        get => _isList;
        set
        {
            if (!Set(ref _isList, value)) return;
            if (value) LoadTable();
            foreach (var p in new[] { nameof(IsStatic), nameof(CanSync), nameof(Source), nameof(Preview), nameof(SyncText) }) OnPropertyChanged(p);
            CommandRequery.Invalidate();
        }
    }

    /// <summary>Pfad der Liste; beim Synchronisieren wird sie neu gelesen.</summary>
    public string ListPath
    {
        get => _listPath;
        set { if (Set(ref _listPath, value ?? string.Empty)) LoadTable(); }
    }

    /// <summary>CSV: Spalte mit den Rechnernamen.</summary>
    public string NameColumn
    {
        get => _table?.NameColumnFor(_nameColumn) ?? _nameColumn;
        set { if (Set(ref _nameColumn, value ?? string.Empty)) ListChanged(); }
    }

    /// <summary>Spalten der Liste (reine Namensliste: nur „Name“).</summary>
    public IReadOnlyList<string> ListColumns => _table?.Columns ?? new[] { MachineTable.NameOnlyColumn };
    public bool IsCsv => _table?.IsCsv == true;
    public System.Collections.ObjectModel.ObservableCollection<ListFilterViewModel> ListFilters { get; } = new();
    public ICommand AddFilterCommand { get; }
    public ICommand ChooseListCommand { get; }

    private string DefaultFilterColumn => _table?.NameColumnFor(_nameColumn) ?? MachineTable.NameOnlyColumn;

    /// <summary>Art der Liste und Zahl der Treffer mit den aktuellen Filtern.</summary>
    public string ListSummary => _listError != null ? L.T("List not readable: ") + _listError
        : _table == null ? L.T("No list chosen yet.")
        : (_table.IsCsv ? L.F("CSV with {0} columns and {1} rows", _table.Columns.Count, _table.Rows.Count) : L.F("Name list with {0} entries", _table.Rows.Count))
          + L.F(" – {0} computers with the filters.", ListMatches.Count);

    private IReadOnlyList<string> ListMatches => _table?.Apply(ListQuery()) ?? Array.Empty<string>();

    public ListGroupQuery ListQuery() => new()
    {
        Path = _listPath.Trim(),
        NameColumn = (_table is { IsCsv: true } ? NameColumn : _nameColumn).Trim(),
        Filters = ListFilters.Select(f => f.ToFilter()).ToList()
    };

    private void ChooseList()
    {
        var file = _app.Dialogs.OpenFile(L.T("Computer lists (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*"), L.T("Choose list for the computer group"));
        if (file != null) ListPath = file;
    }

    /// <summary>Liste einlesen (Spalten, Vorschau); Fehler stehen in der Zusammenfassung.</summary>
    private void LoadTable()
    {
        _table = null;
        _listError = null;
        if (_listPath.Trim().Length > 0)
        {
            try { _table = MachineTable.Parse(File.ReadAllText(_listPath.Trim())); }
            catch (Exception ex) { _listError = ex.Message; }
        }
        OnPropertyChanged(nameof(ListColumns));
        OnPropertyChanged(nameof(IsCsv));
        OnPropertyChanged(nameof(NameColumn));
        ListChanged();
    }

    /// <summary>Filter oder Namensspalte geändert: Vorschau nachziehen.</summary>
    internal void ListChanged()
    {
        foreach (var p in new[] { nameof(ListSummary), nameof(Preview) }) OnPropertyChanged(p);
        if (_isList && _table != null) Machines = string.Join(Environment.NewLine, ListMatches);
    }

    internal void RemoveFilter(ListFilterViewModel f)
    {
        ListFilters.Remove(f);
        ListChanged();
    }

    /// <summary>Liste oder Spaltenwahl aus „Gruppe aus Liste importieren“ setzen.</summary>
    internal void UseList(string path)
    {
        IsListSource = true;
        ListPath = path;
        IsExpanded = true;
    }

    private void Export()
    {
        var file = _app.Dialogs.SaveFile("CSV (*.csv)|*.csv|Text (*.txt)|*.txt", $"{(_name.Trim().Length > 0 ? _name.Trim() : L.T("Computer group"))}.csv",
            L.T("Export computer group"));
        if (file == null) return;
        try
        {
            File.WriteAllText(file, MachineGroupExport.ToFileText(ToGroup(), file.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)), new System.Text.UTF8Encoding(true));
            Status = L.F("{0} exported to {1}.", CountText, file);
        }
        catch (Exception ex)
        {
            Status = L.T("Export failed: ") + ex.Message;
        }
    }

    /// <summary>OU-Baum aus dem AD laden und eine OU für „Ab OU“ wählen lassen.</summary>
    private async Task PickOuAsync()
    {
        IsSearching = true;
        Status = L.T("Reading OUs from Active Directory …");
        try
        {
            var roots = OuTree.Build(await _app.FindOrganizationalUnitsAsync());
            Status = string.Empty;
            if (roots.Count == 0) { Status = L.T("No OUs found."); return; }
            if (_app.Dialogs.PickOrganizationalUnit(roots, _searchBase, _app.SettingsDialogOwner) is { } dn) SearchBase = dn;
        }
        catch (Exception ex)
        {
            Status = L.T("OUs not readable: ") + ex.Message;
        }
        finally { IsSearching = false; }
    }

    // ---- Dynamische Gruppe: Regeln für das Active Directory -----------------------

    private bool _isDynamic;
    private string _searchBase;
    private DateTime? _lastSyncUtc;
    private string? _lastSyncError;

    /// <summary>Rechner kommen aus dem AD (Regelblöcke) statt aus einer festen Liste.</summary>
    public bool IsDynamic
    {
        get => _isDynamic;
        set
        {
            if (!Set(ref _isDynamic, value)) return;
            if (value && Blocks.Count == 0) Blocks.Add(new AdBlockViewModel(this, NewBlock()));
            OnPropertyChanged(nameof(IsStatic));
            OnPropertyChanged(nameof(CanSync));
            OnPropertyChanged(nameof(Source));
            RulesChanged();
            CommandRequery.Invalidate();
        }
    }
    public bool IsStatic => !_isDynamic && !_isList;

    public string SearchBase { get => _searchBase; set { if (Set(ref _searchBase, value ?? string.Empty)) RulesChanged(); } }
    public System.Collections.ObjectModel.ObservableCollection<AdBlockViewModel> Blocks { get; } = new();
    public ICommand AddBlockCommand { get; }
    public ICommand SyncNowCommand { get; }

    private static AdRuleBlock NewBlock() => new() { Rules = { new AdRule { Attribute = "name", Operator = AdOperator.StartsWith } } };

    internal void RemoveBlock(AdBlockViewModel b)
    {
        Blocks.Remove(b);
        RulesChanged();
    }

    internal void RulesChanged()
    {
        for (int i = 0; i < Blocks.Count; i++) Blocks[i].IsFirst = i == 0;
        foreach (var p in new[] { nameof(FilterPreview), nameof(RulesError), nameof(HasRulesError), nameof(RulesSummary), nameof(Preview) }) OnPropertyChanged(p);
    }

    public AdGroupQuery Query() => new() { SearchBase = _searchBase.Trim(), Blocks = Blocks.Select(b => b.ToBlock()).ToList() };
    public string FilterPreview => AdRules.BuildFilter(Query(), DateTime.UtcNow);
    public string? RulesError => _isDynamic ? AdRules.Validate(Query()) : null;
    public bool HasRulesError => RulesError != null;
    public string RulesSummary => AdRules.Describe(Query());

    /// <summary>„Zuletzt aktualisiert …“ bzw. Fehler der letzten Synchronisierung.</summary>
    public string SyncText => _lastSyncError != null
        ? L.F("Last refresh failed: {0} – the previous list ({1}) still applies.", _lastSyncError, CountText)
        : _lastSyncUtc is { } t ? L.F("Last refreshed {0:g} · {1}", t.ToLocalTime(), CountText)
        : _isList ? L.T("Not synced yet – the list is read at startup and regularly.") : L.T("Not read from AD yet.");

    private async Task SyncNowAsync()
    {
        IsSearching = true;
        Status = _isList ? L.T("Reading the list …") : L.T("Reading computers from Active Directory …");
        try
        {
            if (_isList) LoadTable(); // Datei kann sich geändert haben
            var g = ToGroup();
            var names = await _app.SyncMachineGroupAsync(g);
            _lastSyncUtc = g.LastSyncUtc;
            _lastSyncError = g.LastSyncError;
            if (names != null) Machines = string.Join(Environment.NewLine, names);
            Status = names == null ? string.Empty : names.Count == 0 ? (_isList ? L.T("No computers – check the filters.") : L.T("No computers found – check the rules.")) : L.F("{0} computers found.", names.Count);
            OnPropertyChanged(nameof(SyncText));
        }
        finally { IsSearching = false; }
    }

    /// <summary>Nach einer Synchronisierung im Hintergrund: Liste und Stand übernehmen (Regeln bleiben, wie sie im Fenster stehen).</summary>
    internal void ApplySynced(MachineGroup g)
    {
        if (!(_isDynamic && g.AdQuery != null) && !(_isList && g.ListQuery != null)) return;
        _lastSyncUtc = g.LastSyncUtc;
        _lastSyncError = g.LastSyncError;
        Machines = string.Join(Environment.NewLine, g.Machines);
        OnPropertyChanged(nameof(SyncText));
    }

    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public ICommand ToggleCommand { get; }

    /// <summary>Kurzvorschau im eingeklappten Zustand.</summary>
    public string Preview
    {
        get
        {
            if (_isDynamic) return "AD: " + RulesSummary;
            if (_isList) return L.T("List: ") + (_listPath.Trim().Length > 0 ? Path.GetFileName(_listPath.Trim()) : "–") + (ListFilters.Count > 0 ? L.F(" · {0} filters", ListFilters.Count) : string.Empty);
            var names = Parse();
            return names.Count == 0 ? "leer" : string.Join(", ", names.Take(6)) + (names.Count > 6 ? $" … (+{names.Count - 6})" : string.Empty);
        }
    }

    public string Name { get => _name; set { if (Set(ref _name, (value ?? string.Empty).TrimStart('@'))) OnPropertyChanged(nameof(Reference)); } }
    public string Reference => "@" + _name.Trim();
    public string Machines { get => _machines; set { if (Set(ref _machines, value ?? string.Empty)) { OnPropertyChanged(nameof(CountText)); OnPropertyChanged(nameof(Preview)); OnPropertyChanged(nameof(MachinesInline)); } } }

    /// <summary>Dynamische Gruppe: gefundene Rechner kompakt (nur lesen).</summary>
    public string MachinesInline { get { var n = Parse(); return n.Count == 0 ? "–" : string.Join(", ", n); } }
    public string CountText { get { var n = Parse().Count; return n == 1 ? L.T("1 computer") : L.F("{0} computers", n); } }
    public string AdQuery { get => _adQuery; set => Set(ref _adQuery, value ?? string.Empty); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsSearching { get => _searching; private set { if (Set(ref _searching, value)) CommandRequery.Invalidate(); } }

    public ICommand RemoveCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand AdSearchCommand { get; }
    public ICommand SortCommand { get; }

    public IReadOnlyList<string> Parse() => MachineList.ParseNames(_machines);

    private void AddNames(IEnumerable<string> names, string source)
    {
        var existing = Parse().ToList();
        var added = names.Where(n => !existing.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        Machines = string.Join(Environment.NewLine, existing.Concat(added));
        Status = added.Count == 0 ? L.F("{0}: no new computers.", source) : L.F("{0}: {1} computers added.", source, added.Count);
    }

    private void Import()
    {
        var file = _app.Dialogs.OpenFile(L.T("Computer lists (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*"));
        if (file == null) return;
        try
        {
            AddNames(MachineList.ParseImport(File.ReadAllText(file)), Path.GetFileName(file));
        }
        catch (Exception ex)
        {
            Status = L.T("Import failed: ") + ex.Message;
        }
    }

    private async Task SearchAdAsync()
    {
        IsSearching = true;
        Status = L.T("Searching Active Directory …");
        try
        {
            var names = await _app.FindComputersAsync(_adQuery);
            AddNames(names, L.F("AD ({0} found)", names.Count));
        }
        catch (Exception ex)
        {
            Status = L.T("AD search failed: ") + ex.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    public MachineGroup ToGroup() => new()
    {
        Name = _name.Trim(),
        Machines = Parse().ToList(),
        AdQuery = _isDynamic ? Query() : null,
        ListQuery = _isList && !_isDynamic ? ListQuery() : null,
        LastSyncUtc = _isDynamic || _isList ? _lastSyncUtc : null,
        LastSyncError = _isDynamic || _isList ? _lastSyncError : null
    };
}

/// <summary>Filter einer Listengruppe: [Spalte] [Vergleich] [Wert].</summary>
public sealed class ListFilterViewModel : ObservableObject
{
    private readonly MachineGroupViewModel _group;
    private string _column;
    private AdOperator _operator;
    private string _value;

    public ListFilterViewModel(MachineGroupViewModel group, ListFilter filter)
    {
        _group = group;
        _column = filter.Column;
        _operator = filter.Operator;
        _value = filter.Value;
        RemoveCommand = new RelayCommand(() => _group.RemoveFilter(this));
    }

    /// <summary>Spalten der Liste (reine Namensliste: „Name“ – dann filtert man nach dem Namen).</summary>
    public IReadOnlyList<string> Columns => _group.ListColumns;

    public string Column { get => _column; set { if (Set(ref _column, value ?? string.Empty)) _group.ListChanged(); } }

    public IReadOnlyList<KeyValuePair<AdOperator, string>> Operators { get; } =
        MachineTable.Operators.Select(o => new KeyValuePair<AdOperator, string>(o, AdRules.Label(o))).ToList();

    public AdOperator Operator
    {
        get => _operator;
        set { if (Set(ref _operator, value)) { OnPropertyChanged(nameof(NeedsValue)); _group.ListChanged(); } }
    }

    public bool NeedsValue => AdRules.NeedsValue(_operator);
    public string Value { get => _value; set { if (Set(ref _value, value ?? string.Empty)) _group.ListChanged(); } }
    public ICommand RemoveCommand { get; }

    public ListFilter ToFilter() => new() { Column = _column.Trim(), Operator = _operator, Value = _value.Trim() };
}

/// <summary>Regelblock: alle Regeln müssen zutreffen; Blöcke untereinander mit ODER.</summary>
public sealed class AdBlockViewModel : ObservableObject
{
    private readonly MachineGroupViewModel _group;
    private bool _isFirst;

    public AdBlockViewModel(MachineGroupViewModel group, AdRuleBlock block)
    {
        _group = group;
        foreach (var r in block.Rules) Rules.Add(new AdRuleViewModel(this, r));
        AddRuleCommand = new RelayCommand(() => { Rules.Add(new AdRuleViewModel(this, new AdRule { Attribute = "operatingSystem", Operator = AdOperator.Contains })); Changed(); });
        RemoveCommand = new RelayCommand(() => _group.RemoveBlock(this));
    }

    public System.Collections.ObjectModel.ObservableCollection<AdRuleViewModel> Rules { get; } = new();
    public ICommand AddRuleCommand { get; }
    public ICommand RemoveCommand { get; }

    /// <summary>Erster Block: ohne „ODER“ davor.</summary>
    public bool IsFirst { get => _isFirst; set => Set(ref _isFirst, value); }

    internal void RemoveRule(AdRuleViewModel r)
    {
        Rules.Remove(r);
        if (Rules.Count == 0) _group.RemoveBlock(this);
        else Changed();
    }

    internal void Changed() => _group.RulesChanged();

    public AdRuleBlock ToBlock() => new() { Rules = Rules.Select(r => r.ToRule()).ToList() };
}

/// <summary>Eine Regel: [Attribut] [Operator] [Wert].</summary>
public sealed class AdRuleViewModel : ObservableObject
{
    private readonly AdBlockViewModel _block;
    private string _attribute;
    private AdOperator _operator;
    private string _value;

    public AdRuleViewModel(AdBlockViewModel block, AdRule rule)
    {
        _block = block;
        _attribute = rule.Attribute;
        _operator = rule.Operator;
        _value = rule.Value;
        RemoveCommand = new RelayCommand(() => _block.RemoveRule(this));
    }

    /// <summary>Vorgaben (Name, Betriebssystem …); eigene LDAP-Attribute lassen sich eintippen.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> AttributeChoices => Choices;
    private static readonly IReadOnlyList<KeyValuePair<string, string>> Choices =
        AdRules.Attributes.Select(a => new KeyValuePair<string, string>(a.Key, a.Label)).ToList();

    /// <summary>Angezeigter Text im Attributfeld: Bezeichnung der Vorgabe oder der eingetippte Attributname.</summary>
    public string AttributeText
    {
        get => AdRules.Find(_attribute)?.Label ?? _attribute;
        set
        {
            var text = (value ?? string.Empty).Trim();
            var match = AdRules.Attributes.FirstOrDefault(a => a.Label.Equals(text, StringComparison.CurrentCultureIgnoreCase) || a.Key.Equals(text, StringComparison.OrdinalIgnoreCase));
            Attribute = match?.Key ?? text;
        }
    }

    public string Attribute
    {
        get => _attribute;
        set
        {
            if (!Set(ref _attribute, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(AttributeText));
            OnPropertyChanged(nameof(Operators));
            OnPropertyChanged(nameof(ValueHint));
            // Operator passt nicht mehr (z. B. „enthält“ bei „Konto aktiviert“): den ersten passenden nehmen
            if (!AdRules.OperatorsFor(_attribute).Contains(_operator)) Operator = AdRules.OperatorsFor(_attribute)[0];
            _block.Changed();
        }
    }

    public IReadOnlyList<KeyValuePair<AdOperator, string>> Operators =>
        AdRules.OperatorsFor(_attribute).Select(o => new KeyValuePair<AdOperator, string>(o, AdRules.Label(o, _attribute))).ToList();

    public AdOperator Operator
    {
        get => _operator;
        set { if (Set(ref _operator, value)) { OnPropertyChanged(nameof(NeedsValue)); _block.Changed(); } }
    }

    public bool NeedsValue => AdRules.NeedsValue(_operator);
    public string Value { get => _value; set { if (Set(ref _value, value ?? string.Empty)) _block.Changed(); } }
    public string ValueHint => AdRules.Find(_attribute)?.ValueHint ?? L.T("Value");
    public ICommand RemoveCommand { get; }

    public AdRule ToRule() => new() { Attribute = _attribute.Trim(), Operator = _operator, Value = _value.Trim() };
}

/// <summary>Ein Arbeitsbereich in der Liste der Einstellungen.</summary>
public sealed class WorkspaceRowViewModel : ObservableObject
{
    private string _name;

    public WorkspaceRowViewModel(string name, bool isActive, string summary, Action<WorkspaceRowViewModel> activate, Action<WorkspaceRowViewModel> remove)
    {
        OriginalName = name;
        _name = name;
        IsActive = isActive;
        Summary = summary;
        ActivateCommand = new RelayCommand(() => activate(this), () => !IsActive);
        RemoveCommand = new RelayCommand(() => remove(this), () => !IsActive);
    }

    public string OriginalName { get; }
    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }
    public bool IsActive { get; }
    public string Summary { get; }
    public ICommand ActivateCommand { get; }
    public ICommand RemoveCommand { get; }

    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public ICommand ToggleCommand => new RelayCommand(() => IsExpanded = !IsExpanded);

    /// <summary>Welche Gruppen in diesem Arbeitsbereich im Panel erscheinen.</summary>
    public System.Collections.ObjectModel.ObservableCollection<WorkspaceGroupChoice> Groups { get; } = new();
}


/// <summary>Gruppe in der Auswahl eines Arbeitsbereichs (sichtbar oder ausgeblendet).</summary>
public sealed class WorkspaceGroupChoice : ObservableObject
{
    private readonly Workspace _ws;
    private readonly Action _changed;

    public WorkspaceGroupChoice(Workspace ws, string path, string icon, Action changed)
    {
        _ws = ws;
        Path = path;
        Icon = icon;
        _changed = changed;
    }

    public string Path { get; }
    public string Icon { get; }
    public string Display => IsFixed ? FixedGroups.Title(Path) : GroupManager.LeafOf(Path);
    /// <summary>Einrückung links in Pixeln (Untergruppen tiefer).</summary>
    public double IndentLeft => IsFixed ? 0 : GroupManager.DepthOf(Path) * 22;
    /// <summary>Feste Gruppe (Schloss-Symbol): nur ein-/ausblendbar.</summary>
    public bool IsFixed => FixedGroups.IsFixed(Path);
    public string FixedGlyph => FixedGroups.Glyph(Path);

    /// <summary>Die Gruppe selbst ist nicht ausgeblendet.</summary>
    public bool IsVisible
    {
        get => !_ws.HiddenGroups.Contains(Path, StringComparer.CurrentCultureIgnoreCase);
        set { WorkspaceManager.SetGroupHidden(_ws, Path, !value); OnPropertyChanged(); _changed(); }
    }

    /// <summary>Übergeordnete Gruppe ist sichtbar (sonst ist diese automatisch mit ausgeblendet).</summary>
    public bool IsEnabled => GroupManager.ParentOf(Path) is not { } parent || WorkspaceManager.ShowsGroupIn(_ws, parent);

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsEnabled));
    }
}
