using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.Infrastructure;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec.UI.Core.Tests;

/// <summary>Oberfläche ohne Fenster: zeichnet auf, was der App-Kern öffnet, meldet und registriert.</summary>
internal sealed class FakeAppUi : IAppUi
{
    public FakeUiDispatcher FakeDispatcher { get; } = new();
    public FakeShell FakeShell { get; } = new();
    public FakeDialogs FakeDialogs { get; } = new();
    public FakeHotkeys FakeHotkeys { get; } = new();

    public IUiDispatcher Dispatcher => FakeDispatcher;
    public IShell Shell => FakeShell;
    public IDialogs Dialogs => FakeDialogs;
    public IThemeInfo Theme { get; } = new FakeTheme();
    public IIconProvider Icons { get; } = new FakeIcons();

    public List<FakePanelView> Panels { get; } = new();
    public List<(string Title, string Text)> Notifications { get; } = new();
    public List<(string Kind, FakeWindow Window)> Windows { get; } = new();

    public void Initialize() { }
    public void ApplyTheme(AppSettings settings) { }
    public string ThemeStateKey => "test";
    public event Action? SystemThemeChanged { add { } remove { } }

    public IPanelView CreatePanelView(AppPanel panel)
    {
        var v = new FakePanelView(panel);
        Panels.Add(v);
        return v;
    }

    public IHotkeys Hotkeys => FakeHotkeys;
    public void CreateTray() { }
    public void Notify(string title, string text, Action? onClick = null, bool warning = false) => Notifications.Add((title, text));

    private FakeWindow Open(string kind, object? vm = null)
    {
        var w = new FakeWindow(vm);
        Windows.Add((kind, w));
        return w;
    }

    public IAppWindow ShowRun(RunViewModel vm) => Open("Run", vm);
    public IAppWindow ShowProfiles(ProfilesViewModel vm) => Open("Profiles", vm);
    public void ShowProfilesModal(ProfilesViewModel vm, object? owner) => Open("ProfilesModal", vm);
    public IAppWindow ShowSchedule(ScheduleViewModel vm) => Open("Schedule", vm);
    public IAppWindow ShowWorkflow(WorkflowEditorViewModel vm) => Open("Workflow", vm);
    public IAppWindow ShowWelcome(WelcomeViewModel vm, Action<bool> finished) => Open("Welcome", vm);
    public ISettingsWindow ShowSettings(SettingsViewModel vm) => (ISettingsWindow)Open("Settings", vm);
    public IAppWindow ShowResults(ResultsViewModel vm) => Open("Results", vm);
    public IAppWindow ShowSpotlight(SpotlightViewModel vm, bool closeOnDeactivate) => Open("Spotlight", vm);

    public EntryEditorResult EditEntry(AppSettings target, CustomEntry? entry, bool isNew, string? group, IEnumerable<string>? tags, object? owner) =>
        new(null, null, null);

    public RunCredential? AskCredential(object? owner, string target, bool remote) => null;
    public Task<bool> PasteIntoPreviousAsync() => Task.FromResult(true);
    public IReadOnlyList<KeyValuePair<int, string>> Monitors { get; } = new[] { new KeyValuePair<int, string>(0, "Monitor 1") };
    public bool ShutDown { get; private set; }
    public void Shutdown() => ShutDown = true;
    public void Dispose() { }
}

internal sealed class FakePanelView(AppPanel panel) : IPanelView
{
    public AppPanel Panel { get; } = panel;
    public bool IsOpen { get; private set; }
    public bool OpenedViaHotkey { get; private set; }
    public object Owner => this;
    public bool Busy { get; private set; }
    public bool Disposed { get; private set; }
    public int HiddenAfterAction { get; private set; }

    public void ShowViaHotkey() { IsOpen = true; OpenedViaHotkey = true; }
    public void Hide() { IsOpen = false; OpenedViaHotkey = false; }
    public void HideAfterAction(bool force) { HiddenAfterAction++; if (force || !Panel.Config.Pinned) Hide(); }
    public void SetBusy(bool busy) => Busy = busy;
    public void ApplyFullscreen(FullscreenProbe? probe) { }
    public void Dispose() => Disposed = true;

    public PanelOpenState? OpenState => IsOpen ? new PanelOpenState(1, OpenedViaHotkey) : null;
    public PanelOpenState? Restored { get; private set; }

    public void RestoreOpenState(PanelOpenState state)
    {
        Restored = state;
        IsOpen = true;
        OpenedViaHotkey = state.ViaHotkey;
    }
}

internal sealed class FakeWindow(object? viewModel) : ISettingsWindow
{
    public object? Model { get; } = viewModel;
    public bool IsOpen { get; private set; } = true;
    public bool IsActive => IsOpen;
    public object Owner => this;
    public int BroughtToFront { get; private set; }
    public SettingsViewModel ViewModel => (SettingsViewModel)Model!;
    public void BringToFront() => BroughtToFront++;

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Closed?.Invoke();
    }

    public event Action? Closed;
}

