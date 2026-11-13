using System.Globalization;
using PowerExec.Core.Localization;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;

/// <summary>Kachel im ersten Schritt des Eintragsdialogs.</summary>
public sealed record KindTile(string Key, string Label, string Glyph);

/// <summary>Werkzeug aus dem Katalog in der Werkzeug-Auswahl.</summary>
public sealed record ToolChoice(CatalogTemplate Template)
{
    public string Name => Template.Name;
    public string Detail => Template.Category + " · " + Template.Target;
    public string Icon => Template.Icon;
}

/// <summary>
/// Felder eines eigenen Eintrags – je Art eigene Felder, Prüfung live am Feld (<see cref="EntryValidation"/>).
/// Gemeinsame Angaben (Name, Beschreibung, Symbol) bindet der Kopf des Dialogs.
/// </summary>
public sealed class CustomEntryViewModel : ObservableObject
{
    private readonly ICustomEntryHost _app;
    private CustomEntry _e;
    private IReadOnlyList<EntryIssue> _issues = Array.Empty<EntryIssue>();
    private readonly IUiTimer _checkTimer;
    private bool _useRdpFile;
    private string _autoTitle = string.Empty;
    private int _checkVersion;

    /// <param name="optionsOnly">Nur die Startoptionen eines Profils bearbeiten (ohne Ziel/Code, ohne Netzwerkprüfungen).</param>
    public CustomEntryViewModel(CustomEntry entry, ICustomEntryHost app, bool optionsOnly = false)
    {
        _app = app;
        _optionsOnly = optionsOnly;
        _e = entry.Clone();
        _useRdpFile = _e.RdpFile.Trim().Length > 0;
        BrowseCommand = new RelayCommand(Browse, () => CanBrowse);
        BrowseKeyCommand = new RelayCommand(() => PickFile(L.T("Key files (*.ppk;*.pem;id_*)|*.ppk;*.pem;id_*|All files (*.*)|*.*"), f => KeyFile = f));
        BrowseRdpFileCommand = new RelayCommand(() => PickFile(L.T("Remote Desktop connections (*.rdp)|*.rdp"), f => RdpFile = f));
        BrowseOpenWithCommand = new RelayCommand(() => PickFile(L.T("Programs (*.exe)|*.exe"), f => OpenWithProgram = f));
        BrowseWorkDirCommand = new RelayCommand(() => PickFolder(f => WorkingDirectory = f));
        TestCommand = new RelayCommand(Test, () => IsValid && _e.Placeholders.Count == 0);
        ChooseIconCommand = new RelayCommand(ChooseIcon);
        ClearIconCommand = new RelayCommand(() => Icon = string.Empty, () => _e.Icon.Length > 0);
        FetchTitleCommand = new RelayCommand(() => FetchTitleAsync(force: true).Forget(), () => IsUrl && Uri.TryCreate(_e.Target.Trim(), UriKind.Absolute, out _));
        PickToolCommand = new RelayCommand(p => { if (p is ToolChoice t) ApplyTool(t.Template); });
        SmartGroupCommand = new RelayCommand(() => SmartGroupRequested?.Invoke(_e.Target.Trim()), () => IsFolder && _e.Target.Trim().Length > 0);
        _checkTimer = app.UiDispatcher.CreateTimer(TimeSpan.FromMilliseconds(700), () => { _checkTimer!.Stop(); RunDelayedChecks(); });
        Revalidate();
        if (!optionsOnly) RunDelayedChecks();
    }

    private readonly bool _optionsOnly;
    /// <summary>Felder des Eintrags (Ziel, Code …) anzeigen.</summary>
    public bool ShowBase => !_optionsOnly;
    /// <summary>Startoptionen anzeigen (im Profil-Editor).</summary>
    public bool ShowOptions => _optionsOnly;

    /// <summary>Hat diese Art Startoptionen, die je Profil verschieden sein können?</summary>
    public bool HasOptions => _e.Kind is not CustomEntryKind.Snippet || _e.Language == SnippetLanguage.Text;

    /// <summary>Profil-Editor: Grundwerte (Ziel, Art, Code …) vom Eintrag übernehmen, Startoptionen behalten.</summary>
    public void SyncBaseFrom(CustomEntry b)
    {
        var opts = EntryOptions.From(_e);
        var kindChanged = b.Kind != _e.Kind || b.Language != _e.Language;
        _e = b.Clone();
        opts.ApplyTo(_e);
        if (kindChanged) OnPropertyChanged(string.Empty);
        else foreach (var p in new[] { nameof(UseRdpFile), nameof(UseRdpFields), nameof(Target) }) OnPropertyChanged(p);
        _useRdpFile = _e.RdpFile.Trim().Length > 0;
        Revalidate();
    }

