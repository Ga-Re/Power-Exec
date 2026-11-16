using PowerExec.Core.Localization;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;

public sealed class ProfileParamViewModel : ObservableObject
{
    private readonly ProfileEditViewModel _owner;
    private ParameterMode _mode;
    private string _value;

    public ProfileParamViewModel(ProfileEditViewModel owner, ScriptParameter p, ParameterSetting setting, string groupKey)
    {
        _owner = owner;
        Parameter = p;
        _mode = setting.Mode;
        _value = setting.Value;
        GroupKey = groupKey + ":" + p.Name;
        Inherit = new InheritToggle(owner, ProfileInheritance.ParamPrefix + p.Name);
    }

    /// <summary>Geerbt vom Standard oder eigen.</summary>
    public InheritToggle Inherit { get; }

    /// <summary>Wert vom Standard übernehmen (geerbt), ohne Änderungsmeldung an das Profil.</summary>
    internal void CopyFrom(ParameterSetting s)
    {
        _mode = s.Mode;
        _value = s.Value;
        foreach (var n in new[] { nameof(Mode), nameof(Value), nameof(BoolValue), nameof(IsValueEnabled), nameof(Note), nameof(NoteIsWarning) }) OnPropertyChanged(n);
    }

    public ScriptParameter Parameter { get; }
    public string GroupKey { get; }
    public string Name => Parameter.Name;
    public string TypeLabel => Parameter.TypeName.Length == 0 ? string.Empty : $"[{Parameter.TypeName}]";
    public bool IsSecure => Parameter.Kind == ParameterKind.Secure;
    public bool IsNotSecure => !IsSecure;
    public bool IsCheck => Parameter.Kind is ParameterKind.Switch or ParameterKind.Boolean;
    public bool IsChoice => Parameter.Kind == ParameterKind.Choice;
    public bool IsText => !IsCheck && !IsChoice && !IsSecure;
    public IReadOnlyList<string> Choices => Parameter.ValidValues;

    public string Note
    {
        get
        {
            if (IsSecure) return L.T("Never saved – the script asks itself at launch.");
            var help = string.IsNullOrWhiteSpace(Parameter.HelpText) ? string.Empty : Parameter.HelpText;
            if (Parameter.IsMandatory && _mode == ParameterMode.Ask) return L.T("Required · asked for at launch");
            return Parameter.IsMandatory ? L.T("Required") + (help.Length > 0 ? " · " + help : string.Empty) : help;
        }
    }

    public bool NoteIsWarning => Parameter.IsMandatory && _mode == ParameterMode.Ask;

    public ParameterMode Mode
    {
        get => _mode;
        set
        {
            if (!Set(ref _mode, value)) return;
            OnPropertyChanged(nameof(IsValueEnabled));
            OnPropertyChanged(nameof(Note));
            OnPropertyChanged(nameof(NoteIsWarning));
            _owner.OnChanged();
        }
    }

    public bool IsValueEnabled => _mode != ParameterMode.Ask;

    public string Value
    {
        get => _value;
        set { if (Set(ref _value, value ?? string.Empty)) { OnPropertyChanged(nameof(BoolValue)); _owner.OnChanged(); } }
    }