internal sealed class FakeHotkeys : IHotkeys
{
    public Dictionary<int, string> Registered { get; } = new();

    /// <summary>Diese Kürzel sind „schon von einem anderen Programm belegt“.</summary>
    public HashSet<string> Taken { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Register(int id, string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return null;
        if (Taken.Contains(hotkey)) return "Tastenkürzel ist belegt";
        Registered[id] = hotkey;
        return null;
    }

    public void UnregisterAll() => Registered.Clear();
    public event Action<int>? Pressed { add { } remove { } }
    public void Dispose() { }
}

/// <summary>App-Kern mit Fake-Oberfläche, ohne Start() (keine echte Einstellungsdatei, kein Scan, keine Registry).</summary>
internal sealed class TestController : AppController
{
    public TestController(FakeAppUi ui, AppSettings settings, string dir) : base(_ => ui)
    {
        FakeUi = ui;
        Store = new SettingsStore(Path.Combine(dir, "settings.json"));
        HistoryPath = Path.Combine(dir, "history.json");
        Settings = settings;
        Health = new HealthService(ui.Dispatcher);
        Repository = new ItemRepository(settings, ui.Dispatcher);
        Launcher = new Launcher(() => Settings, ui.Dialogs, ui.AskCredential);
    }

    public FakeAppUi FakeUi { get; }
    public string? Recreate() => CreateHosts();
}

public sealed class AppControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PowerExecCoreTests", Guid.NewGuid().ToString("N"));

    public AppControllerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private TestController Create(params PanelConfig[] panels)
    {
        var settings = new AppSettings();
        settings.Panels.AddRange(panels.Length > 0 ? panels : new[] { new PanelConfig { Name = "Haupt", Hotkey = "Ctrl+Alt+Space" } });
        var app = new TestController(new FakeAppUi(), settings, _dir);
        app.Recreate();
        return app;
    }

    [Fact]
    public void CreatesOneViewAndHotkeyPerPanel()
    {
        var app = Create(new PanelConfig { Name = "A", Hotkey = "Ctrl+Alt+A" }, new PanelConfig { Name = "B", Hotkey = "Ctrl+Alt+B" });

        Assert.Equal(2, app.Hosts.Count);
        Assert.Equal(2, app.FakeUi.Panels.Count);
        Assert.All(app.Hosts, h => Assert.Same(h, ((FakePanelView)h.View).Panel));
        Assert.Equal(new[] { "Ctrl+Alt+A", "Ctrl+Alt+B" }, app.FakeUi.FakeHotkeys.Registered.Values.OrderBy(v => v));
    }

    [Fact]
    public void RecreatingPanelsDisposesOldViewsAndReportsTakenHotkeys()
    {
        var app = Create();
        var first = app.FakeUi.Panels[0];
        app.FakeUi.FakeHotkeys.Taken.Add("Ctrl+Alt+Space");

        var error = app.Recreate();

        Assert.True(first.Disposed);
        Assert.Single(app.Hosts);
        Assert.NotSame(first, app.Hosts[0].View);
        Assert.Contains("Haupt", error);
    }

    [Fact]
    public void OpenPanelStaysOpenAfterRecreate()
    {
        var app = Create(new PanelConfig { Name = "A", Hotkey = "Ctrl+Alt+A" }, new PanelConfig { Name = "B", Hotkey = "Ctrl+Alt+B" });
        app.ShowPanelFromHotkey(app.Hosts[1]);

        app.Recreate();

        var a = (FakePanelView)app.Hosts[0].View;
        var b = (FakePanelView)app.Hosts[1].View;
        Assert.False(a.IsOpen);
        Assert.Null(a.Restored);
        Assert.True(b.IsOpen);
        Assert.Equal(new PanelOpenState(1, true), b.Restored);

        // Alte Fenster schließen erst etwas später (die neuen liegen schon darüber) – kein Aufblitzen
        var oldViews = app.FakeUi.Panels.Take(2).ToList();
        Assert.All(oldViews, v => Assert.False(v.Disposed));
        var timer = app.FakeUi.FakeDispatcher.Timers.Last(t => t.IsRunning);
        timer.Tick();
        Assert.All(oldViews, v => Assert.True(v.Disposed));
    }

    [Fact]
    public void DroppedFolderAsGroupOpensNewGroupDialogWithChosenOptions()
    {
        var app = Create();
        var folder = Path.Combine(_dir, "Werkzeuge");
        Directory.CreateDirectory(folder);
        app.FakeUi.FakeDialogs.ChoiceAnswer = (0, false); // als Gruppe
        app.FakeUi.FakeDialogs.NewGroupAnswer = new NewGroupSpec("Werkzeuge", IconRef.None, folder, "#3B82F6", ItemLayout.Grid);

        app.AddDropped(new[] { new DroppedTarget(CustomEntryKind.Folder, folder, "Werkzeuge") }, group: null);

        Assert.Contains("AskNewGroup Ordner als Gruppe hinzufügen", app.FakeUi.FakeDialogs.Asked);
        Assert.Contains(app.Settings.Folders, f => f.Path == folder);
        Assert.Equal(ItemLayout.Grid, GroupManager.LayoutOf(app.Settings, "Werkzeuge"));
    }

