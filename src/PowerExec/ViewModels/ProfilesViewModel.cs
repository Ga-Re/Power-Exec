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
    private bool _runAsOther;
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
        _runAsOther = p.RunAsOtherUser && !p.RunAsAdmin;
        _engine = p.Engine;
        _noExit = p.NoExit;
        _noProfile = p.NoProfile;
        _target = p.Target;
        _machines = string.Join(", ", p.Machines);
        _dialog = p.Dialog;
        _arguments = p.Arguments;
        _confirm = p.ConfirmBeforeRun;
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
        ? o.OptionsSummary + (_runAsAdmin ? " · Admin" : _runAsOther ? " · anderer Benutzer" : string.Empty)
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
            var p = ToProfile();
            var item = _owner.ItemFor(p);
            ParameterValueConverter.TryConvert(item.Parameters, ProfileResolver.InitialValues(p, item), out var values);
            var request = new ExecutionRequest
            {
                Item = item,
                Mode = AppController.ProfileMode(p),
                Engine = p.Engine,
                NoExit = p.NoExit,
                NoProfile = p.NoProfile,
                Parameters = values,
                ExtraArguments = p.Arguments,
                Targets = item.SupportsRemote && p.Target == ProfileTarget.Fixed ? p.Machines : Array.Empty<string>()
            };
            if (item.SupportsRemote && p.Target != ProfileTarget.Local) return CommandBuilder.RemotePreview(request);
            var preview = CommandBuilder.BuildLocal(request).Preview;
            return p.RunAsAdmin ? preview + "   (als Administrator)" : p.RunAsOtherUser ? preview + "   (als anderer Benutzer)" : preview;
        }
    }

    internal void OnChanged()
    {
        _owner.OnProfileChanged(this);
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Detail));
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

    /// <param name="customEntry">Eigener Eintrag, der hier mitbearbeitet wird (null = aus <paramref name="target"/> ermitteln).</param>
    /// <param name="isNew">Neuer eigener Eintrag: wird beim Speichern hinzugefügt.</param>
    public ProfilesViewModel(AppController app, LaunchItem item, AppSettings target, bool saveImmediately,
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
        foreach (var p in profiles) Profiles.Add(new ProfileEditViewModel(this, p));
        if (!Profiles.Any(p => p.IsDefault)) Profiles[0].SetDefaultSilently(true);
        _selected = Profiles.FirstOrDefault(p => p.IsDefault) ?? Profiles[0];
        _selected.IsSelected = true;

        AddCommand = new RelayCommand(Add);
        DuplicateCommand = new RelayCommand(Duplicate, () => Selected != null);
        DeleteCommand = new RelayCommand(Delete, () => Profiles.Count > 1);
        SaveCommand = new RelayCommand(Save, () => !ShowKindPicker && (Entry == null || Entry.IsValid) && Profiles.All(p => p.Options?.OptionsValid != false));
        PickKindCommand = new RelayCommand(p => PickKind(p?.ToString()));
        ChangeKindCommand = new RelayCommand(() => ShowKindPicker = true, () => Entry != null);
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

    internal AppController App => _app;

    /// <summary>Element mit den Startoptionen eines Profils (für die Befehlsvorschau).</summary>
    internal LaunchItem ItemFor(LaunchProfile p)
    {
        if (Entry == null || p.Options == null) return Item;
        var item = Entry.Current.WithProfile(p).ToLaunchItem();
        if (Entry.IsPsSnippet) { item.LaunchTarget = Entry.Current.SnippetPath; try { Core.Metadata.ScriptInfoReader.FillFromText(item, Entry.Current.Code); } catch { } }
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
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool ShowForm => !_showKindPicker;
    public IReadOnlyList<KindTile> Tiles => CustomEntryViewModel.Tiles;
    public ICommand PickKindCommand { get; }
    public ICommand ChangeKindCommand { get; }
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
            var text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty;
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
    public string HeaderDescriptionHint => Entry != null ? "Beschreibung (erscheint im Panel)" : DescriptionPlaceholder;

    // ---- Abschnitte je Art -----------------------------------------------------------------

    private CustomEntryKind? EntryKind => Entry?.Kind;
    /// <summary>Datei aus einer smarten Gruppe (oder eigener Datei-Eintrag): Script/Programm-Infos.</summary>
    public bool IsScannedFile => Entry == null;
    public string FileKindText => Item.IsPowerShell ? "PowerShell-Script" : Item.Type switch
    {
        ItemType.Batch => "Batch-Script", ItemType.Shortcut => "Verknüpfung", ItemType.ConsoleSnapIn => "Snap-in", ItemType.Executable => "Programm", _ => "Datei"
    };
    public string FileInfo => Item.ParseError ?? (Item.Synopsis.Length > 0 ? Item.Synopsis : "Keine Beschreibung im Script (Comment-Based Help .SYNOPSIS).");
    public string FileParametersText => Item.Parameters.Count == 0 ? "Keine Parameter" : "Parameter: " + string.Join(", ", Item.Parameters.Select(p => p.Name + (p.IsMandatory ? " (Pflicht)" : string.Empty)));
    public ICommand OpenFileLocationCommand => new RelayCommand(() => Launcher.OpenLocation(Item.FullPath));

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
    /// <summary>„Weitere Startprofile“ – nur wo Varianten sinnvoll sind.</summary>
    public bool ShowMoreProfiles => true;

    public ProfileEditViewModel? DefaultProfile => Profiles.FirstOrDefault(p => p.IsDefault);

    private bool _moreProfilesExpanded;
    public bool MoreProfilesExpanded { get => _moreProfilesExpanded; set { if (Set(ref _moreProfilesExpanded, value)) OnPropertyChanged(nameof(PreviewText)); } }

    /// <summary>Befehlsvorschau: Standardprofil bzw. das gerade bearbeitete weitere Profil.</summary>
    public string PreviewText => ((_moreProfilesExpanded ? Selected : DefaultProfile) ?? Selected)?.Preview ?? string.Empty;
    public string MoreProfilesText => Profiles.Count <= 1 ? "Weitere Startprofile" : $"Weitere Startprofile ({Profiles.Count - 1})";

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
            try { Core.Metadata.ScriptInfoReader.FillFromText(item, Entry.Current.Code); } catch { }
        }
        Item = item;
        var sig = ParamSignature(item);
        if (sig != _lastParams)
        {
            _lastParams = sig;
            foreach (var p in Profiles) p.RefreshParameters();
            OnPropertyChanged(nameof(HasParameters));
        }
        foreach (var p in Profiles) { p.Options?.SyncBaseFrom(Entry.Current); p.OnChanged(); }
        OnPropertyChanged(nameof(HeaderNameHint));
        OnPropertyChanged(nameof(HeaderName));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(FirstError));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(ShowPsOptions));
        OnPropertyChanged(nameof(ShowProgramArgs));
        OnPropertyChanged(nameof(ShowRunAs));
    }

    private bool _inProfileChange;
    internal void OnProfileChanged(ProfileEditViewModel p)
    {
        if (_inProfileChange) return;
        _inProfileChange = true;
        try { OnPropertyChanged(nameof(MoreProfilesText)); OnPropertyChanged(nameof(PreviewText)); }
        finally { _inProfileChange = false; }
    }
    private readonly string? _originalKey;

    /// <summary>Felder des eigenen Eintrags (Art, Ziel, Code …); null bei Dateien aus Ordnern.</summary>
    public CustomEntryViewModel? Entry { get; }
    public bool HasEntry => Entry != null;
    public string Title => ShowKindPicker ? "Was möchtest du anlegen?" : Entry != null ? (IsNew && string.IsNullOrWhiteSpace(Entry.Name) ? "Neuer Eintrag: " + Entry.KindLabel : Entry.Title) : Item.FileName;
    public string PathText => Entry != null ? Entry.KindLabel : Item.FullPath;
    public string WindowTitle => ShowKindPicker ? "Neuer Eintrag" : (IsNew ? "Neuer Eintrag – " : "Eintrag – ") + (Entry?.KindLabel ?? FileKindText);

    private bool _singleClick;
    /// <summary>Ein Klick im Panel startet sofort (statt aufzuklappen).</summary>
    public bool SingleClick { get => _singleClick; set => Set(ref _singleClick, value); }

    /// <summary>Vorhandene Schlagworte (global) für Vorschläge.</summary>
    public IReadOnlyList<string> AllTagsList => Core.Config.Tags.All(_target);

    private string _error = string.Empty;
    public string Error { get => _error; private set => Set(ref _error, value); }
    public bool IsPowerShell => Item.IsPowerShell;
    public bool IsProgram => !Item.IsPowerShell;
    /// <summary>Erster Eingabefehler – nicht in Schritt 1 (dort ist noch nichts eingegeben).</summary>
    public string FirstError => ShowKindPicker ? string.Empty : Entry?.FirstError ?? string.Empty;
    public bool HasParameters => Item.Parameters.Count > 0;
    public bool SupportsNoExit => Item.Type is ItemType.PowerShell or ItemType.Batch;
    public string SaveLabel => _saveImmediately ? "Speichern" : "Übernehmen";
    public string SaveHint => _saveImmediately
        ? "Kennwörter und Parameter vom Typ PSCredential/SecureString werden nie gespeichert."
        : "Wird sofort übernommen. Kennwörter werden nie gespeichert.";
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
        OnPropertyChanged(nameof(DefaultProfile));
    }

    private void Add()
    {
        var p = new ProfileEditViewModel(this, ProfileResolver.CreateNew(_target, Item, $"Profil {Profiles.Count + 1}"));
        Profiles.Add(p);
        Selected = p;
        OnPropertyChanged(nameof(MoreProfilesText));
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
        OnPropertyChanged(nameof(MoreProfilesText));
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
            Profiles = Profiles.Select(p => p.ToProfile()).ToList()
        };
        if (entry.Category.Length > 0 && !_target.CategoryOrder.Contains(entry.Category, StringComparer.CurrentCultureIgnoreCase))
            _target.CategoryOrder = GroupManager.All(_target).Append(entry.Category).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        _target.Entries.RemoveAll(e => string.Equals(e.Path, key, StringComparison.OrdinalIgnoreCase));
        _target.Entries.Add(entry);
        SavedKey = key;
        if (_saveImmediately) _app.OnProfilesSaved();
        Saved?.Invoke();
        CloseRequested?.Invoke();
    }

    /// <summary>Schlüssel des gespeicherten Eintrags (nach dem Speichern).</summary>
    public string? SavedKey { get; private set; }
}