    public bool BoolValue
    {
        get => _value.Equals("true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }

    public ParameterSetting ToSetting() => new() { Mode = _mode, Value = _value };
}

public sealed class ProfileEditViewModel : ObservableObject
{
    private readonly ProfilesViewModel _owner;
    private string _name;
    private bool _isDefault;
    private bool _runAsAdmin;
    private bool _runAsOther;
    private PsEngine _engine;
    private bool _noExit;
    private bool _noProfile;
    private ProfileTarget _target;
    private string _machines;
    private DialogMode _dialog;
    private string _arguments;
    private bool _confirm;
    private string? _targetText;
    private string? _code;
    /// <summary>Eigene Werte (Schlüssel aus <see cref="ProfileInheritance"/>); im Standardprofil ohne Bedeutung.</summary>
    private readonly HashSet<string> _own = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Stand des Standards beim Anpassen je eigenem Wert.</summary>
    private readonly Dictionary<string, string> _ownBase = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gespeicherte Variablen beim Öffnen (bis die Zeilen aufgebaut sind).</summary>
    private Dictionary<string, ProfileVariable> _variables;
    private static int _counter;

    public ProfileEditViewModel(ProfilesViewModel owner, LaunchProfile p)
    {
        _owner = owner;
        _name = p.Name;
        _isDefault = p.IsDefault;
        _runAsAdmin = p.RunAsAdmin;
        _runAsOther = p.RunAsOtherUser && !p.RunAsAdmin;
        _engine = p.Engine;
        _noExit = p.NoExit;
        _noProfile = p.NoProfile;
        _target = p.Target;
        _machines = string.Join(", ", p.Machines);
        _dialog = p.Dialog;
        _arguments = p.Arguments;
        _confirm = p.ConfirmBeforeRun;
        _targetText = p.TargetText;
        _code = p.Code;
        _variables = p.Variables.ToDictionary(kv => kv.Key, kv => kv.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        if (p.Own != null) foreach (var k in p.Own) _own.Add(k);
        if (p.OwnBase != null) foreach (var kv in p.OwnBase) _ownBase[kv.Key] = kv.Value;
        TargetInherit = new InheritToggle(this, ProfileInheritance.Target);
        RunAsInherit = new InheritToggle(this, ProfileInheritance.RunAs);
        WindowInherit = new InheritToggle(this, ProfileInheritance.Window);
        ArgumentsInherit = new InheritToggle(this, ProfileInheritance.Arguments);
        TestCommand = new RelayCommand(() => _owner.Test(this));
        EditCodeCommand = new RelayCommand(StartEditCode, () => CanEditFile && !IsEditingCode);
        SaveCodeCommand = new RelayCommand(SaveCode, () => IsEditingCode);
        CancelEditCodeCommand = new RelayCommand(() => { IsEditingCode = false; LoadCode(); }, () => IsEditingCode);
        AddVariableCommand = new RelayCommand(AddVariable);
        AdoptStandardCommand = new RelayCommand(AdoptStandard);
        DismissStandardChangeCommand = new RelayCommand(DismissStandardChange);
        _key = "p" + Interlocked.Increment(ref _counter);
        foreach (var param in owner.Item.Parameters)
            Parameters.Add(new ProfileParamViewModel(this, param, ProfileResolver.SettingFor(p, param), _key));
        SelectCommand = new RelayCommand(() => _owner.Selected = this);
        if (owner.Entry != null)
        {
            // Startoptionen dieses Profils (Browser, RDP-Anzeige …); ohne eigene Werte die des Eintrags
            Options = new CustomEntryViewModel(owner.Entry.Current.WithProfile(p), owner.App, optionsOnly: true);
            Options.Changed += () => { OnPropertyChanged(nameof(Detail)); OnChanged(); };
        }
    }

    /// <summary>Startoptionen eines eigenen Eintrags (null bei Dateien aus Ordnern).</summary>
    public CustomEntryViewModel? Options { get; }
    public bool HasOptions => Options?.HasOptions == true;

    /// <summary>Kurzinfo in der Profilliste, z. B. „Edge · Arbeit“ bzw. „Admin · 2 Rechner“.</summary>
    public string Detail => Options is { HasOptions: true } o && o.OptionsSummary.Length > 0
        ? o.OptionsSummary + (_runAsAdmin ? L.T(" · Admin") : _runAsOther ? L.T(" · different user") : string.Empty)
        : Summary;

    private readonly string _key;

    /// <summary>Parameter des Scripts haben sich geändert (Snippet bearbeitet): Liste neu aufbauen, Werte gleichnamiger Parameter behalten.</summary>
    internal void RefreshParameters()
    {
        var old = Parameters.ToDictionary(x => x.Name, x => x.ToSetting(), StringComparer.OrdinalIgnoreCase);
        Parameters.Clear();
        foreach (var param in _owner.Item.Parameters)
            Parameters.Add(new ProfileParamViewModel(this, param, old.TryGetValue(param.Name, out var st) ? st : new ParameterSetting(), _key));
        OnChanged();
    }

    public ObservableCollection<ProfileParamViewModel> Parameters { get; } = new();
    public ICommand SelectCommand { get; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; internal set => Set(ref _isSelected, value); }

    public string Name { get => _name; set { if (Set(ref _name, value ?? string.Empty)) OnChanged(); } }

    public bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (value && !_isDefault) _owner.MakeDefault(this);
            // Abwählen nicht möglich: genau ein Profil ist immer Standard
        }
    }

    internal void SetDefaultSilently(bool value)
    {
        _isDefault = value;
        OnPropertyChanged(nameof(IsDefault));
        RefreshInheritance();
    }

    public bool RunAsAdmin
    {
        get => _runAsAdmin;
        set { if (Set(ref _runAsAdmin, value)) { if (value) _runAsOther = false; OnPropertyChanged(nameof(RunMode)); OnChanged(); } }
    }

    /// <summary>„normal“, „admin“ oder „other“ (anderer Benutzer – Anmeldung beim Start, nie gespeichert).</summary>
    public string RunMode
    {
        get => _runAsAdmin ? "admin" : _runAsOther ? "other" : "normal";
        set
        {
            _runAsAdmin = value == "admin";
            _runAsOther = value == "other";
            OnPropertyChanged(nameof(RunAsAdmin));
            OnPropertyChanged();
            OnChanged();
        }
    }
    public PsEngine Engine { get => _engine; set { if (Set(ref _engine, value)) OnChanged(); } }
    public bool NoExit { get => _noExit; set { if (Set(ref _noExit, value)) OnChanged(); } }
    public bool NoProfile { get => _noProfile; set { if (Set(ref _noProfile, value)) OnChanged(); } }

    public ProfileTarget Target
    {
        get => _target;
        set { if (Set(ref _target, value)) { OnPropertyChanged(nameof(IsFixedTarget)); OnChanged(); } }
    }

    public bool IsFixedTarget => _target == ProfileTarget.Fixed;
    public string Machines { get => _machines; set { if (Set(ref _machines, value ?? string.Empty)) OnChanged(); } }
    public DialogMode Dialog { get => _dialog; set { if (Set(ref _dialog, value)) OnChanged(); } }
    public string Arguments { get => _arguments; set { if (Set(ref _arguments, value ?? string.Empty)) OnChanged(); } }
    public bool ConfirmBeforeRun { get => _confirm; set { if (Set(ref _confirm, value)) OnChanged(); } }

    public string Summary => ProfileResolver.Summary(ToProfile(), _owner.Item);
    public bool CanBeDeleted => _owner.Profiles.Count > 1;

    public string DialogHint
    {
        get
        {
            var p = ToProfile();
            bool needs = ProfileResolver.NeedsDialog(p, _owner.Item);
            return _dialog switch
            {
                DialogMode.Always => L.T("The dialog shows the profile's values; changes apply only to this launch."),
                DialogMode.Never when needs => L.T("Note: a required field is asked for or computers are missing – the dialog appears anyway."),
                DialogMode.Never => L.T("A click in the panel starts immediately."),
                _ when needs => L.T("The dialog appears because something still needs input."),
                _ => L.T("Everything is set – the dialog is skipped.")
            };
        }
    }

    public string Preview
    {
        get
        {
            var p = ToProfile();
            var item = _owner.ItemFor(p);
            ParameterValueConverter.TryConvert(item.Parameters, ProfileResolver.InitialValues(p, item), out var values);
            var request = new ExecutionRequest
            {
                Item = item,
                Mode = ProfileResolver.ModeOf(p),
                Engine = p.Engine,
                NoExit = p.NoExit,
                NoProfile = p.NoProfile,
                Parameters = values,
                ExtraArguments = p.Arguments,
                Targets = item.SupportsRemote && p.Target == ProfileTarget.Fixed ? p.Machines : Array.Empty<string>()
            };
            if (item.SupportsRemote && p.Target != ProfileTarget.Local) return CommandBuilder.RemotePreview(request);
            var preview = CommandBuilder.BuildLocal(request).Preview;
            return p.RunAsAdmin ? preview + L.T("   (as administrator)") : p.RunAsOtherUser ? preview + L.T("   (as different user)") : preview;
        }
    }

    internal void OnChanged()
    {
        _owner.OnProfileChanged(this);
        RaiseDerived();
        if (_owner.Entry?.Current.Kind == CustomEntryKind.Snippet) LoadCode();
    }

    private void RaiseDerived()
    {
        foreach (var n in new[] { nameof(Summary), nameof(Detail), nameof(DialogHint), nameof(Preview), nameof(OwnCount), nameof(HasOwnValues), nameof(InheritText),
                     nameof(HeaderHint), nameof(HasStandardChanged), nameof(StandardChangedText), nameof(ShowTargetOverride), nameof(ShowEntryForm), nameof(TargetText), nameof(CodeText),
                     nameof(UsesCode), nameof(UsesTarget), nameof(IsDefaultProfile) })
            OnPropertyChanged(n);
    }

    public LaunchProfile ToProfile()
    {
        var p = new LaunchProfile
        {
            Name = string.IsNullOrWhiteSpace(_name) ? L.T("Profile") : _name.Trim(),
            IsDefault = _isDefault,
            RunAsAdmin = _runAsAdmin,
            RunAsOtherUser = _runAsOther,
            Engine = _engine,
            NoExit = _noExit,
            NoProfile = _noProfile,
            Target = _target,
            Machines = MachineList.ParseNames(_machines).ToList(),
            Dialog = _dialog,
            Arguments = _arguments.Trim(),
            ConfirmBeforeRun = _confirm,
            Options = Options is { HasOptions: true } o ? EntryOptions.From(o.Current) : null
        };
        foreach (var param in Parameters.Where(x => !x.IsSecure))
            p.Parameters[param.Name] = param.ToSetting();
        foreach (var v in Variables.Where(v => v.IsDefined)) p.Variables[v.Name] = v.ToVariable();
        if (!_isDefault)
        {
            // Nur eigene Werte zählen; ein geerbtes Ziel kommt vom Eintrag selbst
            if (Owns(ProfileInheritance.Target)) { p.TargetText = _targetText; p.Code = _code; }
            p.Own = _own.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            p.OwnBase = _own.Where(_ownBase.ContainsKey).ToDictionary(k => k, k => _ownBase[k], StringComparer.OrdinalIgnoreCase);
        }
        return p;
    }

    // ---- Vererbung vom Standardprofil -------------------------------------------------------

    public InheritToggle TargetInherit { get; }
    public InheritToggle RunAsInherit { get; }
    public InheritToggle WindowInherit { get; }
    public InheritToggle ArgumentsInherit { get; }

    /// <summary>Legt dieses Profil den Wert selbst fest? Das Standardprofil legt alles fest.</summary>
    internal bool Owns(string key) => _isDefault || _own.Contains(key);

    /// <summary>Wert eigen machen (✎) bzw. wieder vom Standard übernehmen (↺).</summary>
    internal void SetOwn(string key, bool own)
    {
        if (_isDefault) return;
        var standard = _owner.DefaultProfile;
        if (own)
        {
            _own.Add(key);
            if (standard != null) _ownBase[key] = ProfileInheritance.Snapshot(standard.ToProfile(), key);
            // Ziel bzw. Code beginnen mit dem des Eintrags
            if (key == ProfileInheritance.Target && _owner.Entry != null)
            {
                _targetText ??= _owner.Entry.Current.Target;
                _code ??= _owner.Entry.Current.Code;
            }
        }
        else
        {
            _own.Remove(key);
            _ownBase.Remove(key);
            if (key == ProfileInheritance.Target) { _targetText = null; _code = null; }
            if (standard != null) SyncFrom(standard);
        }
        RefreshInheritance();
        OnChanged();
    }

    /// <summary>Anzahl eigener Werte (Profilliste).</summary>
    public int OwnCount => _isDefault ? 0 : _own.Count;
    public bool HasOwnValues => OwnCount > 0;
    public string InheritText => _isDefault
        ? L.T("Basis for all profiles")
        : _own.Count == 0 ? L.T("takes everything from ★ Standard") : _own.Count == 1 ? L.T("1 own value") : L.F("{0} own values", _own.Count);
    public string HeaderHint => _isDefault
        ? L.T("Changes here also apply to all other profiles, unless they set the value themselves.")
        : L.T("inherits from ★ Standard · grey = taken over, ✎ = own");

    /// <summary>Alle geerbten Werte vom Standard übernehmen (ohne Änderungsmeldung an den Standard).</summary>
    internal void SyncFrom(ProfileEditViewModel standard)
    {
        if (_isDefault || ReferenceEquals(standard, this)) return;
        var d = standard.ToProfile();
        if (!Owns(ProfileInheritance.RunAs))
        {
            _runAsAdmin = d.RunAsAdmin;
            _runAsOther = d.RunAsOtherUser && !d.RunAsAdmin;
            _target = d.Target;
            _machines = string.Join(", ", d.Machines);
            _engine = d.Engine;
        }
        if (!Owns(ProfileInheritance.Window))
        {
            _dialog = d.Dialog;
            _noExit = d.NoExit;
            _noProfile = d.NoProfile;
            _confirm = d.ConfirmBeforeRun;
            if (d.Options != null && Options != null) Options.ApplyOptions(d.Options);
        }
        if (!Owns(ProfileInheritance.Arguments)) _arguments = d.Arguments;
        if (!Owns(ProfileInheritance.Target)) { _targetText = null; _code = null; }
        foreach (var row in Parameters.Where(r => !r.IsSecure && !Owns(r.Inherit.Key)))
            row.CopyFrom(d.Parameters.TryGetValue(row.Name, out var st) ? st : ProfileResolver.SettingFor(new LaunchProfile(), row.Parameter));
        foreach (var row in Variables.Where(r => !Owns(r.Inherit.Key)))
            row.CopyFrom(d.Variables.TryGetValue(row.Name, out var v) ? v : null);
        foreach (var n in new[] { nameof(RunAsAdmin), nameof(RunMode), nameof(Target), nameof(IsFixedTarget), nameof(Machines), nameof(Engine), nameof(Dialog),
                     nameof(NoExit), nameof(NoProfile), nameof(ConfirmBeforeRun), nameof(Arguments), nameof(TargetText), nameof(CodeText) })
            OnPropertyChanged(n);
        RaiseDerived();
    }

    /// <summary>Standard ist ein anderes Profil geworden: eigene Werte = Unterschiede zum neuen Standard.</summary>
    internal void RebaseOn(ProfileEditViewModel standard)
    {
        _own.Clear();
        _ownBase.Clear();
        if (_isDefault) return;
        var d = standard.ToProfile();
        var mine = ToProfile();
        mine.TargetText = _targetText;
        mine.Code = _code;
        foreach (var k in ProfileInheritance.Differences(d, mine))
        {
            _own.Add(k);
            _ownBase[k] = ProfileInheritance.Snapshot(d, k);
        }
    }

    internal void RefreshInheritance()
    {
        foreach (var t in new[] { TargetInherit, RunAsInherit, WindowInherit, ArgumentsInherit }) t.Refresh();
        foreach (var r in Parameters) r.Inherit.Refresh();
        foreach (var r in Variables) r.Refresh();
        RaiseDerived();
    }

    // ---- Hinweis: Standard geändert, nachdem ein Wert hier angepasst wurde ---------------------

    private IReadOnlyList<string> ChangedInStandard => _isDefault || _owner.DefaultProfile is not { } std
        ? Array.Empty<string>()
        : ProfileInheritance.ChangedInStandard(std.ToProfile(), ToProfile());

    public bool HasStandardChanged => ChangedInStandard.Count > 0;

    public string StandardChangedText
    {
        get
        {
            var keys = ChangedInStandard;
            if (keys.Count == 0) return string.Empty;
            return L.F("★ Standard was changed after this profile set its own value: {0}", string.Join(", ", keys.Select(KeyLabel)));
        }
    }

    private string KeyLabel(string key) =>
        key.StartsWith(ProfileInheritance.ParamPrefix, StringComparison.OrdinalIgnoreCase) ? "-" + key[ProfileInheritance.ParamPrefix.Length..]
        : key.StartsWith(ProfileInheritance.VarPrefix, StringComparison.OrdinalIgnoreCase) ? "{{" + key[ProfileInheritance.VarPrefix.Length..] + "}}"
        : key switch
        {
            ProfileInheritance.Target => L.T("Target / code"),
            ProfileInheritance.RunAs => L.T("Run as / where"),
            ProfileInheritance.Window => L.T("Window and behavior"),
            _ => L.T("Arguments")
        };

    public ICommand AdoptStandardCommand { get; }
    public ICommand DismissStandardChangeCommand { get; }

    /// <summary>Die betroffenen Werte wieder vom Standard übernehmen.</summary>
    private void AdoptStandard()
    {
        foreach (var k in ChangedInStandard.ToList()) { _own.Remove(k); _ownBase.Remove(k); if (k == ProfileInheritance.Target) { _targetText = null; _code = null; } }
        if (_owner.DefaultProfile is { } std) SyncFrom(std);
        RefreshInheritance();
        OnChanged();
    }

    /// <summary>Eigene Werte behalten, Hinweis ausblenden (neuer Stand des Standards gilt als gesehen).</summary>
    private void DismissStandardChange()
    {
        if (_owner.DefaultProfile is not { } std) return;
        var d = std.ToProfile();
        foreach (var k in ChangedInStandard.ToList()) _ownBase[k] = ProfileInheritance.Snapshot(d, k);
        RaiseDerived();
    }

    // ---- Ziel bzw. Code eines eigenen Eintrags ----------------------------------------------------

    /// <summary>Ziel dieses Profils (geerbt: das des Eintrags).</summary>
    public string TargetText
    {
        get => _targetText ?? _owner.Entry?.Current.Target ?? string.Empty;
        set { if (Owns(ProfileInheritance.Target) && !_isDefault && _targetText != value) { _targetText = value ?? string.Empty; OnPropertyChanged(); OnChanged(); } }
    }

    /// <summary>Code dieses Profils (Snippets; geerbt: der des Eintrags).</summary>
    public string CodeText
    {
        get => _code ?? _owner.Entry?.Current.Code ?? string.Empty;
        set { if (Owns(ProfileInheritance.Target) && !_isDefault && _code != value) { _code = value ?? string.Empty; OnPropertyChanged(); OnChanged(); } }
    }

    /// <summary>Anderes Profil als der Standard: Ziel bzw. Code hier überschreiben (statt das Formular des Eintrags zu zeigen).</summary>
    public bool ShowTargetOverride => !_isDefault && _owner.Entry != null;
    /// <summary>Snippets: der Code wird überschrieben; sonst das Ziel (Adresse, Pfad, Befehl).</summary>
    public bool UsesCode => _owner.Entry?.Current.Kind is CustomEntryKind.Snippet;
    public bool UsesTarget => !UsesCode;
    public bool IsDefaultProfile => _isDefault;
    public bool ShowEntryForm => _isDefault && _owner.Entry != null;

    // ---- Variablen ---------------------------------------------------------------------------------

    public ObservableCollection<ProfileVariableRowViewModel> Variables { get; } = new();
    public bool HasVariables => Variables.Count > 0;
    public ICommand AddVariableCommand { get; }

    /// <summary>
    /// Zeilen aus dem Text des Eintrags (Abfragen und {{$Variablen}}), den Variablen des Standards und den eigenen dieses
    /// Profils. Werte bereits angezeigter Zeilen bleiben erhalten.
    /// </summary>
    internal void BuildVariables()
    {
        var keep = Variables.ToDictionary(v => v.Name, v => v.ToVariable(), StringComparer.OrdinalIgnoreCase);
        foreach (var r in Variables.Where(r => r.IsDefined)) _variables[r.Name] = r.ToVariable();
        var defined = keep.Count > 0 ? Variables.Where(r => r.IsDefined).ToDictionary(r => r.Name, r => r.ToVariable(), StringComparer.OrdinalIgnoreCase) : _variables;
        var fromText = _owner.VariableNamesInText(this);
        var standard = _owner.DefaultProfile is { } std && !ReferenceEquals(std, this) ? std.ToProfile().Variables : new Dictionary<string, ProfileVariable>();
        var names = fromText.Concat(defined.Keys).Concat(standard.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Variables.Clear();
        foreach (var name in names)
        {
            var inText = fromText.Contains(name, StringComparer.OrdinalIgnoreCase);
            var profileOnly = name.StartsWith('$') && !_owner.IsGlobalVariable(name) && !Core.Config.Variables.IsBuiltin(name);
            defined.TryGetValue(name, out var def);
            Variables.Add(new ProfileVariableRowViewModel(this, name, def, inText, profileOnly));
        }
        OnPropertyChanged(nameof(HasVariables));
    }

    private void AddVariable()
    {
        var name = _owner.App.Dialogs.Ask(L.T("New variable"), L.T("Name of the variable for this entry (used as {{$Name}}):"), string.Empty, L.T("Add"),
            n => Core.Config.Variables.Normalize(n) is { Length: > 0 } x && System.Text.RegularExpressions.Regex.IsMatch(x, @"^[\p{L}_][\p{L}\p{N}_]*$")
                ? Variables.Any(v => v.Name.Equals("$" + x, StringComparison.OrdinalIgnoreCase)) ? L.T("This variable already exists here.") : null
                : L.T("Only letters, digits and _ – no spaces, not starting with a digit."));
        if (name == null) return;
        var key = "$" + Core.Config.Variables.Normalize(name);
        Variables.Add(new ProfileVariableRowViewModel(this, key, new ProfileVariable(), false, !_owner.IsGlobalVariable(key)));
        if (!_isDefault) SetOwn(ProfileInheritance.VarPrefix + key, true);
        OnPropertyChanged(nameof(HasVariables));
        OnChanged();
    }

    internal void RemoveVariable(ProfileVariableRowViewModel row)
    {
        Variables.Remove(row);
        _variables.Remove(row.Name);
        OnPropertyChanged(nameof(HasVariables));
        OnChanged();
    }

    /// <summary>Globaler Wert einer {{$Variable}} (Anzeige; geheime verdeckt).</summary>
    internal string GlobalValue(string name) => _owner.App.VariablePreview(name);

    // ---- Vorschau: Aufruf und Code ----------------------------------------------------------------

    public ICommand TestCommand { get; }

    private IReadOnlyList<Core.Metadata.CodeSegment> _codeSegments = Array.Empty<Core.Metadata.CodeSegment>();
    /// <summary>Script bzw. Code mit Syntaxfarben (schreibgeschützt).</summary>
    public IReadOnlyList<Core.Metadata.CodeSegment> CodeSegments { get => _codeSegments; private set => Set(ref _codeSegments, value); }
    public bool HasCode => _codeSegments.Count > 0;
    private string _codeStatus = string.Empty;
    public string CodeStatus { get => _codeStatus; private set => Set(ref _codeStatus, value); }

    /// <summary>Datei-Scripte lassen sich in der Vorschau bearbeiten (gespeichert wird in die Datei, mit Sicherung).</summary>
    public bool CanEditFile => _owner.IsScannedFile && _owner.Item.Type is ItemType.PowerShell or ItemType.Batch && File.Exists(_owner.Item.FullPath);
    public ICommand EditCodeCommand { get; }
    public ICommand SaveCodeCommand { get; }
    public ICommand CancelEditCodeCommand { get; }

    private bool _isEditingCode;
    public bool IsEditingCode
    {
        get => _isEditingCode;
        private set { if (Set(ref _isEditingCode, value)) { OnPropertyChanged(nameof(IsReadingCode)); CommandRequery.Invalidate(); } }
    }
    public bool IsReadingCode => !_isEditingCode;

    private string _editableCode = string.Empty;
    public string EditableCode
    {
        get => _editableCode;
        set { if (Set(ref _editableCode, value ?? string.Empty)) CodeStatus = L.T("● changed, not saved"); }
    }

    public string EditWarning => L.F("Changes are saved to {0} – they apply to all profiles and to everyone who uses the file. A backup is created first.", _owner.Item.FullPath);

    /// <summary>Code für die Vorschau laden: Datei-Script bzw. Code des Snippets (eigener oder der des Eintrags).</summary>
    internal void LoadCode()
    {
        string? text = null;
        bool ps = _owner.Item.Type == ItemType.PowerShell;
        bool batch = _owner.Item.Type == ItemType.Batch;
        if (_owner.Entry is { } e)
        {
            if (e.Current.Kind is CustomEntryKind.Snippet)
            {
                text = CodeText;
                ps = e.Current.Language == SnippetLanguage.PowerShell;
                batch = false;
            }
        }
        else if ((ps || batch) && Core.Metadata.CodePreview.ReadHead(_owner.Item.FullPath, 2000) is { } head)
            text = head.Text;
        CodeSegments = string.IsNullOrEmpty(text) ? Array.Empty<Core.Metadata.CodeSegment>()
            : ps ? Core.Metadata.CodePreview.TokenizePowerShell(text)
            : batch ? Core.Metadata.CodePreview.TokenizeBatch(text)
            : new[] { new Core.Metadata.CodeSegment(text, Core.Metadata.CodeTokenKind.Text) };
        var lines = string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length;
        CodeStatus = lines == 0 ? string.Empty : L.F("read-only · {0} lines", lines);
        OnPropertyChanged(nameof(HasCode));
        OnPropertyChanged(nameof(CanEditFile));
    }

    private void StartEditCode()
    {
        try { EditableCode = File.ReadAllText(_owner.Item.FullPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CodeStatus = L.F("The file couldn't be read: {0}", ex.Message); return; }
        CodeStatus = L.T("editing");
        IsEditingCode = true;
    }

    private void SaveCode()
    {
        try
        {
            var backup = Core.Config.ScriptFiles.SaveWithBackup(_owner.Item.FullPath, _editableCode);
            IsEditingCode = false;
            LoadCode();
            _owner.OnFileEdited(_editableCode);
            CodeStatus = L.F("saved · backup: {0}", Path.GetFileName(backup));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CodeStatus = L.F("The file couldn't be written: {0}", ex.Message);
        }
    }
}

/// <summary>Bearbeitet Anzeigename, Beschreibung und Startprofile eines Eintrags.</summary>
public sealed class ProfilesViewModel : ObservableObject
{
    private readonly IProfilesHost _app;
    private readonly AppSettings _target;
    private readonly bool _saveImmediately;
    private ProfileEditViewModel? _selected;
    private string _displayName;
    private string _description;
    private string _category;

    /// <param name="customEntry">Eigener Eintrag, der hier mitbearbeitet wird (null = aus <paramref name="target"/> ermitteln).</param>
    /// <param name="isNew">Neuer eigener Eintrag: wird beim Speichern hinzugefügt.</param>
    public ProfilesViewModel(IProfilesHost app, LaunchItem item, AppSettings target, bool saveImmediately,
        CustomEntry? customEntry = null, bool isNew = false, string? initialGroup = null, IEnumerable<string>? initialTags = null,
        bool pickKind = false)
    {
        _app = app;
        _item = item;
        _target = target;
        _saveImmediately = saveImmediately;
        IsNew = isNew;
        customEntry ??= item.Source != null ? target.CustomEntries.FirstOrDefault(c => c.Id == item.Source.Id) ?? item.Source : null;
        if (customEntry != null)
        {
            _originalKey = isNew ? null : customEntry.Key;
            Entry = new CustomEntryViewModel(customEntry, app);
            Entry.Changed += OnEntryChanged;
            Entry.TestHandler = e => _app.TestRun(e, (DefaultProfile ?? Profiles[0]).ToProfile());
            Entry.SmartGroupRequested += path => { SmartGroupPath = path; CloseRequested?.Invoke(); };
            _showKindPicker = pickKind;
            _lastParams = ParamSignature(item);
        }
        var entry = target.FindEntry(item.FullPath);
        _displayName = entry?.DisplayName ?? string.Empty;
        _description = entry?.Description ?? string.Empty;
        _category = entry?.Category ?? initialGroup ?? string.Empty;
        _notes = entry?.Notes ?? string.Empty;
        _singleClick = entry?.SingleClick ?? false;
        _askProfile = entry?.AskProfile switch { true => "yes", false => "no", _ => "default" };
        _tags = Core.Config.Tags.Format(entry?.Tags ?? initialTags?.ToList() ?? new List<string>());
        Categories = GroupManager.All(target);
        if (Entry != null)
        {
            // Früher getrennt gespeicherter Anzeigename/Beschreibung wandert in den Eintrag
            if (_displayName.Length > 0 && string.IsNullOrWhiteSpace(Entry.Name)) Entry.Name = _displayName;
            if (_description.Length > 0 && string.IsNullOrWhiteSpace(Entry.Description)) Entry.Description = _description;
        }

        var profiles = entry != null && entry.Profiles.Count > 0
            ? entry.Profiles.Select(p => p.Clone()).ToList()
            : new List<LaunchProfile> { ProfileResolver.CreateImplicit(target, item) };
        if (!profiles.Any(p => p.IsDefault)) profiles[0].IsDefault = true;
        // Umzug ins Vererbungsmodell: Werte, die dem Standard gleichen, gelten als geerbt, abweichende als eigen
        var standardProfile = profiles.First(p => p.IsDefault);
        foreach (var p in profiles.Where(p => !p.IsDefault && p.Own == null))
        {
            p.Own = ProfileInheritance.Differences(standardProfile, p);
            p.OwnBase = p.Own.ToDictionary(k => k, k => ProfileInheritance.Snapshot(standardProfile, k), StringComparer.OrdinalIgnoreCase);
        }
        foreach (var p in profiles) Profiles.Add(new ProfileEditViewModel(this, p));
        _selected = Profiles.FirstOrDefault(p => p.IsDefault) ?? Profiles[0];
        _selected.IsSelected = true;
        foreach (var p in Profiles) { p.BuildVariables(); p.LoadCode(); p.RefreshInheritance(); }

        AddCommand = new RelayCommand(Add);
        DuplicateCommand = new RelayCommand(Duplicate, () => Selected != null);
        DeleteCommand = new RelayCommand(Delete, () => Profiles.Count > 1);
        SaveCommand = new RelayCommand(Save, () => !ShowKindPicker && (Entry == null || Entry.IsValid) && Profiles.All(p => p.Options?.OptionsValid != false));
        PickKindCommand = new RelayCommand(p => PickKind(p?.ToString()));
        // Die Art lässt sich nur beim Anlegen wählen bzw. korrigieren – bestehende Einträge behalten ihre Art
        ChangeKindCommand = new RelayCommand(() => ShowKindPicker = true, () => Entry != null && IsNew);
        BackToFormCommand = new RelayCommand(() => ShowKindPicker = false, () => !IsNew || _kindChosen);
        UseClipboardCommand = new RelayCommand(UseClipboard, () => ClipboardSuggestion != null);
        CatalogCommand = new RelayCommand(() => { AlternativeRequested = "catalog"; CloseRequested?.Invoke(); });
        FilesCommand = new RelayCommand(() => { AlternativeRequested = "files"; CloseRequested?.Invoke(); });
        if (_showKindPicker) LoadClipboardSuggestion();
        _kindChosen = !pickKind;
        if (Entry != null)
        {
            _lastParams = string.Empty;
            OnEntryChanged(); // Parameter eines PowerShell-Snippets, Vorschau
        }
    }

    private LaunchItem _item;
    private bool _kindChosen;

    internal IProfilesHost App => _app;

    /// <summary>Element mit den Startoptionen eines Profils (für die Befehlsvorschau).</summary>
    internal LaunchItem ItemFor(LaunchProfile p)
    {
        if (Entry == null || (p.Options == null && p.TargetText == null && p.Code == null && p.Variables.Count == 0)) return Item;
        var item = Entry.Current.WithProfile(p).ToLaunchItem();
        if (Entry.IsPsSnippet) { item.LaunchTarget = Entry.Current.SnippetPath; try { Core.Metadata.ScriptInfoReader.FillFromText(item, Entry.Current.Code); } catch { /* unvollständiger Code beim Tippen: Parameter bleiben leer */ } }
        return item;
    }
    private string _lastParams = string.Empty;

    /// <summary>Der Eintrag als startbares Element (für Vorschau und Parameter); folgt den Eingaben.</summary>
    public LaunchItem Item { get => _item; private set => Set(ref _item, value); }
    public bool IsNew { get; }

    // ---- Schritt 1: Art wählen ------------------------------------------------------

    private bool _showKindPicker;
    public bool ShowKindPicker
    {
        get => _showKindPicker;
        set
        {
            if (!Set(ref _showKindPicker, value)) return;
            OnPropertyChanged(nameof(ShowForm));
            OnPropertyChanged(nameof(FirstError));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(WindowTitle));
            if (value) LoadClipboardSuggestion();
            CommandRequery.Invalidate();
        }
    }
    public bool ShowForm => !_showKindPicker;

    private string _page = "profiles";
    /// <summary>Reiter des Formulars: „general“ (Schlagworte, Notiz, Verhalten) oder „profiles“ (Ziel, Variablen, Start).</summary>
    public string Page
    {
        get => _page;
        set { if (Set(ref _page, value == "general" ? "general" : "profiles")) { OnPropertyChanged(nameof(IsGeneralPage)); OnPropertyChanged(nameof(IsProfilesPage)); } }
    }
    public bool IsGeneralPage { get => _page == "general"; set { if (value) Page = "general"; } }
    public bool IsProfilesPage { get => _page == "profiles"; set { if (value) Page = "profiles"; } }
    public string ProfilesTabText => L.F("Profiles · {0}", Profiles.Count);
    public ICommand ShowProfilesPageCommand => _showProfilesPage ??= new RelayCommand(() => Page = "profiles");
    private ICommand? _showProfilesPage;
    public IReadOnlyList<KindTile> Tiles => CustomEntryViewModel.Tiles;
    public ICommand PickKindCommand { get; }
    public ICommand ChangeKindCommand { get; }
    /// <summary>„Art ändern …“ nur bei neuen Einträgen.</summary>
    public bool CanChangeKind => Entry != null && IsNew;
    public ICommand BackToFormCommand { get; }
    public ICommand UseClipboardCommand { get; }
    public ICommand CatalogCommand { get; }
    public ICommand FilesCommand { get; }

    /// <summary>„catalog“ oder „files“: statt eines Eintrags wurde Vorlagen bzw. Dateien gewählt.</summary>
    public string? AlternativeRequested { get; private set; }
    /// <summary>„Stattdessen als smarte Gruppe anlegen“: Ordnerpfad.</summary>
    public string? SmartGroupPath { get; private set; }
    /// <summary>Zuletzt gewählte Gruppe (für Alternativen aus Schritt 1).</summary>
    public string InitialGroup => _category;

    private CustomEntry? _clipboardSuggestion;
    public CustomEntry? ClipboardSuggestion { get => _clipboardSuggestion; private set { if (Set(ref _clipboardSuggestion, value)) { OnPropertyChanged(nameof(HasClipboardSuggestion)); OnPropertyChanged(nameof(ClipboardText)); } } }
    public bool HasClipboardSuggestion => _clipboardSuggestion != null;
    public string ClipboardText => _clipboardSuggestion == null ? string.Empty
        : $"{CustomEntryViewModel.Tiles.FirstOrDefault(t => t.Key == _clipboardSuggestion.KindKey)?.Label ?? CustomEntry.KindLabel(_clipboardSuggestion.Kind)}: " +
          (_clipboardSuggestion.Kind == CustomEntryKind.Snippet ? FirstLine(_clipboardSuggestion.Code) : _clipboardSuggestion.SummaryText());

    private static string FirstLine(string t) { var l = t.Replace("\r", "").Split('\n')[0].Trim(); return l.Length > 70 ? l[..70] + " …" : l; }

    private void LoadClipboardSuggestion()
    {
        try
        {
            var text = _app.Shell.GetClipboardText();
            ClipboardSuggestion = text.Trim().Length is > 0 and < 20000
                ? ClipboardClassifier.Suggest(text, System.IO.Directory.Exists, System.IO.File.Exists)
                : null;
        }
        catch { ClipboardSuggestion = null; }
    }

    private void UseClipboard()
    {
        if (_clipboardSuggestion is not { } c || Entry == null) return;
        PickKind(c.KindKey);
        if (c.Target.Length > 0) Entry.Target = c.Target;
        if (c.Code.Length > 0) Entry.Code = c.Code;
        if (c.User.Length > 0) Entry.User = c.User;
    }

    private void PickKind(string? key)
    {
        if (Entry == null || key == null) return;
        Entry.SetKind(key);
        _kindChosen = true;
        ShowKindPicker = false;
        OnEntryChanged();
        RaiseKindDependent();
        KindChanged?.Invoke(Entry.KindKey);
    }

    /// <summary>Art gewählt oder geändert (Fenstergröße je Art).</summary>
    public event Action<string>? KindChanged;

    // ---- Kopf: gemeinsame Angaben ----------------------------------------------------------

    /// <summary>Anzeigename: bei eigenen Einträgen deren Name, bei Dateien der Anzeigename.</summary>
    public string HeaderName
    {
        get => Entry != null ? Entry.Name : _displayName;
        set { if (Entry != null) Entry.Name = value; else DisplayName = value; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); }
    }
    public string HeaderDescription
    {
        get => Entry != null ? Entry.Description : _description;
        set { if (Entry != null) Entry.Description = value; else Description = value; OnPropertyChanged(); }
    }
    public string HeaderNameHint => Entry != null ? Entry.NameSuggestion : Item.DisplayName;
    public string HeaderDescriptionHint => Entry != null ? L.T("Description (appears in the panel)") : DescriptionPlaceholder;

    // ---- Abschnitte je Art -----------------------------------------------------------------

    private CustomEntryKind? EntryKind => Entry?.Kind;
    /// <summary>Datei aus einer smarten Gruppe (oder eigener Datei-Eintrag): Script/Programm-Infos.</summary>
    public bool IsScannedFile => Entry == null;
    public string FileKindText => Item.IsPowerShell ? L.T("PowerShell script") : Item.Type switch
    {
        ItemType.Batch => L.T("Batch script"),
        ItemType.Shortcut => L.T("Shortcut"),
        ItemType.ConsoleSnapIn => "Snap-in",
        ItemType.Executable => L.T("Program"),
        _ => L.T("File")
    };
    public string FileInfo => Item.ParseError ?? (Item.Synopsis.Length > 0 ? Item.Synopsis : L.T("No description in the script (comment-based help .SYNOPSIS)."));
    public string FileParametersText => Item.Parameters.Count == 0 ? L.T("No parameters") : L.T("Parameters: ") + string.Join(", ", Item.Parameters.Select(p => p.Name + (p.IsMandatory ? L.T(" (required)") : string.Empty)));
    public ICommand OpenFileLocationCommand => new RelayCommand(() => _app.Shell.OpenLocation(Item.FullPath));

    /// <summary>Startprofile gibt es für alle Arten.</summary>
    public bool ShowStartSection => true;
    /// <summary>„Ausführen als“ nur, wo es Sinn ergibt (Programme, Scripte, Ordner).</summary>
    public bool ShowRunAs => EntryKind is null or CustomEntryKind.Command or CustomEntryKind.Tool or CustomEntryKind.File or CustomEntryKind.Folder
                             || (EntryKind == CustomEntryKind.Snippet && Entry!.IsPsSnippet && !Entry.CopyInsteadOfRun);
    /// <summary>PowerShell: Ziel (lokal/remote), Engine, Konsole, Dialog, Parameter.</summary>
    public bool ShowPsOptions => ShowStartSection && Item.IsPowerShell && !(Entry?.CopyInsteadOfRun ?? false);
    /// <summary>Programme und Batch: Argumente.</summary>
    public bool ShowProgramArgs => !Item.IsPowerShell && EntryKind is null or CustomEntryKind.Command or CustomEntryKind.Tool or CustomEntryKind.File;
    /// <summary>Ordner kennen nur „normal“ und „anderer Benutzer“.</summary>
    public bool ShowAdminMode => EntryKind != CustomEntryKind.Folder;

    /// <summary>Abschnitt „Ausführen als / wo“ im Profil.</summary>
    public bool ShowRunAsSection => ShowRunAs || ShowPsOptions;
    /// <summary>Abschnitt „Parameter“ im Profil (PowerShell mit Parametern).</summary>
    public bool ShowParametersSection => ShowPsOptions && HasParameters;
    /// <summary>„Konsole offen lassen“ bei Programmen, die eine Konsole haben.</summary>
    public bool ShowKeepOpen => ShowProgramArgs && SupportsNoExit;
    /// <summary>„Weitere Startprofile“ – nur wo Varianten sinnvoll sind.</summary>
    public bool ShowMoreProfiles => true;

    public ProfileEditViewModel? DefaultProfile => Profiles.FirstOrDefault(p => p.IsDefault);

    private bool _moreProfilesExpanded;
    public bool MoreProfilesExpanded { get => _moreProfilesExpanded; set { if (Set(ref _moreProfilesExpanded, value)) OnPropertyChanged(nameof(PreviewText)); } }

    /// <summary>Befehlsvorschau: Standardprofil bzw. das gerade bearbeitete weitere Profil.</summary>
    public string PreviewText => ((_moreProfilesExpanded ? Selected : DefaultProfile) ?? Selected)?.Preview ?? string.Empty;
    public string MoreProfilesText => Profiles.Count <= 1 ? L.T("More launch profiles") : L.F("More launch profiles ({0})", Profiles.Count - 1);

    private void RaiseKindDependent()
    {
        foreach (var p in new[] { nameof(ShowStartSection), nameof(ShowPsOptions), nameof(ShowProgramArgs), nameof(ShowAdminMode), nameof(ShowMoreProfiles), nameof(ShowRunAs),
                     nameof(PathText), nameof(Title), nameof(WindowTitle), nameof(HeaderNameHint), nameof(SupportsNoExit), nameof(IsPowerShell), nameof(IsProgram), nameof(HasParameters) })
            OnPropertyChanged(p);
    }

    private static string ParamSignature(LaunchItem i) => string.Join("|", i.Parameters.Select(p => p.Name + ":" + p.TypeName + ":" + p.IsMandatory));

    /// <summary>Eingaben im Eintrag: Element für Vorschau/Parameter neu bilden.</summary>
    private void OnEntryChanged()
    {
        if (Entry == null) return;
        var item = Entry.Current.ToLaunchItem();
        if (Entry.IsPsSnippet)
        {
            item.LaunchTarget = Entry.Current.SnippetPath;
            try { Core.Metadata.ScriptInfoReader.FillFromText(item, Entry.Current.Code); } catch { /* unvollständiger Code beim Tippen: Parameter bleiben leer */ }
        }
        Item = item;
        var sig = ParamSignature(item);
        if (sig != _lastParams)
        {
            _lastParams = sig;
            foreach (var p in Profiles) p.RefreshParameters();
            OnPropertyChanged(nameof(HasParameters));
            OnPropertyChanged(nameof(ShowParametersSection));
        }
        foreach (var p in Profiles) { p.Options?.SyncBaseFrom(Entry.Current); p.BuildVariables(); p.LoadCode(); p.OnChanged(); }
        OnPropertyChanged(nameof(HeaderNameHint));
        OnPropertyChanged(nameof(HeaderName));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(FirstError));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(ShowPsOptions));
        OnPropertyChanged(nameof(ShowProgramArgs));
        OnPropertyChanged(nameof(ShowRunAs));
        OnPropertyChanged(nameof(ShowRunAsSection));
        OnPropertyChanged(nameof(ShowParametersSection));
        OnPropertyChanged(nameof(ShowKeepOpen));
    }

    private bool _inProfileChange;
    internal void OnProfileChanged(ProfileEditViewModel p)
    {
        if (_inProfileChange) return;
        _inProfileChange = true;
        try
        {
            // Änderung am Standard: geerbte Werte der anderen Profile nachziehen
            if (p.IsDefault) foreach (var other in Profiles.Where(x => !ReferenceEquals(x, p))) other.SyncFrom(p);
            OnPropertyChanged(nameof(MoreProfilesText));
            OnPropertyChanged(nameof(ProfilesTabText));
            OnPropertyChanged(nameof(PreviewText));
        }
        finally { _inProfileChange = false; }
    }
    private readonly string? _originalKey;

    /// <summary>Felder des eigenen Eintrags (Art, Ziel, Code …); null bei Dateien aus Ordnern.</summary>
    public CustomEntryViewModel? Entry { get; }
    public bool HasEntry => Entry != null;
    public string Title => ShowKindPicker ? L.T("What would you like to create?") : Entry != null ? (IsNew && string.IsNullOrWhiteSpace(Entry.Name) ? L.T("New entry: ") + Entry.KindLabel : Entry.Title) : Item.FileName;
    public string PathText => Entry != null ? Entry.KindLabel : Item.FullPath;
    public string WindowTitle => ShowKindPicker ? L.T("New entry") : (IsNew ? L.T("New entry – ") : L.T("Entry – ")) + (Entry?.KindLabel ?? FileKindText);

    private bool _singleClick;
    /// <summary>Ein Klick im Panel startet sofort (statt aufzuklappen).</summary>
    public bool SingleClick { get => _singleClick; set => Set(ref _singleClick, value); }

    private string _askProfile = "default";
    /// <summary>Beim Start nach dem Profil fragen: „default“ (wie unter Ausführung), „yes“, „no“.</summary>
    public string AskProfile { get => _askProfile; set => Set(ref _askProfile, value ?? "default"); }
    public IReadOnlyList<KeyValuePair<string, string>> AskProfileChoices { get; } = new[]
    {
        new KeyValuePair<string, string>("default", L.T("As set under Execution")),
        new KeyValuePair<string, string>("yes", L.T("Always ask for the profile")),
        new KeyValuePair<string, string>("no", L.T("Never ask (default profile)"))
    };

    /// <summary>Vorhandene Schlagworte (global) für Vorschläge.</summary>
    public IReadOnlyList<string> AllTagsList => Core.Config.Tags.Suggested(_target);

    private string _error = string.Empty;
    public string Error { get => _error; private set => Set(ref _error, value); }
    public bool IsPowerShell => Item.IsPowerShell;
    public bool IsProgram => !Item.IsPowerShell;
    /// <summary>Erster Eingabefehler – nicht in Schritt 1 (dort ist noch nichts eingegeben).</summary>
    public string FirstError => ShowKindPicker ? string.Empty : Entry?.FirstError ?? string.Empty;
    public bool HasParameters => Item.Parameters.Count > 0;
    public bool SupportsNoExit => Item.Type is ItemType.PowerShell or ItemType.Batch;
    public string SaveLabel => _saveImmediately ? L.T("Save") : L.T("Apply");
    public string SaveHint => _saveImmediately
        ? L.T("Passwords and parameters of type PSCredential/SecureString are never saved.")
        : L.T("Applied immediately. Passwords are never saved.");
    public string DisplayNamePlaceholder => Item.DisplayName;
    public string DescriptionPlaceholder => string.IsNullOrWhiteSpace(Item.Synopsis) ? L.T("Description in the panel") : L.T("from the file: ") + Item.Synopsis;

    public string DisplayName { get => _displayName; set => Set(ref _displayName, value ?? string.Empty); }
    public string Description { get => _description; set => Set(ref _description, value ?? string.Empty); }

    /// <summary>Eigene Kategorie (Gruppierung „Nach Kategorie“).</summary>
    public string Category { get => _category; set => Set(ref _category, value ?? string.Empty); }

    private string _notes;
    /// <summary>Eigene Notiz (z. B. „nur nach Rücksprache mit dem DBA“); erscheint im Panel und im Ausführen-Dialog.</summary>
    public string Notes { get => _notes; set => Set(ref _notes, value ?? string.Empty); }

    private string _tags;
    /// <summary>Schlagworte, durch Komma getrennt (Suche im Panel: #schlagwort).</summary>
    public string TagsText { get => _tags; set => Set(ref _tags, value ?? string.Empty); }
    public IReadOnlyList<string> AllTags => Core.Config.Tags.All(_target);
    public IReadOnlyList<string> Categories { get; }

    public ObservableCollection<ProfileEditViewModel> Profiles { get; } = new();

    public ProfileEditViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            foreach (var p in Profiles) p.IsSelected = ReferenceEquals(p, value);
            OnPropertyChanged(nameof(PreviewText));
        }
    }

    public IReadOnlyList<KeyValuePair<PsEngine, string>> Engines { get; } = new[]
    {
        new KeyValuePair<PsEngine, string>(PsEngine.PowerShell7, "PowerShell 7"),
        new KeyValuePair<PsEngine, string>(PsEngine.WindowsPowerShell, "Windows PowerShell 5.1")
    };

    public ICommand AddCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveCommand { get; }

    /// <summary>Wird nach dem Übernehmen ausgelöst (Einstellungsfenster aktualisiert die Zeile).</summary>
    public event Action? Saved;
    public Action? CloseRequested { get; set; }

    internal void MakeDefault(ProfileEditViewModel p)
    {
        foreach (var x in Profiles) x.SetDefaultSilently(ReferenceEquals(x, p));
        // Neue Grundlage: eigene Werte der anderen sind jetzt ihre Unterschiede zum neuen Standard
        foreach (var x in Profiles) x.RebaseOn(p);
        foreach (var x in Profiles) { x.BuildVariables(); x.RefreshInheritance(); }
        OnPropertyChanged(nameof(DefaultProfile));
    }

    /// <summary>Profil zur Probe starten (ohne Verlauf).</summary>
    internal void Test(ProfileEditViewModel p)
    {
        var profile = p.ToProfile();
        if (DefaultProfile is { } std && !ReferenceEquals(std, p)) profile = ProfileInheritance.Effective(std.ToProfile(), profile);
        if (Entry != null) _app.TestRun(Entry.Current, profile);
        else _app.TestRunItem(Item, profile);
    }

    /// <summary>Namen der Abfragen und {{$Variablen}} im Ziel bzw. Code (eigene Werte des Profils eingeschlossen).</summary>
    internal IReadOnlyList<string> VariableNamesInText(ProfileEditViewModel p)
    {
        if (Entry == null) return Item.Parameters.Count == 0 ? Array.Empty<string>() : Placeholders.VariableNames(string.Join(" ", p.Parameters.Select(x => x.Value)), p.Arguments)
            .Where(n => !Core.Config.Variables.IsBuiltin(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var e = Entry.Current;
        var texts = new[] { p.TargetText, e.Arguments, p.CodeText, e.User, e.RemoteCommand, e.WorkingDirectory };
        return Placeholders.Find(texts).Select(x => x.Name)
            .Concat(Placeholders.VariableNames(texts).Where(n => !Core.Config.Variables.IsBuiltin(n)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Gibt es die {{$Variable}} global (unter Einstellungen › Variablen)?</summary>
    internal bool IsGlobalVariable(string name) => _app.UserVariables.Any(v => v.Name.Equals(Core.Config.Variables.Normalize(name), StringComparison.OrdinalIgnoreCase));

    /// <summary>Datei-Script in der Vorschau bearbeitet: Parameter neu lesen.</summary>
    internal void OnFileEdited(string text)
    {
        if (Entry != null || !Item.IsPowerShell) return;
        try { Core.Metadata.ScriptInfoReader.FillFromText(Item, text); } catch { /* unvollständiges Script: Parameter bleiben */ }
        foreach (var p in Profiles) { p.RefreshParameters(); p.LoadCode(); }
        OnPropertyChanged(nameof(HasParameters));
        OnPropertyChanged(nameof(FileParametersText));
    }

    private void Add()
    {
        // Neues Profil übernimmt alles vom Standard und legt zunächst nichts selbst fest
        var start = (DefaultProfile ?? Profiles[0]).ToProfile();
        start.Name = L.F("Profile {0}", Profiles.Count + 1);
        start.IsDefault = false;
        start.Own = new List<string>();
        start.OwnBase = new Dictionary<string, string>();
        var p = new ProfileEditViewModel(this, start);
        p.BuildVariables();
        p.LoadCode();
        p.RefreshInheritance();
        Profiles.Add(p);
        Selected = p;
        OnPropertyChanged(nameof(MoreProfilesText));
        OnPropertyChanged(nameof(ProfilesTabText));
    }

    private void Duplicate()
    {
        if (Selected == null) return;
        var copy = Selected.ToProfile();
        copy.Name += L.T(" (copy)");
        if (copy.IsDefault) { copy.Own = new List<string>(); copy.OwnBase = new Dictionary<string, string>(); } // Kopie des Standards erbt alles
        copy.IsDefault = false;
        var p = new ProfileEditViewModel(this, copy);
        p.BuildVariables();
        p.LoadCode();
        p.RefreshInheritance();
        Profiles.Add(p);
        Selected = p;
        OnPropertyChanged(nameof(MoreProfilesText));
        OnPropertyChanged(nameof(ProfilesTabText));
    }

    private void Delete()
    {
        if (Selected == null || Profiles.Count <= 1) return;
        var wasDefault = Selected.IsDefault;
        var idx = Profiles.IndexOf(Selected);
        Profiles.Remove(Selected);
        if (wasDefault) MakeDefault(Profiles[0]);
        Selected = Profiles[Math.Min(idx, Profiles.Count - 1)];
        OnPropertyChanged(nameof(MoreProfilesText));
        OnPropertyChanged(nameof(ProfilesTabText));
    }

    private void Save()
    {
        var key = Item.FullPath;
        if (Entry != null)
        {
            if (Entry.Validate() is { } problem)
            {
                Error = problem;
                return;
            }
            var edited = Entry.ToEntry();
            // Startoptionen des Standardprofils gelten auch für den Eintrag selbst (Symbol, Prüfung, Start ohne Profil)
            if ((DefaultProfile ?? Profiles[0]).Options is { HasOptions: true } def) EntryOptions.From(def.Current).ApplyTo(edited);
            // Name/Beschreibung stehen beim eigenen Eintrag selbst, nicht doppelt in der Eintragskonfiguration
            DisplayName = string.Empty;
            Description = string.Empty;
            var index = _target.CustomEntries.FindIndex(c => c.Id == edited.Id);
            if (index >= 0) _target.CustomEntries[index] = edited;
            else _target.CustomEntries.Add(edited);
            key = edited.Key;
            // Datei-Eintrag mit neuem Pfad: alte Einstellungen wandern mit
            if (_originalKey != null && !_originalKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                _target.Entries.RemoveAll(e => e.Path.Equals(_originalKey, StringComparison.OrdinalIgnoreCase));
        }
        var entry = new EntryConfig
        {
            Path = key,
            DisplayName = DisplayName.Trim(),
            Description = Description.Trim(),
            Category = GroupManager.Normalize(Category),
            Notes = Notes.Trim(),
            Tags = Core.Config.Tags.Parse(TagsText),
            SingleClick = SingleClick,
            AskProfile = AskProfile switch { "yes" => true, "no" => false, _ => null },
            Profiles = Profiles.Select(p => p.ToProfile()).ToList()
        };
        if (entry.Category.Length > 0 && !_target.CategoryOrder.Contains(entry.Category, StringComparer.CurrentCultureIgnoreCase))
            _target.CategoryOrder = GroupManager.All(_target).Append(entry.Category).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        _target.Entries.RemoveAll(e => string.Equals(e.Path, key, StringComparison.OrdinalIgnoreCase));
        _target.Entries.Add(entry);
        Core.Config.Tags.Remember(_target, entry.Tags);
        SavedKey = key;
        if (_saveImmediately) _app.OnProfilesSaved();
        Saved?.Invoke();
        CloseRequested?.Invoke();
    }

    /// <summary>Schlüssel des gespeicherten Eintrags (nach dem Speichern).</summary>
    public string? SavedKey { get; private set; }
}
