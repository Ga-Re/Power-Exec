using System.Windows;
using PowerExec.Core.Config;
using PowerExec.Infrastructure;
using PowerExec.ViewModels;
using PowerExec.Views;

namespace PowerExec.Services;

/// <summary><see cref="IAppUi"/> für WPF: Fenster, Panels, Tray (WinForms-NotifyIcon), Tastenkürzel und Design.</summary>
public sealed class WpfAppUi : IAppUi
{
    private readonly AppController _app;
    private TrayIcon? _tray;
    private HotkeyService? _hotkeys;
    private IconService? _icons;
    private IIconProvider? _iconProvider;

    public WpfAppUi(AppController app)
    {
        _app = app;
        Dispatcher = new WpfUiDispatcher(Application.Current.Dispatcher);
        Dialogs = new WpfDialogs(() => app.AskForTest);
        Shell = new WpfShell(Dialogs);
    }

    public IUiDispatcher Dispatcher { get; }
    public IShell Shell { get; }
    public IDialogs Dialogs { get; }
    public IThemeInfo Theme { get; } = new WpfThemeInfo();

    /// <summary>Symbole als WPF-Bilder (für die WPF-Fenster); <see cref="Icons"/> reicht Griffe darauf an die ViewModels.</summary>
    public IconService IconService => _icons ??= new IconService(Application.Current.Dispatcher);

    public IIconProvider Icons => _iconProvider ??= new WpfIconProvider(IconService);

    public void Initialize()
    {
        WindowHelper.Settings = () => _app.Settings;
        WindowHelper.PanelsBehind = behind => { foreach (var h in PanelHosts) h.Window.SetBehindAppWindows(behind); };
        Controls.MachineBox.GroupSource = () => _app.Settings.MachineGroups;
        TagBrushConverter.Lookup = t => Core.Config.Tags.ColorOf(_app.Settings, t);
        ThemeService.SystemChanged += () => SystemThemeChanged?.Invoke();
    }

    /// <summary>Die WPF-Seite der laufenden Panels.</summary>
    public IEnumerable<PanelHost> PanelHosts => _app.Hosts.Select(p => p.View).OfType<PanelHost>();

    // ── Design ───────────────────────────────────────────────────────

    public void ApplyTheme(AppSettings settings) => ThemeService.Apply(settings);
    public string ThemeStateKey => ThemeService.StateKey;
    public event Action? SystemThemeChanged;

    // ── Panels, Tastenkürzel, Infobereich ────────────────────────────

    public IPanelView CreatePanelView(AppPanel panel)
    {
        var host = new PanelHost(_app, panel);
        host.CreateTriggers(_app);
        return host;
    }

    public IHotkeys Hotkeys => _hotkeys ??= new HotkeyService();

    public void CreateTray() => _tray ??= new TrayIcon(_app);

    public void Notify(string title, string text, Action? onClick = null, bool warning = false) =>
        _tray?.ShowBalloon(title, text, onClick, warning);

    // ── Fenster ──────────────────────────────────────────────────────

    public IAppWindow ShowRun(RunViewModel vm) => Show(new RunWindow(vm));
    public IAppWindow ShowProfiles(ProfilesViewModel vm) => Show(new ProfilesWindow(vm, _app));

    public void ShowProfilesModal(ProfilesViewModel vm, object? owner)
    {
        var w = new ProfilesWindow(vm, _app);
        if (owner is Window o) w.Owner = o;
        w.ShowDialog();
    }

    public IAppWindow ShowSchedule(ScheduleViewModel vm) => Show(new ScheduleWindow(vm));
    public IAppWindow ShowWorkflow(WorkflowEditorViewModel vm) => Show(new WorkflowWindow(vm));
    public IAppWindow ShowWelcome(WelcomeViewModel vm, Action<bool> finished) => Show(new WelcomeWindow(vm, finished));

    public ISettingsWindow ShowSettings(SettingsViewModel vm)
    {
        var w = new SettingsWindow(vm, _app);
        var handle = new SettingsHandle(w);
        w.Show();
        w.Activate();
        return handle;
    }

    public IAppWindow ShowResults(ResultsViewModel vm)
    {
        var handle = new ResultsHandle(vm);
        handle.BringToFront();
        return handle;
    }

    public IAppWindow ShowSpotlight(SpotlightViewModel vm, bool closeOnDeactivate)
    {
        var w = new SpotlightWindow(vm, ScreenService.MonitorAtCursor()) { CloseOnDeactivate = closeOnDeactivate };
        var handle = new WindowHandle(w);
        w.Show();
        return handle;
    }

    public EntryEditorResult EditEntry(AppSettings target, CustomEntry? entry, bool isNew, string? group, IEnumerable<string>? tags, object? owner) =>
        ProfilesWindow.EditEntryEx(_app, target, entry, isNew, saveImmediately: false, group, tags, owner as Window);

    public RunCredential? AskCredential(object? owner, string target, bool remote) => CredentialWindow.Prompt(owner as Window, target, remote);

    public Task<bool> PasteIntoPreviousAsync() => ForegroundTracker.PasteIntoPreviousAsync();

    public IReadOnlyList<KeyValuePair<int, string>> Monitors =>
        ScreenService.GetMonitors().Select(m => new KeyValuePair<int, string>(m.Index, m.DisplayName)).ToList();


    public void Shutdown() => Application.Current.Shutdown();

    public void Dispose()
    {
        _hotkeys?.Dispose();
        _hotkeys = null;
        _tray?.Dispose();
        _tray = null;
        _icons?.Dispose();
    }

    private static WindowHandle Show(Window w)
    {
        var handle = new WindowHandle(w);
        w.Show();
        w.Activate();
        return handle;
    }

    /// <summary>Ein WPF-Fenster als <see cref="IAppWindow"/>.</summary>
    internal class WindowHandle : IAppWindow
    {
        private bool _closed;

        public WindowHandle(Window w)
        {
            Window = w;
            w.Closed += (_, _) => { _closed = true; Closed?.Invoke(); };
        }

        public Window Window { get; }
        public bool IsOpen => !_closed;
        public bool IsActive => !_closed && Window.IsActive;
        public object Owner => Window;

        public void BringToFront()
        {
            if (_closed) return;
            if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
            Window.Activate();
        }

        public void Close() { if (!_closed) Window.Close(); }

        public event Action? Closed;
    }

    private sealed class SettingsHandle(SettingsWindow w) : WindowHandle(w), ISettingsWindow
    {
        public SettingsViewModel ViewModel => w.CurrentViewModel;
    }

    /// <summary>Ergebnisliste: geschlossen und erneut angefordert (Aktivitäten-Leiste) → neues Fenster mit demselben ViewModel.</summary>
    private sealed class ResultsHandle(ResultsViewModel vm) : IAppWindow
    {
        private ResultsWindow? _window;

        public bool IsOpen => _window is { IsLoaded: true };
        public bool IsActive => _window is { IsLoaded: true, IsActive: true };
        public object Owner => _window!;

        public void BringToFront()
        {
            if (_window is not { IsLoaded: true })
            {
                var first = _window == null;
                _window = new ResultsWindow(vm);
                _window.Closed += (_, _) => Closed?.Invoke();
                _window.Show();
                if (first) { _window.Activate(); return; }
            }
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        public void Close() => _window?.Close();

        public event Action? Closed;
    }
}
