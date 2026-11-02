using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

public sealed class ParameterViewModel : ObservableObject
{
    private string _value = string.Empty;
    private bool _boolValue;

    public ParameterViewModel(ScriptParameter p, string? initial = null, bool isFixed = false)
    {
        Parameter = p;
        IsFixed = isFixed;
        var def = initial ?? p.DefaultValueLiteral;
        if (p.Kind is ParameterKind.Switch or ParameterKind.Boolean)
            _boolValue = def != null && def.Equals("true", StringComparison.OrdinalIgnoreCase);
        else if (def != null)
            _value = def;
        else if (p.Kind == ParameterKind.Choice && p.IsMandatory && p.ValidValues.Count > 0)
            _value = p.ValidValues[0];
    }

    /// <summary>Im Startprofil fest hinterlegt – im Dialog nicht änderbar.</summary>
    public bool IsFixed { get; }
    public bool IsEditable => !IsFixed;
    public bool IsSecure => Parameter.Kind == ParameterKind.Secure;

    public ScriptParameter Parameter { get; }
    public string Name => Parameter.Name;
    public string TypeLabel => Parameter.TypeName.Length == 0 ? string.Empty : $"[{Parameter.TypeName}]";
    public string HelpText => Parameter.HelpText;
    public bool HasHelp => !string.IsNullOrWhiteSpace(Parameter.HelpText);
    public bool IsMandatory => Parameter.IsMandatory;
    public bool IsCheck => Parameter.Kind is ParameterKind.Switch or ParameterKind.Boolean;
    public bool IsChoice => Parameter.Kind == ParameterKind.Choice;
    public bool IsText => !IsCheck && !IsChoice && !IsSecure;
    public IReadOnlyList<string> Choices => Parameter.IsMandatory ? Parameter.ValidValues : new[] { string.Empty }.Concat(Parameter.ValidValues).ToList();

    public string Placeholder => Parameter.Kind switch
    {
        ParameterKind.List => "Mehrere Werte durch Komma trennen",
        ParameterKind.Number => "Zahl",
        _ => Parameter.DefaultValueText is { Length: > 0 } d && Parameter.DefaultValueLiteral == null ? $"Standard: {d}" : string.Empty
    };

    public string Value { get => _value; set => Set(ref _value, value ?? string.Empty); }
    public bool BoolValue { get => _boolValue; set => Set(ref _boolValue, value); }

    public string? RawValue => IsCheck ? (BoolValue ? "true" : "false") : Value;
}

public sealed class MachineViewModel : ObservableObject
{
    private readonly RunViewModel _owner;
    private bool _isSelected;
    private bool _isFavorite;

    public MachineViewModel(RunViewModel owner, string name, bool favorite, string lastUsed, bool selected)
    {
        _owner = owner;
        Name = name;
        _isFavorite = favorite;
        LastUsed = lastUsed;
        _isSelected = selected;
        ToggleFavoriteCommand = new RelayCommand(() => IsFavorite = !IsFavorite);
        RemoveCommand = new RelayCommand(() => IsSelected = false);
    }

    public string Name { get; }
    public string LastUsed { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) _owner.OnMachinesChanged(); }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (Set(ref _isFavorite, value)) _owner.OnFavoriteChanged(this); }
    }

    public ICommand ToggleFavoriteCommand { get; }
    public ICommand RemoveCommand { get; }

    private string? _testText;
    private bool? _testOk;

    /// <summary>Ergebnis des Verbindungstests (null = nicht getestet).</summary>
    public string? TestText { get => _testText; set { if (Set(ref _testText, value)) OnPropertyChanged(nameof(HasTest)); } }
    public bool? TestOk { get => _testOk; set => Set(ref _testOk, value); }
    public bool HasTest => _testText != null;
}

public sealed class RunViewModel : ObservableObject
{
    private readonly AppController _app;
    private ExecutionMode _mode;
    private PsEngine _engine;
    private bool _remote;
    private bool _noExit;
    private bool _noProfile;
    private bool _copyFolder;
    private string _newMachine = string.Empty;
    private string _preview = string.Empty;
    private string _validation = string.Empty;