    [Fact]
    public void DroppedFolderDialogCancelledAddsNothing()
    {
        var app = Create();
        var folder = Path.Combine(_dir, "Abbruch");
        Directory.CreateDirectory(folder);
        app.FakeUi.FakeDialogs.ChoiceAnswer = (0, false);
        app.FakeUi.FakeDialogs.NewGroupAnswer = null;

        app.AddDropped(new[] { new DroppedTarget(CustomEntryKind.Folder, folder, "Abbruch") }, group: null);

        Assert.DoesNotContain(app.Settings.Folders, f => f.Path == folder);
    }

    [Fact]
    public void HotkeyOpensPanelAndClosesItAgain()
    {
        var app = Create();
        var view = app.FakeUi.Panels[0];

        app.ShowPanelFromHotkey(app.Hosts[0]);
        Assert.True(view.IsOpen);

        app.ShowPanelFromHotkey(app.Hosts[0]);
        Assert.False(view.IsOpen);
    }

    [Fact]
    public void HotkeyTogglesSpotlightInsteadOfPanel()
    {
        var app = Create(new PanelConfig { Name = "Suche", Hotkey = "Ctrl+Alt+S", HotkeyOpensSpotlight = true });

        app.ShowPanelFromHotkey(app.Hosts[0]);
        var (kind, spotlight) = Assert.Single(app.FakeUi.Windows);
        Assert.Equal("Spotlight", kind);
        Assert.IsType<SpotlightViewModel>(spotlight.Model);
        Assert.False(app.FakeUi.Panels[0].IsOpen);

        app.ShowPanelFromHotkey(app.Hosts[0]);
        Assert.False(spotlight.IsOpen);

        app.ShowPanelFromHotkey(app.Hosts[0]);
        Assert.Equal(2, app.FakeUi.Windows.Count);
    }

    [Fact]
    public void SettingsWindowIsReusedWhileOpen()
    {
        var app = Create();

        app.ShowSettings();
        app.ShowSettings(section: "groups");
        var (_, window) = Assert.Single(app.FakeUi.Windows);
        Assert.Equal(1, window.BroughtToFront);
        Assert.Equal("groups", window.ViewModel.Section);

        window.Close();
        app.ShowSettings();
        Assert.Equal(2, app.FakeUi.Windows.Count);
    }

    [Fact]
    public void ActionsHidePanelsUnlessPinned()
    {
        var app = Create(new PanelConfig { Name = "Frei" }, new PanelConfig { Name = "Angeheftet", Pinned = true });
        foreach (var v in app.FakeUi.Panels) v.ShowViaHotkey();

        app.HidePanels();
        Assert.False(app.FakeUi.Panels[0].IsOpen);
        Assert.True(app.FakeUi.Panels[1].IsOpen);

        app.HidePanels(force: true);
        Assert.False(app.FakeUi.Panels[1].IsOpen);
    }

    [Fact]
    public void CopySnippetUsesShellAndNotifies()
    {
        var app = Create();
        var entry = new CustomEntry { Name = "Gruß", Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text, Code = "Hallo Welt" };
        app.Settings.CustomEntries.Add(entry);

        app.CopySnippet(entry.ToLaunchItem(), record: false);

        Assert.Equal(new[] { "Hallo Welt" }, app.FakeUi.FakeShell.Clipboard);
        Assert.Contains(app.FakeUi.Notifications, n => n.Title == "In die Zwischenablage kopiert");
    }

    [Fact]
    public void SaveErrorIsReportedThroughDialogs()
    {
        var app = Create();
        // Ordner statt Datei: Speichern muss scheitern und als Warnung erscheinen, nicht als Ausnahme
        var blocked = Path.Combine(_dir, "blockiert");
        Directory.CreateDirectory(Path.Combine(blocked, "settings.json"));
        typeof(AppController).GetProperty(nameof(AppController.Store))!.SetValue(app, new SettingsStore(Path.Combine(blocked, "settings.json")));

        app.SaveSettings();

        Assert.Contains(app.FakeUi.FakeDialogs.Messages, m => m.Kind == "warn" && m.Message.Contains("nicht gespeichert"));
    }

    [Fact]
    public void ExitDisposesPanelsAndShutsDownTheUi()
    {
        var app = Create();
        var view = app.FakeUi.Panels[0];

        app.Exit();

        Assert.True(app.IsExiting);
        Assert.True(view.Disposed);
        Assert.Empty(app.Hosts);
        Assert.True(app.FakeUi.ShutDown);
    }
}
