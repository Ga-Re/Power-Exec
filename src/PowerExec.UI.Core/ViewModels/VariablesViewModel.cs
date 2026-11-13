using PowerExec.Core.Localization;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;

/// <summary>Einstellungsseite „Variablen“: eingebaute und eigene {{$Variablen}}, Datums-/Zeitformat.</summary>
public sealed class VariablesViewModel : ObservableObject
{
    private readonly ISettingsHost _app;
    private readonly AppSettings _s;

    public VariablesViewModel(ISettingsHost app, AppSettings workingCopy, Action<string>? copyText = null)
    {
        _app = app;
        _s = workingCopy;
        CopyText = copyText ?? (_ => { });
        Resolver = new VariableResolver(() => _s, app.RunVariableCommand, SecretProtector.Unprotect, () => string.Empty);
        foreach (var v in _s.Variables) Items.Add(new VariableRowViewModel(this, v));
        AddCommand = new RelayCommand(Add);
        SelectAllCommand = new RelayCommand(() =>
        {
            var all = VisibleItems.All(i => i.IsSelected);
            foreach (var i in VisibleItems) i.IsSelected = !all;
        });
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => Items.Any(i => i.IsSelected));
        Refilter();
        Builtins = Variables.Builtins.Select(b => new BuiltinVariableRow("{{$" + b.Name + "}}", b.Description, () => Resolver.Preview(b.Name))).ToList();
    }

    internal VariableResolver Resolver { get; }
    internal AppSettings Settings => _s;
    internal ISettingsHost App => _app;
    internal Action<string> CopyText { get; }

    public IReadOnlyList<BuiltinVariableRow> Builtins { get; }
    public ObservableCollection<VariableRowViewModel> Items { get; } = new();
    /// <summary>Nach Suche gefilterte Zeilen der Tabelle.</summary>
    public ObservableCollection<VariableRowViewModel> VisibleItems { get; } = new();
    public bool HasNoItems => Items.Count == 0;
    public ICommand AddCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand RemoveSelectedCommand { get; }

    private string _query = string.Empty;
    public string Query { get => _query; set { if (Set(ref _query, value ?? string.Empty)) Refilter(); } }

    public int SelectedCount => Items.Count(i => i.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public string SelectionText => SelectedCount == 1 ? L.T("1 variable selected") : L.F("{0} variables selected", SelectedCount);
    public string SummaryText => Items.Count == VisibleItems.Count ? (Items.Count == 1 ? L.T("1 variable") : L.F("{0} variables", Items.Count)) : L.F("{0} of {1}", VisibleItems.Count, Items.Count);

    internal void Refilter()
    {
        var terms = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        VisibleItems.Clear();
        foreach (var i in Items.Where(i => terms.All(t => i.SearchText.Contains(t, StringComparison.CurrentCultureIgnoreCase))))
            VisibleItems.Add(i);
        foreach (var p in new[] { nameof(SummaryText), nameof(HasNoItems) }) OnPropertyChanged(p);
        SelectionChanged();
    }

    internal void SelectionChanged()
    {
        foreach (var p in new[] { nameof(SelectedCount), nameof(HasSelection), nameof(SelectionText) }) OnPropertyChanged(p);
        CommandRequery.Invalidate();
    }

    private void RemoveSelected()
    {
        var rows = Items.Where(i => i.IsSelected).ToList();
        if (rows.Count == 0 || !_app.Dialogs.Confirm(rows.Count == 1 ? L.F("Delete variable {0}?", rows[0].Token) : L.F("Delete {0} variables?", rows.Count),
                L.T("Delete variables"), destructive: true, owner: _app.SettingsDialogOwner)) return;
        foreach (var r in rows) Remove(r);
    }

    public string DateFormat
    {
        get => _s.DateFormat;
        set { _s.DateFormat = string.IsNullOrWhiteSpace(value) ? Variables.DefaultDateFormat : value.Trim(); OnPropertyChanged(); RefreshBuiltins(); }
    }

    public string TimeFormat
    {
        get => _s.TimeFormat;
        set { _s.TimeFormat = string.IsNullOrWhiteSpace(value) ? Variables.DefaultTimeFormat : value.Trim(); OnPropertyChanged(); RefreshBuiltins(); }
    }

    public IReadOnlyList<string> DateFormats { get; } = new[] { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd.MM.yy", "dddd, d. MMMM yyyy" };
    public IReadOnlyList<string> TimeFormats { get; } = new[] { "HH:mm", "HH:mm:ss", "H:mm 'Uhr'", "HHmm" };

    private void RefreshBuiltins()
    {
        foreach (var b in Builtins) b.Refresh();
    }

    private void Add()
    {
        var name = "NeueVariable";
        for (int i = 2; Variables.Find(_s, name) != null; i++) name = "NeueVariable" + i;
        var v = new UserVariable { Name = name };
        _s.Variables.Add(v);
        var row = new VariableRowViewModel(this, v);
        Items.Add(row);
        Refilter();
        // Neue Variable gleich im Dialog bearbeiten; Abbrechen verwirft sie wieder
        if (!row.Edit(isNew: true)) Remove(row);
    }

    /// <summary>
    /// Neue Variable vorbelegt anlegen und im Dialog bearbeiten (z. B. von einem Marktplatz-Eintrag verlangt).
    /// Gibt es sie schon, passiert nichts. False = abgebrochen (dann wird sie nicht angelegt).
    /// </summary>
    public bool AddFor(string name, VariableKind kind, string description, string suggestion)
    {
        name = Variables.Normalize(name);
        if (Variables.Find(_s, name) != null) return true;
        var v = new UserVariable
        {
            Name = name,
            Kind = kind,
            Description = description,
            // Geheimes nie vorbelegen (wird verschlüsselt gespeichert); sonst Vorschlag bzw. Befehl übernehmen
            Value = kind == VariableKind.Secret ? string.Empty : suggestion
        };
        _s.Variables.Add(v);
        var row = new VariableRowViewModel(this, v);
        Items.Add(row);
        Refilter();
        if (row.Edit(isNew: true)) return true;
        Remove(row);
        return false;
    }

    internal void Remove(VariableRowViewModel row)
    {
        _s.Variables.Remove(row.Model);
        Items.Remove(row);
        Resolver.Invalidate(row.Model.Name);
        Refilter();
    }

    /// <summary>Namen der Arbeitsbereiche (für Werte je Arbeitsbereich).</summary>
    internal IReadOnlyList<string> WorkspaceNames =>
        _s.Workspaces.Count > 0 ? _s.Workspaces.Select(w => w.Name).ToList() : new[] { _s.ActiveWorkspace };
}

/// <summary>Eingebaute Variable (nur Anzeige mit aktuellem Wert).</summary>
public sealed class BuiltinVariableRow : ObservableObject
{
    private readonly Func<string> _preview;
    public BuiltinVariableRow(string token, string description, Func<string> preview)
    {
        Token = token;
        Description = description;
        _preview = preview;
    }
    public string Token { get; }
    public string Description { get; }
    public string Preview => _preview();
    public void Refresh() => OnPropertyChanged(nameof(Preview));
}

/// <summary>Eine eigene Variable in den Einstellungen.</summary>
public sealed class VariableRowViewModel : ObservableObject
{
    private readonly VariablesViewModel _page;
    private string _name;
    private string? _nameError;
    private bool _isExpanded;
    private string _status = string.Empty;
    private bool _testing;

    public VariableRowViewModel(VariablesViewModel page, UserVariable model)
    {
        _page = page;
        Model = model;
        _name = model.Name;
        RemoveCommand = new RelayCommand(() =>
        {
            if (_page.App.Dialogs.Confirm(L.F("Delete variable {0}? Entries that use it won't start anymore.", Token), L.T("Delete variable"),
                    destructive: true, owner: _page.App.SettingsDialogOwner))
                _page.Remove(this);
        });
        EditCommand = new RelayCommand(() => Edit(isNew: false));
        TestCommand = new RelayCommand(async () => await TestAsync(), () => !_testing);
        CopyTokenCommand = new RelayCommand(() => { _page.CopyText(Token); Snackbar.Show(Token + L.T(" copied to the clipboard.")); });
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        WorkspaceValues = _page.WorkspaceNames.Select(n => new WorkspaceValueRow(this, n)).ToList();
    }

    internal UserVariable Model { get; }

    private bool _selected;
    /// <summary>Häkchen in der Tabelle (Mehrfachauswahl).</summary>
    public bool IsSelected { get => _selected; set { if (Set(ref _selected, value)) _page.SelectionChanged(); } }

    internal string SearchText => $"{Model.Name} {Model.Description} {KindText} {(IsSecret ? string.Empty : Model.Value)}";

    public ICommand EditCommand { get; }
    public bool IsNew { get; private set; }

    /// <summary>Im Dialog bearbeiten; Abbrechen stellt den Stand vor dem Öffnen wieder her.</summary>
    internal bool Edit(bool isNew)
    {
        IsNew = isNew;
        var before = Model.Clone();
        if (_page.App.Dialogs.EditVariable(this, _page.App.SettingsDialogOwner))
        {
            _page.Refilter();
            RaiseAll();
            return true;
        }
        Model.Name = before.Name; Model.Kind = before.Kind; Model.Value = before.Value; Model.Description = before.Description;
        Model.Refresh = before.Refresh; Model.CacheMinutes = before.CacheMinutes; Model.WorkspaceValues = before.WorkspaceValues;
        _name = Model.Name;
        _nameError = null;
        _page.Resolver.Invalidate(Model.Name);
        RaiseAll();
        return false;
    }

    private void RaiseAll()
    {
        OnPropertyChanged(string.Empty);
        foreach (var w in WorkspaceValues) w.Raise();
    }

    /// <summary>Dialog darf schließen: Name gültig.</summary>
    public bool CanSave => _nameError == null && Model.Name.Length > 0;

    public string Name
    {
        get => _name;
        set
        {
            _name = Variables.Normalize(value);
            _nameError = Variables.ValidateName(_page.Settings, _name, Model);
            if (_nameError == null)
            {
                _page.Resolver.Invalidate(Model.Name);
                Model.Name = _name;
            }
            foreach (var p in new[] { nameof(Name), nameof(NameError), nameof(HasNameError), nameof(Token), nameof(CanSave) }) OnPropertyChanged(p);
            RefreshPreview();
        }
    }

    public string? NameError => _nameError;
    public bool HasNameError => _nameError != null;
    public string Token => "{{$" + Model.Name + "}}";

    public VariableKind Kind
    {
        get => Model.Kind;
        set
        {
            if (Model.Kind == value) return;
            // Wechsel zu/von „geheim“: der gespeicherte Wert ist dann verschlüsselt bzw. Klartext – neu eingeben lassen
            if (value == VariableKind.Secret || Model.Kind == VariableKind.Secret) Model.Value = string.Empty;
            Model.Kind = value;
            _page.Resolver.Invalidate(Model.Name);
            foreach (var p in new[] { nameof(Kind), nameof(IsText), nameof(IsWorkspace), nameof(IsSecret), nameof(IsEnvironment), nameof(IsPowerShell),
                         nameof(Value), nameof(ValueLabel), nameof(KindText), nameof(IsCached), nameof(SecretValue) }) OnPropertyChanged(p);
            RefreshPreview();
        }
    }

    public static IReadOnlyList<KeyValuePair<VariableKind, string>> KindChoices { get; } =
        Enum.GetValues<VariableKind>().Select(k => new KeyValuePair<VariableKind, string>(k, Variables.Label(k))).ToList();
    public IReadOnlyList<KeyValuePair<VariableKind, string>> Kinds => KindChoices;
    public string KindText => Variables.Label(Model.Kind);

    public bool IsText => Model.Kind == VariableKind.Text;
    public bool IsWorkspace => Model.Kind == VariableKind.Workspace;
    public bool IsSecret => Model.Kind == VariableKind.Secret;
    public bool IsEnvironment => Model.Kind == VariableKind.Environment;
    public bool IsPowerShell => Model.Kind == VariableKind.PowerShell;

    /// <summary>Wert (nicht bei „geheim“): Text, Standardwert, Name der Umgebungsvariable bzw. Befehl.</summary>
    public string Value
    {
        get => Model.Kind == VariableKind.Secret ? string.Empty : Model.Value;
        set
        {
            if (Model.Kind == VariableKind.Secret) return;
            Model.Value = value ?? string.Empty;
            _page.Resolver.Invalidate(Model.Name);
            OnPropertyChanged();
            RefreshPreview();
        }
    }

    public string ValueLabel => Model.Kind switch
    {
        VariableKind.Workspace => L.T("Default value"),
        VariableKind.Environment => L.T("Environment variable"),
        VariableKind.PowerShell => L.T("PowerShell command"),
        _ => L.T("Value")
    };

    /// <summary>Geheimer Wert: wird beim Setzen verschlüsselt; gelesen wird nie der Klartext.</summary>
    public string SecretValue
    {
        get => string.Empty;
        set
        {
            if (Model.Kind != VariableKind.Secret || string.IsNullOrEmpty(value)) return;
            Model.Value = SecretProtector.Protect(value);
            OnPropertyChanged(nameof(HasSecret));
            RefreshPreview();
        }
    }

    public bool HasSecret => Model.Kind == VariableKind.Secret && Model.Value.Length > 0;

    public IReadOnlyList<WorkspaceValueRow> WorkspaceValues { get; }

    public VariableRefresh Refresh
    {
        get => Model.Refresh;
        set { Model.Refresh = value; _page.Resolver.Invalidate(Model.Name); OnPropertyChanged(); OnPropertyChanged(nameof(IsCached)); }
    }

    public static IReadOnlyList<KeyValuePair<VariableRefresh, string>> RefreshChoices { get; } =
        Enum.GetValues<VariableRefresh>().Select(r => new KeyValuePair<VariableRefresh, string>(r, Variables.Label(r))).ToList();
    public IReadOnlyList<KeyValuePair<VariableRefresh, string>> Refreshes => RefreshChoices;
    public bool IsCached => IsPowerShell && Model.Refresh == VariableRefresh.Cached;

    public int CacheMinutes
    {
        get => Model.CacheMinutes;
        set { Model.CacheMinutes = Math.Clamp(value, 1, 24 * 60); OnPropertyChanged(); }
    }

    public string Description { get => Model.Description; set { Model.Description = value ?? string.Empty; OnPropertyChanged(); } }

    /// <summary>Aktueller Wert (Geheimes verdeckt, PowerShell erst nach „Jetzt ermitteln“).</summary>
    public string Preview => _page.Resolver.Preview(Model.Name);
    internal void RefreshPreview() => OnPropertyChanged(nameof(Preview));

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public ICommand ToggleCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand CopyTokenCommand { get; }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsTesting { get => _testing; private set { if (Set(ref _testing, value)) CommandRequery.Invalidate(); } }

    /// <summary>Wert jetzt ermitteln (PowerShell: Befehl ausführen) und anzeigen.</summary>
    private async Task TestAsync()
    {
        IsTesting = true;
        Status = IsPowerShell ? L.T("Running the command …") : string.Empty;
        try
        {
            _page.Resolver.Invalidate(Model.Name);
            var name = Model.Name;
            var value = await Task.Run(() => _page.Resolver.Resolve(name));
            Status = IsSecret ? L.T("Secret value can be read.") : L.F("Result: {0}", value);
        }
        catch (Exception ex)
        {
            Status = L.T("Error: ") + ex.Message;
        }
        finally
        {
            IsTesting = false;
            RefreshPreview();
        }
    }
}

/// <summary>Wert einer Variable für einen Arbeitsbereich.</summary>
public sealed class WorkspaceValueRow : ObservableObject
{
    private readonly VariableRowViewModel _row;
    public WorkspaceValueRow(VariableRowViewModel row, string workspace)
    {
        _row = row;
        Workspace = workspace;
    }
    public string Workspace { get; }
    internal void Raise() => OnPropertyChanged(nameof(Value));
    public string Value
    {
        get => VariableResolver.WorkspaceValue(_row.Model, Workspace) ?? string.Empty;
        set
        {
            foreach (var k in _row.Model.WorkspaceValues.Keys.Where(k => k.Equals(Workspace, StringComparison.OrdinalIgnoreCase)).ToList())
                _row.Model.WorkspaceValues.Remove(k);
            if (!string.IsNullOrWhiteSpace(value)) _row.Model.WorkspaceValues[Workspace] = value;
            OnPropertyChanged();
            _row.RefreshPreview();
        }
    }
}