    /// <summary>Fehler in Startoptionen (für Profile).</summary>
    public bool OptionsValid => !_issues.Any(i => i.IsError && i.Field is EntryField.ScreenSize or EntryField.PortForward or EntryField.OpenWithProgram);

    // ---- Art -------------------------------------------------------------------

    public static IReadOnlyList<KindTile> Tiles { get; } = new[]
    {
        new KindTile("Url", L.T("Website"), "\uE774"),
        new KindTile("Folder", L.T("Folder / share"), "\uE8B7"),
        new KindTile("Rdp", L.T("Remote Desktop"), "\uE8AF"),
        new KindTile("Ssh", L.T("SSH connection"), "\uE756"),
        new KindTile("Snippet", L.T("PowerShell snippet"), "\uE943"),
        new KindTile("TextSnippet", L.T("Text snippet"), "\uE8C8"),
        new KindTile("Tool", L.T("Windows tool"), "\uE90F"),
        new KindTile("Command", L.T("Command / program"), "\uE7C4"),
        new KindTile("File", L.T("File"), "\uE8A5")
    };

    public CustomEntryKind Kind => _e.Kind;
    public string KindKey => _e.KindKey;
    public string KindLabel => Tiles.FirstOrDefault(t => t.Key == KindKey)?.Label ?? CustomEntry.KindLabel(_e.Kind);
    public string KindGlyph => Tiles.FirstOrDefault(t => t.Key == KindKey)?.Glyph ?? "\uE71B";

    /// <summary>Art wechseln; passende Werte bleiben, beim Speichern wird Unpassendes verworfen.</summary>
    public void SetKind(string key)
    {
        if (CustomEntry.FromKindKey(key) is not { } template) return;
        _e.Kind = template.Kind;
        if (template.Kind == CustomEntryKind.Snippet) _e.Language = template.Language;
        RaiseAll();
    }

    public bool IsUrl => _e.Kind == CustomEntryKind.Url;
    public bool IsFolder => _e.Kind == CustomEntryKind.Folder;
    public bool IsRdp => _e.Kind == CustomEntryKind.Rdp;
    public bool IsSsh => _e.Kind == CustomEntryKind.Ssh;
    public bool IsSnippet => _e.Kind == CustomEntryKind.Snippet;
    public bool IsPsSnippet => IsSnippet && _e.Language == SnippetLanguage.PowerShell;
    public bool IsTextSnippet => IsSnippet && _e.Language == SnippetLanguage.Text;
    public bool IsTool => _e.Kind == CustomEntryKind.Tool;
    public bool IsCommand => _e.Kind == CustomEntryKind.Command;
    public bool IsFile => _e.Kind == CustomEntryKind.File;
    /// <summary>Befehl, Datei, Werkzeug: Arbeitsordner und Fenster.</summary>
    public bool IsProgramLike => _e.Kind is CustomEntryKind.Command or CustomEntryKind.Tool or CustomEntryKind.File;
    public bool HasArguments => _e.Kind is CustomEntryKind.Command or CustomEntryKind.Tool;
    public bool CanBrowse => _e.Kind is CustomEntryKind.Folder or CustomEntryKind.Command or CustomEntryKind.File or CustomEntryKind.Tool;

    // ---- Gemeinsam -----------------------------------------------------------------

    public string Name { get => _e.Name; set { _e.Name = value ?? string.Empty; Touch(); OnPropertyChanged(nameof(Title)); } }
    public string Description { get => _e.Description; set { _e.Description = value ?? string.Empty; Touch(); } }
    public string Title => string.IsNullOrWhiteSpace(_e.Name) ? (string.IsNullOrWhiteSpace(_e.Target) ? L.T("New entry") : _e.Target) : _e.Name;
    /// <summary>Vorschlag für den Anzeigenamen (Platzhalter im Namensfeld).</summary>
    public string NameSuggestion => _e.ToLaunchItem().DisplayName is { Length: > 0 } n && n != "Snippet" ? n : L.T("Display name");