    public RunViewModel(AppController app, LaunchItem item, ExecutionMode mode, bool remote, LaunchProfile? profile = null)
    {
        _app = app;
        Item = item;
        var s = app.Settings;
        Profile = profile ?? ProfileResolver.GetDefault(s, item);
        _mode = mode;
        _remote = remote && item.SupportsRemote;
        _noExit = Profile.NoExit;
        _noProfile = Profile.NoProfile;
        _copyFolder = item.CopyFolderByDefault;
        _engine = _remote && Profile.IsImplicit
            ? (s.RemoteEngine == RemoteEngineMode.PreferPowerShell7 ? PsEngine.PowerShell7 : PsEngine.WindowsPowerShell)
            : _remote ? Profile.Engine : Launcher.EffectiveEngine(Profile.Engine);

        var initial = ProfileResolver.InitialValues(Profile, item);
        foreach (var p in item.Parameters)
        {
            var vm = new ParameterViewModel(p, initial.GetValueOrDefault(p.Name), ProfileResolver.IsFixed(Profile, p));
            vm.PropertyChanged += (_, _) => UpdatePreview();
            Parameters.Add(vm);
        }

        var preselect = Profile.Target == ProfileTarget.Fixed
            ? new HashSet<string>(Profile.Machines, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in s.Machines.OrderByDescending(m => m.LastUsedUtc))
        {
            var vm = new MachineViewModel(this, m.Name, m.Favorite, MachineList.RelativeTime(m.LastUsedUtc), preselect.Remove(m.Name));
            (m.Favorite ? FavoriteMachines : RecentMachines).Add(vm);
        }
        foreach (var name in preselect)
            RecentMachines.Insert(0, new MachineViewModel(this, name, false, "aus Profil", true));

        AddMachineCommand = new RelayCommand(AddMachines, () => !string.IsNullOrWhiteSpace(NewMachine));
        TestConnectionCommand = new RelayCommand(() => _ = TestConnectionsAsync(), () => _remote && !_testing && SelectedMachines.Any());
        RunCommand = new RelayCommand(Execute, () => CanRun);
        UpdatePreview();
    }

