using PowerExec.Core.Localization;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Search;
using PowerExec.Core.Updates;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI;

/// <summary>
/// Zentrale Steuerung (App-Kern): Einstellungen, Panels, Aktionen, Updates. Ohne Oberflächen-Typen:
/// Fenster, Tray, Tastenkürzel und Design erreicht er über <see cref="IAppUi"/> (umgesetzt von der WPF-App, in Tests durch Attrappen).
/// </summary>
public partial class AppController : IDisposable
{
    /// <param name="createUi">Erzeugt die Oberfläche zu diesem Kern.</param>
    public AppController(Func<AppController, IAppUi> createUi)
    {
        Ui = createUi(this);
        // {{$Variablen}} gleich auflösbar – auch wenn Teile der Initialisierung übersprungen werden (Selbsttest)
        Placeholders.Resolver = name => VariableResolver.Resolve(name);
    }

    protected readonly List<AppPanel> _hosts = new();
    private ISettingsWindow? _settingsWindow;

    /// <summary>Die Oberfläche: Fenster, Panels, Tray, Tastenkürzel, Design.</summary>
    public IAppUi Ui { get; }

    public SettingsStore Store { get; protected set; } = new();
    public AppSettings Settings { get; protected set; } = new();
    public ItemRepository Repository { get; protected set; } = null!;
    public Launcher Launcher { get; protected set; } = null!;
    public UpdateService Updates { get; } = new();
    public HealthService Health { get; protected set; } = null!;
    /// <summary>Update-Hinweis für alle Panels und das Einstellungsfenster.</summary>
    public UpdateState Update { get; } = new();

    public IReadOnlyList<AppPanel> Hosts => _hosts;

    public void Start()
    {
        var firstStart = !Store.Exists;
        Settings = Store.Load();
        PowerExec.Core.Localization.L.Use(Settings.Language);
        if (firstStart)
        {
            // Der Installer legt den Autostart-Eintrag an; beim ersten Start übernehmen.
            Settings.StartWithWindows = AutostartService.IsEnabled();
            SaveSettings();
        }
        Ui.Initialize();
        Ui.ApplyTheme(Settings);
        Ui.SystemThemeChanged += OnSystemThemeChanged;
        SetupSnippetFolder();
        CustomEntry.TerminalAvailable = () => ProgramResolver.Resolve("wt.exe") != null;
        CustomEntry.RdpFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec", "Rdp");
        Placeholders.ClipboardText = () => Shell.GetClipboardText();
        // {{$Variablen}}: eingebaute und eigene; PowerShell-Variablen mit Zwischenspeicher im Hintergrund vorwärmen
        WarmVariables();

        Health = new HealthService(UiDispatcher);
        Health.Changed += RebuildPanels;
        Repository = new ItemRepository(Settings, UiDispatcher);
        Launcher = new Launcher(() => Settings, Dialogs, Ui.AskCredential);
        Repository.Changed += (_, _) =>
        {
            UpdateKnownFiles();
            MaintainPathsAsync().Forget();
            RebuildPanels();
        };

        Ui.Hotkeys.Pressed += id => ShowPanelFromHotkey(_hosts.FirstOrDefault(h => h.HotkeyId == id));
        var hotkeyError = CreateHosts();
        StartFullscreenWatch();
        StartScheduler();
        StartCatalogs();
        StartMachineSync();

        Ui.CreateTray();

        AppLog.Info(string.Format("Power-Exec {0} started ({1} folders)", UpdateService.CurrentVersionText, Settings.Folders.Count));
        Repository.LoadCache();
        Repository.SetupWatchers();
        Repository.ScanAsync().Forget();
        AutostartService.Apply(Settings.StartWithWindows);

        // Neu in dieser Version: nach einem Update einmal im Panel zeigen
        var current = UpdateService.CurrentVersion;
        var lastSeen = Core.Updates.ReleaseNotes.Parse(Settings.LastSeenVersion);
        if (firstStart || current.Major + current.Minor + current.Build == 0) Settings.LastSeenVersion = current.ToString(3);
        else if (lastSeen == null || lastSeen < current) WhatsNew = Core.Updates.ReleaseNotes.Since(lastSeen, current).FirstOrDefault();
        if (WhatsNew == null && Settings.LastSeenVersion != current.ToString(3)) { Settings.LastSeenVersion = current.ToString(3); SaveSettings(); }

        if (Settings.Folders.Count == 0 && !Settings.WelcomeDone)
        {
            ShowWelcome();
        }
        else if (Settings.Folders.Count == 0)
        {
            Ui.Notify(L.T("Power-Exec is running"), L.T("Choose in the settings which folders are scanned."));
            ShowSettings(section: "groups");
        }
        else if (hotkeyError != null)
        {
            Ui.Notify(L.T("Hotkey not available"), hotkeyError);
        }

        if (Settings.CheckUpdatesOnStart)
            CheckUpdatesAsync(manual: false, includePrereleases: Settings.IncludePrereleases).Forget();
    }

    public void ShowSettings(string? folderPath = null, string? section = null)
    {
        HidePanels(force: true);
        if (_settingsWindow != null)
        {
            if (folderPath != null) _settingsWindow.ViewModel.FocusGroup(folderPath);
            if (section != null) _settingsWindow.ViewModel.Section = section;
            _settingsWindow.BringToFront();
            return;
        }
        var vm = new SettingsViewModel(this, folderPath);
        if (section != null) vm.Section = section;
        var w = Ui.ShowSettings(vm);
        _settingsWindow = w;
        w.Closed += () => { if (_settingsWindow == w) _settingsWindow = null; };
    }

    public void Exit()
    {
        IsExiting = true;
        // Offene Fenster (Einstellungen, Dialoge) zuerst schließen, solange die Panels noch existieren
        _settingsWindow?.Close();
        Dispose();
        Ui.Shutdown();
    }

    public void Dispose()
    {
        Repository?.Dispose();
        DisposePanels();
        Ui.Dispose();
    }
}