    public string Icon { get => _e.Icon; set { _e.Icon = value ?? string.Empty; Touch(); OnPropertyChanged(nameof(HasIcon)); OnPropertyChanged(nameof(ShowAutoIcon)); OnPropertyChanged(nameof(ShowBadge)); CommandRequery.Invalidate(); } }
    public bool HasIcon => _e.Icon.Length > 0;

    private IconImage? _autoIcon;
    /// <summary>Automatisches Symbol (Favicon, Programm-, Ordnersymbol) für die Vorschau im Kopf.</summary>
    public IconImage? AutoIcon { get => _autoIcon; private set { if (Set(ref _autoIcon, value)) { OnPropertyChanged(nameof(ShowAutoIcon)); OnPropertyChanged(nameof(ShowBadge)); } } }
    public bool ShowAutoIcon => !HasIcon && _autoIcon != null;
    public bool ShowBadge => !HasIcon && _autoIcon == null;
    public string Badge => PanelBadge(_e.Kind, _e.Target, _e.Language);

    public string Target
    {
        get => _e.Target;
        set
        {
            _e.Target = value ?? string.Empty;
            Touch();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Badge));
            OnPropertyChanged(nameof(NameSuggestion));
            ScheduleChecks();
        }
    }
    public string Arguments { get => _e.Arguments; set { _e.Arguments = value ?? string.Empty; Touch(); } }

    // ---- Webseite --------------------------------------------------------------

    public BrowserChoice Browser
    {
        get => _e.Browser;
        set
        {
            if (_e.Browser != value) _e.BrowserProfile = string.Empty; // Profile gehören zum Browser
            _e.Browser = value;
            Touch();
            foreach (var p in new[] { nameof(HasBrowserOptions), nameof(AppWindowHint), nameof(BrowserProfile), nameof(BrowserProfileList), nameof(HasBrowserProfiles) }) OnPropertyChanged(p);
        }
    }
    public bool HasBrowserOptions => _e.Browser != BrowserChoice.Default;
    public bool Private { get => _e.Private; set { _e.Private = value; Touch(); } }
    public string BrowserProfile { get => _e.BrowserProfile; set { _e.BrowserProfile = value ?? string.Empty; Touch(); } }
    /// <summary>Kurzinfo der Startoptionen (Profilliste).</summary>
    public string OptionsSummary => EntryOptions.From(_e).Summary(_e.Kind, BrowserCatalog.ProfileDisplay);
    public bool AppWindow { get => _e.AppWindow; set { _e.AppWindow = value; Touch(); OnPropertyChanged(nameof(AppWindowHint)); } }
    public string AppWindowHint => !_e.AppWindow ? string.Empty
        : _e.Browser == BrowserChoice.Firefox ? L.T("Firefox has no app window – the page opens in its own window.")
        : _e.Browser == BrowserChoice.Default ? L.T("Opens in Microsoft Edge without tabs and address bar.") : L.T("Without tabs and address bar.");
    public static IReadOnlyList<KeyValuePair<BrowserChoice, string>> Browsers { get; } = new[]
    {
        new KeyValuePair<BrowserChoice, string>(BrowserChoice.Default, L.T("Default browser")),
        new KeyValuePair<BrowserChoice, string>(BrowserChoice.Edge, "Microsoft Edge"),
        new KeyValuePair<BrowserChoice, string>(BrowserChoice.Chrome, "Google Chrome"),
        new KeyValuePair<BrowserChoice, string>(BrowserChoice.Firefox, "Mozilla Firefox")
    };
    /// <summary>Nur installierte Browser (plus der gespeicherte, falls er fehlt).</summary>
    public IReadOnlyList<KeyValuePair<BrowserChoice, string>> BrowserList =>
        BrowserCatalog.Installed.Append(_e.Browser).Distinct()
            .Select(b => new KeyValuePair<BrowserChoice, string>(b, BrowserCatalog.DisplayName(b) + (BrowserCatalog.Installed.Contains(b) ? string.Empty : L.T(" (not installed)"))))
            .ToList();

    /// <summary>Gefundene Profile des gewählten Browsers; leer = Standardprofil des Browsers.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> BrowserProfileList
    {
        get
        {
            var list = new List<KeyValuePair<string, string>> { new(string.Empty, L.T("Default profile")) };
            list.AddRange(BrowserCatalog.ProfilesOf(_e.Browser).Select(p => new KeyValuePair<string, string>(p.Id, p.Display)));
            if (_e.BrowserProfile.Length > 0 && !list.Any(x => x.Key.Equals(_e.BrowserProfile, StringComparison.OrdinalIgnoreCase)))
                list.Add(new(_e.BrowserProfile, _e.BrowserProfile + L.T(" (not found)")));
            return list;
        }
    }
    public bool HasBrowserProfiles => BrowserProfileList.Count > 1;

    private string _titleStatus = string.Empty;
    public string TitleStatus { get => _titleStatus; private set => Set(ref _titleStatus, value); }

    // ---- Remotedesktop ---------------------------------------------------------

    public string User { get => _e.User; set { _e.User = value ?? string.Empty; Touch(); } }
    public string PortText
    {
        get => _e.Port > 0 ? _e.Port.ToString(CultureInfo.InvariantCulture) : string.Empty;
        set { _e.Port = int.TryParse(value, out var p) ? p : value?.Trim().Length > 0 ? -1 : 0; Touch(); }
    }
    public string PortHint => IsRdp ? "3389" : "22";

    public bool UseRdpFile
    {
        get => _useRdpFile;
        set
        {
            if (!Set(ref _useRdpFile, value)) return;
            if (!value) _e.RdpFile = string.Empty;
            OnPropertyChanged(nameof(RdpFile));
            OnPropertyChanged(nameof(UseRdpFields));
            Revalidate();
        }
    }
    public bool UseRdpFields { get => !_useRdpFile; set => UseRdpFile = !value; }
    public string RdpFile { get => _e.RdpFile; set { _e.RdpFile = value ?? string.Empty; Touch(); } }
    public RdpScreen Screen { get => _e.Screen; set { _e.Screen = value; Touch(); OnPropertyChanged(nameof(IsRdpWindow)); } }
    public bool IsRdpWindow => _e.Screen == RdpScreen.Window;
    public string ScreenWidthText { get => _e.ScreenWidth > 0 ? _e.ScreenWidth.ToString(CultureInfo.InvariantCulture) : string.Empty; set { _e.ScreenWidth = int.TryParse(value, out var w) ? w : 0; Touch(); } }
    public string ScreenHeightText { get => _e.ScreenHeight > 0 ? _e.ScreenHeight.ToString(CultureInfo.InvariantCulture) : string.Empty; set { _e.ScreenHeight = int.TryParse(value, out var h) ? h : 0; Touch(); } }
    public bool AllMonitors { get => _e.AllMonitors; set { _e.AllMonitors = value; Touch(); } }
    public bool AdminSession { get => _e.AdminSession; set { _e.AdminSession = value; Touch(); } }
    public string Gateway { get => _e.Gateway; set { _e.Gateway = value ?? string.Empty; Touch(); } }
    public string RdpHint => _e.NeedsGeneratedRdpFile
        ? L.T("Power-Exec passes user and gateway via a generated .rdp file – without a password. Windows asks for the password when connecting.")
        : L.T("Windows asks for user name and password when connecting; nothing is saved here.");

    // ---- SSH -------------------------------------------------------------------

    public string KeyFile { get => _e.KeyFile; set { _e.KeyFile = value ?? string.Empty; Touch(); } }
    public SshClient Client { get => _e.Client; set { _e.Client = value; Touch(); OnPropertyChanged(nameof(IsTerminalClient)); } }
    public bool IsTerminalClient => _e.Client == SshClient.Terminal;
    public string RemoteCommand { get => _e.RemoteCommand; set { _e.RemoteCommand = value ?? string.Empty; Touch(); } }
    public string JumpHost { get => _e.JumpHost; set { _e.JumpHost = value ?? string.Empty; Touch(); } }
    public string PortForward { get => _e.PortForward; set { _e.PortForward = value ?? string.Empty; Touch(); } }
    public string TerminalProfile { get => _e.TerminalProfile; set { _e.TerminalProfile = value ?? string.Empty; Touch(); } }
    public static IReadOnlyList<KeyValuePair<SshClient, string>> Clients { get; } = new[]
    {
        new KeyValuePair<SshClient, string>(SshClient.Terminal, "Windows Terminal"),
        new KeyValuePair<SshClient, string>(SshClient.Console, L.T("Console (ssh.exe)")),
        new KeyValuePair<SshClient, string>(SshClient.Putty, "PuTTY")
    };
    public IReadOnlyList<KeyValuePair<SshClient, string>> ClientList => Clients;

    // ---- Snippets --------------------------------------------------------------

    public string Code
    {
        get => _e.Code;
        set { _e.Code = value ?? string.Empty; Touch(); OnPropertyChanged(nameof(LineNumbers)); ScheduleChecks(); }
    }
    public SnippetAction Action { get => _e.Action; set { _e.Action = value; Touch(); } }
    public bool CopyInsteadOfRun { get => _e.Action == SnippetAction.Copy; set => Action = value ? SnippetAction.Copy : SnippetAction.Run; }
    public bool PasteAfterCopy { get => _e.PasteAfterCopy; set { _e.PasteAfterCopy = value; Touch(); } }
    public bool CopyAsHtml { get => _e.CopyAsHtml; set { _e.CopyAsHtml = value; Touch(); } }

    /// <summary>Zeilennummern für den Code-Editor.</summary>
    public string LineNumbers => string.Join("\n", Enumerable.Range(1, Math.Max(1, _e.Code.Split('\n').Length)));

    private IReadOnlyList<SyntaxIssue> _syntax = Array.Empty<SyntaxIssue>();
    public IReadOnlyList<SyntaxIssue> SyntaxErrors { get => _syntax; private set { if (Set(ref _syntax, value)) OnPropertyChanged(nameof(HasSyntaxErrors)); } }
    public bool HasSyntaxErrors => _syntax.Count > 0;

    private string _parameterPreview = string.Empty;
    /// <summary>Welche param()-Parameter beim Start abgefragt werden.</summary>
    public string ParameterPreview { get => _parameterPreview; private set => Set(ref _parameterPreview, value); }

    /// <summary>Variablen zum Einfügen: eingebaute und eigene ({{$Name}}).</summary>
    public IReadOnlyList<string> Builtins =>
        Placeholders.BuiltinNames.Select(n => "{{" + n + "}}").Concat(_app.UserVariables.Select(v => "{{$" + v.Name + "}}")).ToList();

    /// <summary>{{$Variablen}}, die es nicht gibt – Hinweis im Editor (der Start bricht sonst ab).</summary>
    public string? VariableWarning
    {
        get
        {
            var unknown = Placeholders.VariableNames(_e.Target, _e.Arguments, _e.Code, _e.User, _e.RemoteCommand, _e.WorkingDirectory)
                .Where(n => !Variables.IsBuiltin(n) && !_app.UserVariables.Any(v => v.Name.Equals(Variables.Normalize(n), StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            return unknown.Count == 0 ? null
                : (unknown.Count == 1 ? L.F("The variable {{{{{0}}}}} doesn't exist", unknown[0]) : L.T("These variables don't exist: ") + string.Join(", ", unknown.Select(n => "{{" + n + "}}")))
                  + L.T(" – create them under Settings › Content › Variables, otherwise the entry won't start.");
        }
    }
    public bool HasVariableWarning => VariableWarning != null;

    public string PlaceholderInfo
    {
        get
        {
            var ph = _e.Placeholders;
            return ph.Count == 0
                ? L.T("{{Name}} or {{Name:Default}} is asked for at launch; Power-Exec fills in {{$Variables}} (built-in and custom) itself.")
                : L.T("Asked for at launch: ") + string.Join(", ", ph.Select(p => p.Default.Length > 0 ? L.F("{0} (default {1})", p.Name, p.Default) : p.Name));
        }
    }
    public bool HasPlaceholders => _e.Placeholders.Count > 0;

    // ---- Ordner ----------------------------------------------------------------

    public FolderOpener OpenWith { get => _e.OpenWith; set { _e.OpenWith = value; Touch(); OnPropertyChanged(nameof(IsOpenWithProgram)); } }
    public bool IsOpenWithProgram => _e.OpenWith == FolderOpener.Program;
    public string OpenWithProgram { get => _e.OpenWithProgram; set { _e.OpenWithProgram = value ?? string.Empty; Touch(); } }

    private string _targetStatus = string.Empty;
    private bool? _targetOk;
    /// <summary>Ergebnis der Erreichbarkeitsprüfung (Ordner, Datei, Programm).</summary>
    public string TargetStatus { get => _targetStatus; private set => Set(ref _targetStatus, value); }
    public bool? TargetOk { get => _targetOk; private set => Set(ref _targetOk, value); }

    // ---- Befehl, Datei, Werkzeug ---------------------------------------------------

    public string WorkingDirectory { get => _e.WorkingDirectory; set { _e.WorkingDirectory = value ?? string.Empty; Touch(); } }
    public LaunchWindowState WindowState { get => _e.WindowState; set { _e.WindowState = value; Touch(); } }
    public static IReadOnlyList<KeyValuePair<LaunchWindowState, string>> WindowStates { get; } = new[]
    {
        new KeyValuePair<LaunchWindowState, string>(LaunchWindowState.Normal, "Normal"),
        new KeyValuePair<LaunchWindowState, string>(LaunchWindowState.Maximized, L.T("Maximized")),
        new KeyValuePair<LaunchWindowState, string>(LaunchWindowState.Minimized, L.T("Minimized"))
    };
    public IReadOnlyList<KeyValuePair<LaunchWindowState, string>> WindowStateList => WindowStates;

    private string _toolSearch = string.Empty;
    public string ToolSearch { get => _toolSearch; set { if (Set(ref _toolSearch, value ?? string.Empty)) OnPropertyChanged(nameof(ToolChoices)); } }
    public IReadOnlyList<ToolChoice> ToolChoices
    {
        get
        {
            var words = _toolSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return ToolCatalog.All.Where(t => t.Kind == CustomEntryKind.Tool)
                .Where(t => words.All(w => (t.Name + " " + t.Target + " " + t.Category + " " + t.Description).Contains(w, StringComparison.CurrentCultureIgnoreCase)))
                .Select(t => new ToolChoice(t)).ToList();
        }
    }

    private void ApplyTool(CatalogTemplate t)
    {
        Target = t.Target;
        Arguments = t.Arguments;
        if (string.IsNullOrWhiteSpace(_e.Name)) Name = t.Name;
        if (string.IsNullOrWhiteSpace(_e.Description)) Description = t.Description;
        if (_e.Icon.Length == 0) Icon = t.Icon;
    }

    // ---- Prüfung -------------------------------------------------------------------

    private string? ErrorOf(EntryField f) => _issues.FirstOrDefault(i => i.Field == f && i.IsError)?.Message;
    public string? TargetError => ErrorOf(EntryField.Target);
    public string? CodeError => ErrorOf(EntryField.Code);
    public string? PortError => ErrorOf(EntryField.Port);
    public string? RdpFileError => ErrorOf(EntryField.RdpFile);
    public string? ScreenSizeError => ErrorOf(EntryField.ScreenSize);
    public string? PortForwardError => ErrorOf(EntryField.PortForward);
    public string? OpenWithProgramError => ErrorOf(EntryField.OpenWithProgram);
    /// <summary>Hinweise, die das Speichern nicht verhindern.</summary>
    public string Warnings => string.Join("\n", _issues.Where(i => !i.IsError).Select(i => i.Message));
    public bool IsValid => !_issues.Any(i => i.IsError);
    /// <summary>Erster Fehler (für die Fußzeile).</summary>
    public string? FirstError => _issues.FirstOrDefault(i => i.IsError)?.Message;

    private static readonly string[] IssueProps =
    {
        nameof(TargetError), nameof(CodeError), nameof(PortError), nameof(RdpFileError), nameof(ScreenSizeError), nameof(PortForwardError),
        nameof(OpenWithProgramError), nameof(Warnings), nameof(IsValid), nameof(FirstError), nameof(PlaceholderInfo), nameof(HasPlaceholders), nameof(RdpHint),
        nameof(VariableWarning), nameof(HasVariableWarning)
    };

    private void Revalidate()
    {
        _issues = EntryValidation.Check(_e);
        foreach (var p in IssueProps) OnPropertyChanged(p);
        OnPropertyChanged(nameof(OptionsSummary));
        OnPropertyChanged(nameof(OptionsValid));
        CommandRequery.Invalidate();
        Changed?.Invoke();
    }

    private void Touch([CallerMemberName] string? name = null)
    {
        OnPropertyChanged(name);
        Revalidate();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(string.Empty);
        Revalidate();
        ScheduleChecks();
    }

    /// <summary>Änderung an einem Feld (der Dialog aktualisiert Vorschau und Speichern-Knopf).</summary>
    public event Action? Changed;

    /// <summary>„Stattdessen als smarte Gruppe anlegen“ (Ordnerpfad).</summary>
    public event Action<string>? SmartGroupRequested;

    /// <summary>Testen über die App (mit dem eingestellten Standardverhalten). Ohne Handler: einfacher Start.</summary>
    public Action<CustomEntry>? TestHandler { get; set; }

    private void ScheduleChecks()
    {
        if (_optionsOnly) return;
        _checkTimer.Stop();
        _checkTimer.Start();
    }

    /// <summary>Verzögerte, teurere Prüfungen: Syntax, Parameter, Erreichbarkeit, Titel und Symbol.</summary>
    private void RunDelayedChecks()
    {
        var version = ++_checkVersion;
        if (IsPsSnippet)
        {
            SyntaxErrors = EntryValidation.CheckSyntax(_e.Code);
            var probe = new LaunchItem { FullPath = "snippet.ps1", RootFolder = string.Empty, Type = ItemType.PowerShell };
            try { Core.Metadata.ScriptInfoReader.FillFromText(probe, _e.Code); } catch { /* unvollständiger Code beim Tippen: Parameter-Vorschau bleibt leer */ }
            ParameterPreview = probe.Parameters.Count == 0
                ? L.T("No parameters – the snippet starts without asking.")
                : L.T("Asked for at launch: ") + string.Join(", ", probe.Parameters.Select(p =>
                    p.Name + (p.TypeName.Length > 0 ? $" [{p.TypeName}]" : string.Empty) + (p.IsMandatory ? L.T(" (required)") : string.Empty)));
        }
        else SyntaxErrors = Array.Empty<SyntaxIssue>();

        CheckTargetAsync(version).Forget();
        LoadAutoIcon();
        if (IsUrl) FetchTitleAsync(force: false).Forget();
    }

    private async Task CheckTargetAsync(int version)
    {
        var t = _e.Target.Trim();
        if (t.Length == 0 || t.Contains("{{") || !(IsFolder || IsFile || IsCommand))
        {
            TargetStatus = string.Empty;
            TargetOk = null;
            return;
        }
        TargetStatus = L.T("Checking …");
        TargetOk = null;
        bool? ok;
        try
        {
            var check = Task.Run(() => IsFolder ? Directory.Exists(t) : IsFile ? File.Exists(t) : ProgramResolver.Resolve(t) != null);
            ok = await Task.WhenAny(check, Task.Delay(3000)) == check ? check.Result : null;
        }
        catch { ok = false; }
        if (version != _checkVersion) return;
        TargetOk = ok;
        var unc = IsFolder || IsFile ? _app.ToUnc(t) : t;
        var uncHint = !unc.Equals(t, StringComparison.OrdinalIgnoreCase) ? L.F(" · network drive – other accounts reach it as {0}", unc) : string.Empty;
        TargetStatus = ok switch
        {
            true => (IsFolder ? L.T("Folder reachable") : IsFile ? L.T("File found") : L.T("Program found")) + uncHint,
            false => (IsFolder ? L.T("Folder not found") : IsFile ? L.T("File not found") : L.T("Program not found")) + L.T(" – you can still save (e.g. share currently offline)."),
            null => L.T("No response within 3 seconds (network?).")
        };
    }

    private void LoadAutoIcon()
    {
        if (HasIcon) { AutoIcon = null; return; }
        try
        {
            var item = _e.ToLaunchItem();
            AutoIcon = _e.Kind == CustomEntryKind.File && File.Exists(item.FullPath)
                ? _app.IconProvider.Get(item.FullPath, File.GetLastWriteTimeUtc(item.FullPath), img => AutoIcon = img)
                : ItemViewModel.LoadIcon(_app.IconProvider, item, img => AutoIcon = img);
        }
        catch { AutoIcon = null; }
    }

    private static readonly HttpClient Http = new(new HttpClientHandler { UseDefaultCredentials = true }) { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>Seitentitel als Anzeigename vorschlagen (nur wenn der Name leer ist bzw. der letzte Vorschlag war).</summary>
    private async Task FetchTitleAsync(bool force)
    {
        var url = _e.Target.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || url.Contains("{{")) return;
        if (!force && _e.Name.Length > 0 && _e.Name != _autoTitle) return;
        TitleStatus = L.T("Fetching title …");
        try
        {
            var html = await Http.GetStringAsync(uri);
            if (_e.Target.Trim() != url) return;
            var m = Regex.Match(html, @"<title[^>]*>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var title = m.Success ? System.Net.WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, @"\s+", " ")).Trim() : string.Empty;
            if (title.Length == 0) { TitleStatus = L.T("The page has no title."); return; }
            if (force || _e.Name.Length == 0 || _e.Name == _autoTitle)
            {
                _autoTitle = title.Length > 80 ? title[..80] : title;
                Name = _autoTitle;
            }
            TitleStatus = L.T("Title applied.");
        }
        catch (Exception ex)
        {
            if (_e.Target.Trim() == url) TitleStatus = L.T("Title not available (") + (ex is TaskCanceledException ? L.T("no response") : L.T("sign-in required or offline")) + ").";
        }
    }

    // ---- Befehle -------------------------------------------------------------------

    public ICommand BrowseCommand { get; }
    public ICommand BrowseKeyCommand { get; }
    public ICommand BrowseRdpFileCommand { get; }
    public ICommand BrowseOpenWithCommand { get; }
    public ICommand BrowseWorkDirCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand ChooseIconCommand { get; }
    public ICommand ClearIconCommand { get; }
    public ICommand FetchTitleCommand { get; }
    public ICommand PickToolCommand { get; }
    public ICommand SmartGroupCommand { get; }

    public string TargetLabel => _e.Kind switch
    {
        CustomEntryKind.Url => L.T("Address"),
        CustomEntryKind.Folder => L.T("Path"),
        CustomEntryKind.Rdp or CustomEntryKind.Ssh => L.T("Computer"),
        CustomEntryKind.File => L.T("File"),
        CustomEntryKind.Tool => L.T("Tool"),
        _ => L.T("Program")
    };

    public string TargetHint => _e.Kind switch
    {
        CustomEntryKind.Url => "https://intranet.firma.local oder https://ticket/{{Nummer}}",
        CustomEntryKind.Folder => @"\\server\freigabe\Ordner",
        CustomEntryKind.Rdp => L.T("SRV-01 or {{Computer}}"),
        CustomEntryKind.Ssh => "server.firma.local",
        CustomEntryKind.File => @"\\server\tools\Skript.ps1",
        CustomEntryKind.Tool => "dsa.msc, ms-settings:display, control.exe",
        _ => @"z. B. mmc.exe, wt.exe oder C:\Tools\tool.exe"
    };

    public static string PanelBadge(CustomEntryKind kind, string target = "", SnippetLanguage language = SnippetLanguage.PowerShell) =>
        EntryBadge.For(kind, target, language);

    private void Browse()
    {
        if (_e.Kind == CustomEntryKind.Folder) { PickFolder(f => Target = f); return; }
        PickFile(_e.Kind == CustomEntryKind.File
            ? L.T("Scripts and programs (*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc)|*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc|All files (*.*)|*.*")
            : L.T("Programs (*.exe;*.cmd;*.bat;*.msc)|*.exe;*.cmd;*.bat;*.msc|All files (*.*)|*.*"), f => Target = f);
    }

    private void PickFile(string filter, Action<string> done)
    {
        if (_app.Dialogs.OpenFile(filter) is { } file) done(file);
    }

    private void PickFolder(Action<string> done)
    {
        if (_app.Dialogs.PickFolder() is { } folder) done(folder);
    }

    private void ChooseIcon()
    {
        var picked = _app.Dialogs.PickIcon(IconRef.Parse(_e.Icon), L.T("Icon for the entry"));
        if (picked != null) Icon = picked.Value.ToString();
    }

    private void Test()
    {
        var entry = ToEntry();
        try
        {
            if (TestHandler != null) { TestHandler(entry); return; }
            if (entry.CopiesToClipboard) { _app.Shell.CopyToClipboard(entry.Code, entry.CopyAsHtml); return; }
            var item = entry.ToLaunchItem();
            if (entry.Kind == CustomEntryKind.File) { _app.Shell.OpenLocation(item.FullPath); return; }
            entry.PrepareLaunch();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.LaunchTarget!, item.LaunchArguments) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            _app.Dialogs.Warn(ex.Message, L.T("Test entry"));
        }
    }

    /// <summary>Gespeicherter Stand: Werte, die zur Art nicht passen, werden verworfen.</summary>
    public CustomEntry ToEntry() => _e.CleanedForKind();

    /// <summary>Arbeitsstand (für Vorschau und Test ohne Bereinigung).</summary>
    internal CustomEntry Current => _e;

    /// <summary>Fehlermeldung oder null, wenn der Eintrag gespeichert werden kann.</summary>
    public string? Validate() => FirstError;
}
