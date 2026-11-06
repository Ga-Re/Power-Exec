using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;


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
        AdSearchCommand = new RelayCommand(() => _ = SearchAdAsync(), () => !_searching && _adQuery.Trim().Length > 0);
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
        SyncNowCommand = new RelayCommand(() => _ = SyncNowAsync(), () => !_searching && _isDynamic);
        RulesChanged();
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
            RulesChanged();
            CommandRequery.Invalidate();
        }
    }
    public bool IsStatic => !_isDynamic;

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
        ? $"Letzte Aktualisierung fehlgeschlagen: {_lastSyncError} – die bisherige Liste ({CountText}) gilt weiter."
        : _lastSyncUtc is { } t ? $"Zuletzt aktualisiert {t.ToLocalTime():dd.MM. HH:mm} · {CountText}" : "Noch nicht aus dem AD gelesen.";

    private async Task SyncNowAsync()
    {
        IsSearching = true;
        Status = "Lese Rechner aus dem Active Directory …";
        try
        {
            var g = ToGroup();
            var names = await _app.SyncMachineGroupAsync(g);
            _lastSyncUtc = g.LastSyncUtc;
            _lastSyncError = g.LastSyncError;
            if (names != null) Machines = string.Join(Environment.NewLine, names);
            Status = names == null ? string.Empty : names.Count == 0 ? "Keine Rechner gefunden – Regeln prüfen." : $"{names.Count} Rechner gefunden.";
            OnPropertyChanged(nameof(SyncText));
        }
        finally { IsSearching = false; }
    }

    /// <summary>Nach einer Synchronisierung im Hintergrund: Liste und Stand übernehmen (Regeln bleiben, wie sie im Fenster stehen).</summary>
    internal void ApplySynced(MachineGroup g)
    {
        if (!_isDynamic || g.AdQuery == null) return;
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
            var names = Parse();
            return names.Count == 0 ? "leer" : string.Join(", ", names.Take(6)) + (names.Count > 6 ? $" … (+{names.Count - 6})" : string.Empty);
        }
    }

    public string Name { get => _name; set { if (Set(ref _name, (value ?? string.Empty).TrimStart('@'))) OnPropertyChanged(nameof(Reference)); } }
    public string Reference => "@" + _name.Trim();
    public string Machines { get => _machines; set { if (Set(ref _machines, value ?? string.Empty)) { OnPropertyChanged(nameof(CountText)); OnPropertyChanged(nameof(Preview)); OnPropertyChanged(nameof(MachinesInline)); } } }

    /// <summary>Dynamische Gruppe: gefundene Rechner kompakt (nur lesen).</summary>
    public string MachinesInline { get { var n = Parse(); return n.Count == 0 ? "–" : string.Join(", ", n); } }
    public string CountText { get { var n = Parse().Count; return n == 1 ? "1 Rechner" : $"{n} Rechner"; } }
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
        Status = added.Count == 0 ? $"{source}: keine neuen Rechner." : $"{source}: {added.Count} Rechner hinzugefügt.";
    }

    private void Import()
    {
        var file = _app.Dialogs.OpenFile("Rechnerlisten (*.csv;*.txt)|*.csv;*.txt|Alle Dateien (*.*)|*.*");
        if (file == null) return;
        try
        {
            AddNames(MachineList.ParseImport(File.ReadAllText(file)), Path.GetFileName(file));
        }
        catch (Exception ex)
        {
            Status = "Import fehlgeschlagen: " + ex.Message;
        }
    }

    private async Task SearchAdAsync()
    {
        IsSearching = true;
        Status = "Suche im Active Directory …";
        try
        {
            var names = await _app.FindComputersAsync(_adQuery);
            AddNames(names, $"AD ({names.Count} gefunden)");
        }
        catch (Exception ex)
        {
            Status = "AD-Suche fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    public MachineGroup ToGroup() => new()
    {
        Name = _name.Trim(), Machines = Parse().ToList(),
        AdQuery = _isDynamic ? Query() : null,
        LastSyncUtc = _isDynamic ? _lastSyncUtc : null,
        LastSyncError = _isDynamic ? _lastSyncError : null
    };
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
    public string ValueHint => AdRules.Find(_attribute)?.ValueHint ?? "Wert";
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
