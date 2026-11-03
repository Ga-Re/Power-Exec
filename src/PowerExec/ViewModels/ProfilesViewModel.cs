using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

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
            if (IsSecure) return "Wird nie gespeichert – das Script fragt beim Start selbst.";
            var help = string.IsNullOrWhiteSpace(Parameter.HelpText) ? string.Empty : Parameter.HelpText;
            if (Parameter.IsMandatory && _mode == ParameterMode.Ask) return "Pflicht · wird beim Start abgefragt";
            return Parameter.IsMandatory ? "Pflicht" + (help.Length > 0 ? " · " + help : string.Empty) : help;
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
    private PsEngine _engine;
    private bool _noExit;
    private bool _noProfile;
    private ProfileTarget _target;
    private string _machines;
    private DialogMode _dialog;
    private string _arguments;
    private bool _confirm;
    private static int _counter;

    public ProfileEditViewModel(ProfilesViewModel owner, LaunchProfile p)
    {
        _owner = owner;
        _name = p.Name;
        _isDefault = p.IsDefault;
        _runAsAdmin = p.RunAsAdmin;
        _engine = p.Engine;
        _noExit = p.NoExit;
        _noProfile = p.NoProfile;
        _target = p.Target;
        _machines = string.Join(", ", p.Machines);
        _dialog = p.Dialog;
        _arguments = p.Arguments;
        _confirm = p.ConfirmBeforeRun;
        var key = "p" + Interlocked.Increment(ref _counter);
        foreach (var param in owner.Item.Parameters)
            Parameters.Add(new ProfileParamViewModel(this, param, ProfileResolver.SettingFor(p, param), key));
        SelectCommand = new RelayCommand(() => _owner.Selected = this);
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
    }

    public bool RunAsAdmin { get => _runAsAdmin; set { if (Set(ref _runAsAdmin, value)) OnChanged(); } }
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

    public string DialogHint
    {
        get
        {
            var p = ToProfile();
            bool needs = ProfileResolver.NeedsDialog(p, _owner.Item);
            return _dialog switch
            {
                DialogMode.Always => "Der Dialog zeigt die Werte des Profils; Änderungen gelten nur für diesen Start.",
                DialogMode.Never when needs => "Achtung: Ein Pflichtfeld wird abgefragt oder Rechner fehlen – der Dialog erscheint trotzdem.",
                DialogMode.Never => "Ein Klick im Panel startet sofort.",
                _ when needs => "Der Dialog erscheint, weil noch etwas abgefragt wird.",
                _ => "Alles festgelegt – der Dialog wird übersprungen."
            };
        }
    }

    public string Preview
    {
        get
        {
            var item = _owner.Item;
            var p = ToProfile();
            ParameterValueConverter.TryConvert(item.Parameters, ProfileResolver.InitialValues(p, item), out var values);
            var request = new ExecutionRequest
            {
                Item = item,
                Mode = p.RunAsAdmin ? ExecutionMode.Administrator : ExecutionMode.CurrentUser,
                Engine = p.Engine,
                NoExit = p.NoExit,
                NoProfile = p.NoProfile,
                Parameters = values,
                ExtraArguments = p.Arguments,
                Targets = item.SupportsRemote && p.Target == ProfileTarget.Fixed ? p.Machines : Array.Empty<string>()
            };
            if (item.SupportsRemote && p.Target != ProfileTarget.Local) return CommandBuilder.RemotePreview(request);
            var preview = CommandBuilder.BuildLocal(request).Preview;
            return p.RunAsAdmin ? preview + "   (als Administrator)" : preview;
        }
    }

    internal void OnChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(DialogHint));
        OnPropertyChanged(nameof(Preview));
    }

    public LaunchProfile ToProfile()
    {
        var p = new LaunchProfile
        {
            Name = string.IsNullOrWhiteSpace(_name) ? "Profil" : _name.Trim(),
            IsDefault = _isDefault,
            RunAsAdmin = _runAsAdmin,
            Engine = _engine,
            NoExit = _noExit,
            NoProfile = _noProfile,
            Target = _target,
            Machines = MachineList.ParseNames(_machines).ToList(),
            Dialog = _dialog,
            Arguments = _arguments.Trim(),
            ConfirmBeforeRun = _confirm
        };
        foreach (var param in Parameters.Where(x => !x.IsSecure))
            p.Parameters[param.Name] = param.ToSetting();
        return p;
    }
}