    public LaunchItem Item { get; }
    public LaunchProfile Profile { get; }
    public string ProfileText => Profile.IsImplicit ? string.Empty : $"Startprofil: {Profile.Name}";
    public bool HasProfile => !Profile.IsImplicit;
    public string Title => Item.DisplayName;
    public string PathText => Item.FullPath;
    public string Synopsis => Item.Synopsis;
    public string Description => Item.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Item.Description);
    public bool HasParameters => Parameters.Count > 0;
    public bool SupportsRemote => Item.SupportsRemote;
    public bool IsPowerShell => Item.IsPowerShell;
    public string? ParseError => Item.ParseError;

    public ObservableCollection<ParameterViewModel> Parameters { get; } = new();
    public ObservableCollection<MachineViewModel> FavoriteMachines { get; } = new();
    public ObservableCollection<MachineViewModel> RecentMachines { get; } = new();
    public IEnumerable<MachineViewModel> SelectedMachines => FavoriteMachines.Concat(RecentMachines).Where(m => m.IsSelected);

    public ExecutionMode Mode
    {
        get => _mode;
        set { if (Set(ref _mode, value)) { OnPropertyChanged(nameof(ModeInfo)); UpdatePreview(); } }
    }

    public PsEngine Engine
    {
        get => _engine;
        set { if (Set(ref _engine, value)) { OnPropertyChanged(nameof(EngineWarning)); UpdatePreview(); } }
    }

    public IReadOnlyList<KeyValuePair<PsEngine, string>> Engines { get; } = new[]
    {
        new KeyValuePair<PsEngine, string>(PsEngine.PowerShell7, "PowerShell 7 (pwsh.exe)"),
        new KeyValuePair<PsEngine, string>(PsEngine.WindowsPowerShell, "Windows PowerShell 5.1")
    };

    public bool IsRemote
    {
        get => _remote;
        set
        {
            if (!Set(ref _remote, value)) return;
            OnPropertyChanged(nameof(IsLocal));
            OnPropertyChanged(nameof(ModeInfo));
            OnPropertyChanged(nameof(ReadHostWarning));
            OnPropertyChanged(nameof(EngineWarning));
            UpdatePreview();
        }
    }

    public bool IsLocal
    {
        get => !_remote;
        set => IsRemote = !value;
    }

    public bool NoExit { get => _noExit; set { if (Set(ref _noExit, value)) UpdatePreview(); } }
    public bool NoProfile { get => _noProfile; set { if (Set(ref _noProfile, value)) UpdatePreview(); } }
    public bool CopyFolder { get => _copyFolder; set { if (Set(ref _copyFolder, value)) UpdatePreview(); } }

    public string NewMachine
    {
        get => _newMachine;
        set => Set(ref _newMachine, value ?? string.Empty);
    }

    public string ModeInfo => (_mode, _remote) switch
    {
        (ExecutionMode.CurrentUser, false) => "Läuft mit deinem Konto, ohne erhöhte Rechte.",
        (ExecutionMode.CurrentUser, true) => "Verbindung mit deinem aktuellen Konto (Kerberos/WinRM).",
        (ExecutionMode.Administrator, false) => "Windows fragt per UAC nach Bestätigung.",
        (ExecutionMode.Administrator, true) => "Remote-Sitzungen laufen bereits mit den vollen Rechten des verbundenen Kontos. Für ein anderes Admin-Konto „Anderer Benutzer“ wählen.",
        _ => "Benutzername und Kennwort werden beim Start abgefragt und nicht gespeichert."
    };

    public string? EngineWarning
    {
        get
        {
            if (_remote) return _engine == PsEngine.PowerShell7
                ? "Auf den meisten Servern gibt es nur 5.1. Fehlt der Endpunkt PowerShell.7, wird automatisch 5.1 verwendet."
                : null;
            return _engine == PsEngine.PowerShell7 && !Launcher.IsPwshInstalled
                ? "PowerShell 7 ist auf diesem PC nicht installiert."
                : null;
        }
    }

    public string? ReadHostWarning => _remote && Item.UsesReadHost
        ? "Das Script verwendet Read-Host. Bei Remote-Ausführung kann niemand antworten – es bricht ab."
        : null;

    public string? ModulesInfo => Item.RequiredModules.Count > 0
        ? "Benötigte Module (#Requires): " + string.Join(", ", Item.RequiredModules) + ". Bei Remote wird vorher geprüft, ob sie vorhanden sind."
        : null;

    public string Preview { get => _preview; private set => Set(ref _preview, value); }
    public string Validation { get => _validation; private set => Set(ref _validation, value); }

    public bool CanRun => !_remote || SelectedMachines.Any();

    public string RunLabel
    {
        get
        {
            if (!_remote) return "Ausführen";
            var sel = SelectedMachines.ToList();
            return sel.Count switch
            {
                0 => "Rechner auswählen",
                1 => $"Auf {sel[0].Name} ausführen",
                _ => $"Auf {sel.Count} Rechnern ausführen"
            };
        }
    }

    public string FootNote => _mode == ExecutionMode.OtherUser
        ? "Beim Start werden die Anmeldedaten abgefragt. Nichts wird gespeichert."
        : _remote
            ? "Vor dem Start wird pro Rechner geprüft: erreichbar, PowerShell-Version, Module aus #Requires."
            : "Befehlsvorschau: So startet Power-Exec das Script.";

    public ICommand AddMachineCommand { get; }
    public ICommand TestConnectionCommand { get; }

    private bool _testing;
    public bool IsTesting { get => _testing; private set { if (Set(ref _testing, value)) { OnPropertyChanged(nameof(TestButtonText)); CommandManager.InvalidateRequerySuggested(); } } }
    public string TestButtonText => _testing ? "Teste …" : "Verbindung testen";
    public string SslHint => _app.Settings.RemoteUseSsl ? "Verbindung über HTTPS (5986)" : "Verbindung über HTTP (5985)";

    /// <summary>Prüft die gewählten Rechner: WinRM erreichbar, Anmeldung, PowerShell-Version, Endpunkt PowerShell.7.</summary>
    private async Task TestConnectionsAsync()
    {
        var machines = SelectedMachines.ToList();
        if (machines.Count == 0) return;
        RunCredential? cred = null;
        if (_mode == ExecutionMode.OtherUser)
        {
            cred = Views.CredentialWindow.Prompt(OwnerWindow?.Invoke(), "Verbindungstest", remote: true);
            if (cred == null) return;
        }
        IsTesting = true;
        foreach (var m in machines) { m.TestText = "wird getestet …"; m.TestOk = null; }
        var results = await RemoteTestService.RunAsync(machines.Select(m => m.Name).ToList(), _app.Settings, cred);
        foreach (var m in machines)
        {
            var r = results.FirstOrDefault(x => x.Computer.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
            m.TestOk = r?.Ok ?? false;
            m.TestText = r?.Summary ?? "keine Antwort";
        }
        IsTesting = false;
    }
    public ICommand RunCommand { get; }

    /// <summary>Wird vom Fenster gesetzt, um sich nach erfolgreichem Start zu schließen.</summary>
    public Action? CloseRequested { get; set; }
    public Func<System.Windows.Window?>? OwnerWindow { get; set; }

    internal void OnMachinesChanged()
    {
        OnPropertyChanged(nameof(SelectedMachines));
        OnPropertyChanged(nameof(RunLabel));
        OnPropertyChanged(nameof(CanRun));
        UpdatePreview();
        CommandManager.InvalidateRequerySuggested();
    }

    internal void OnFavoriteChanged(MachineViewModel m)
    {
        MachineList.SetFavorite(_app.Settings, m.Name, m.IsFavorite);
        _app.SaveSettings();
        if (m.IsFavorite && RecentMachines.Remove(m)) FavoriteMachines.Add(m);
        else if (!m.IsFavorite && FavoriteMachines.Remove(m)) RecentMachines.Insert(0, m);
    }

    private void AddMachines()
    {
        foreach (var name in MachineList.ParseNames(NewMachine))
        {
            var existing = FavoriteMachines.Concat(RecentMachines).FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.IsSelected = true;
                continue;
            }
            RecentMachines.Insert(0, new MachineViewModel(this, name, false, "neu", true));
        }
        NewMachine = string.Empty;
        OnMachinesChanged();
    }

    private Dictionary<string, string?> RawValues() =>
        Parameters.ToDictionary(p => p.Name, p => p.RawValue, StringComparer.OrdinalIgnoreCase);

    private void UpdatePreview()
    {
        ParameterValueConverter.TryConvert(Item.Parameters, RawValues(), out var values);
        var req = BuildRequest(values);
        Preview = _remote ? CommandBuilder.RemotePreview(req) : CommandBuilder.BuildLocal(req).Preview;
        OnPropertyChanged(nameof(FootNote));
    }

    private ExecutionRequest BuildRequest(Dictionary<string, object> values) => new()
    {
        Item = Item,
        Mode = _mode,
        Engine = _engine,
        NoExit = _noExit,
        NoProfile = _noProfile,
        ExecutionPolicy = _app.Settings.ExecutionPolicy,
        Parameters = values,
        Targets = _remote ? SelectedMachines.Select(m => m.Name).ToList() : Array.Empty<string>(),
        CopyFolder = _remote && _copyFolder,
        ResolvedPath = PathResolver.ToUnc(Item.FullPath)
    };

    private void Execute()
    {
        var errors = ParameterValueConverter.TryConvert(Item.Parameters, RawValues(), out var values);
        if (errors.Count > 0)
        {
            Validation = string.Join("\n", errors);
            return;
        }
        Validation = string.Empty;
        var req = BuildRequest(values);
        if (!_app.ConfirmRun(Item, Profile, _remote ? req.Targets.Count : 0, OwnerWindow?.Invoke())) return;

        if (_remote)
        {
            MachineList.MarkUsed(_app.Settings, req.Targets);
            _app.SaveSettings();
            if (_app.StartRemote(req, OwnerWindow?.Invoke()))
            {
                _app.RecordRun(Item, Profile);
                AppLog.Info($"Start (Dialog): {Item.FullPath} · {req.Targets.Count} Rechner");
                CloseRequested?.Invoke();
            }
        }
        else if (_app.Launcher.StartLocal(req, OwnerWindow?.Invoke()))
        {
            _app.RecordRun(Item, Profile);
            AppLog.Info($"Start (Dialog): {Item.FullPath} · lokal{(req.Mode == ExecutionMode.Administrator ? " · Admin" : string.Empty)}");
            CloseRequested?.Invoke();
        }
    }
}