/// <summary>Bearbeitet Anzeigename, Beschreibung und Startprofile eines Eintrags.</summary>
public sealed class ProfilesViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly AppSettings _target;
    private readonly bool _saveImmediately;
    private ProfileEditViewModel? _selected;
    private string _displayName;
    private string _description;
    private string _category;

    public ProfilesViewModel(AppController app, LaunchItem item, AppSettings target, bool saveImmediately)
    {
        _app = app;
        Item = item;
        _target = target;
        _saveImmediately = saveImmediately;
        var entry = target.FindEntry(item.FullPath);
        _displayName = entry?.DisplayName ?? string.Empty;
        _description = entry?.Description ?? string.Empty;
        _category = entry?.Category ?? string.Empty;
        _notes = entry?.Notes ?? string.Empty;
        _tags = Core.Config.Tags.Format(entry?.Tags ?? new List<string>());
        Categories = target.CategoryOrder
            .Concat(target.Entries.Select(e => e.Category))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var profiles = entry != null && entry.Profiles.Count > 0
            ? entry.Profiles.Select(p => p.Clone()).ToList()
            : new List<LaunchProfile> { ProfileResolver.CreateImplicit(target, item) };
        foreach (var p in profiles) Profiles.Add(new ProfileEditViewModel(this, p));
        if (!Profiles.Any(p => p.IsDefault)) Profiles[0].SetDefaultSilently(true);
        _selected = Profiles.FirstOrDefault(p => p.IsDefault) ?? Profiles[0];
        _selected.IsSelected = true;

        AddCommand = new RelayCommand(Add);
        DuplicateCommand = new RelayCommand(Duplicate, () => Selected != null);
        DeleteCommand = new RelayCommand(Delete, () => Profiles.Count > 1);
        SaveCommand = new RelayCommand(Save);
    }

    public LaunchItem Item { get; }
    public string Title => Item.FileName;
    public string PathText => Item.FullPath;
    public bool IsPowerShell => Item.IsPowerShell;
    public bool IsProgram => !Item.IsPowerShell;
    public bool HasParameters => Item.Parameters.Count > 0;
    public bool SupportsNoExit => Item.Type is ItemType.PowerShell or ItemType.Batch;
    public string SaveLabel => _saveImmediately ? "Speichern" : "Übernehmen";
    public string SaveHint => _saveImmediately
        ? "Kennwörter und Parameter vom Typ PSCredential/SecureString werden nie gespeichert."
        : "Wird mit „Speichern“ in den Einstellungen übernommen. Kennwörter werden nie gespeichert.";
    public string DisplayNamePlaceholder => Item.DisplayName;
    public string DescriptionPlaceholder => string.IsNullOrWhiteSpace(Item.Synopsis) ? "Beschreibung im Panel" : "aus der Datei: " + Item.Synopsis;

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
    public IReadOnlyList<string> Categories { get; }

    public ObservableCollection<ProfileEditViewModel> Profiles { get; } = new();

    public ProfileEditViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            foreach (var p in Profiles) p.IsSelected = ReferenceEquals(p, value);
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
    }

    private void Add()
    {
        var p = new ProfileEditViewModel(this, ProfileResolver.CreateNew(_target, Item, $"Profil {Profiles.Count + 1}"));
        Profiles.Add(p);
        Selected = p;
    }

    private void Duplicate()
    {
        if (Selected == null) return;
        var copy = Selected.ToProfile();
        copy.Name += " (Kopie)";
        copy.IsDefault = false;
        var p = new ProfileEditViewModel(this, copy);
        Profiles.Add(p);
        Selected = p;
    }

    private void Delete()
    {
        if (Selected == null || Profiles.Count <= 1) return;
        var wasDefault = Selected.IsDefault;
        var idx = Profiles.IndexOf(Selected);
        Profiles.Remove(Selected);
        if (wasDefault) MakeDefault(Profiles[0]);
        Selected = Profiles[Math.Min(idx, Profiles.Count - 1)];
    }

    private void Save()
    {
        var entry = new EntryConfig
        {
            Path = Item.FullPath,
            DisplayName = DisplayName.Trim(),
            Description = Description.Trim(),
            Category = Category.Trim(),
            Notes = Notes.Trim(),
            Tags = Core.Config.Tags.Parse(TagsText),
            Profiles = Profiles.Select(p => p.ToProfile()).ToList()
        };
        if (entry.Category.Length > 0 && !_target.CategoryOrder.Contains(entry.Category, StringComparer.CurrentCultureIgnoreCase))
            _target.CategoryOrder.Add(entry.Category);
        _target.Entries.RemoveAll(e => string.Equals(e.Path, Item.FullPath, StringComparison.OrdinalIgnoreCase));
        _target.Entries.Add(entry);
        if (_saveImmediately) _app.OnProfilesSaved();
        Saved?.Invoke();
        CloseRequested?.Invoke();
    }
}
