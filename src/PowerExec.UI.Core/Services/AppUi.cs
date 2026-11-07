using System.Drawing;
using System.Runtime.InteropServices;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Infrastructure;
using PowerExec.ViewModels;

namespace PowerExec.Services;

/// <summary>
/// Die Oberfläche aus Sicht des App-Kerns (<c>AppController</c>): Fenster, Panels, Tray, Tastenkürzel und Design.
/// Von der WPF-App umgesetzt (in Tests durch Attrappen); der Kern selbst kennt keine Oberflächen-Typen.
/// </summary>
public interface IAppUi : IDisposable
{
    IUiDispatcher Dispatcher { get; }
    IShell Shell { get; }
    IDialogs Dialogs { get; }
    IThemeInfo Theme { get; }
    IIconProvider Icons { get; }

    /// <summary>Einmal beim Start, nachdem die Einstellungen geladen sind (Design, Verknüpfungen der Oberfläche).</summary>
    void Initialize();

    // ── Design ───────────────────────────────────────────────────────

    /// <summary>Design, Akzentfarbe, Fensterhintergrund und Größe aus den Einstellungen anwenden.</summary>
    void ApplyTheme(AppSettings settings);

    /// <summary>Kennung des angewandten Designs; ändert sie sich, müssen die Panels neu erzeugt werden.</summary>
    string ThemeStateKey { get; }

    /// <summary>Windows-Design, Akzentfarbe oder Kontrastdesign geändert.</summary>
    event Action? SystemThemeChanged;

    // ── Panels, Tastenkürzel, Infobereich ────────────────────────────

    /// <summary>Panelfenster samt Hover-Zonen bzw. Mini-Docks für ein Panel erzeugen.</summary>
    IPanelView CreatePanelView(AppPanel panel);

    /// <summary>Globale Tastenkürzel.</summary>
    IHotkeys Hotkeys { get; }

    /// <summary>Symbol im Infobereich anlegen (nach dem Erzeugen der Panels).</summary>
    void CreateTray();

    /// <summary>Hinweis am Symbol im Infobereich; <paramref name="onClick"/> beim Klick darauf.</summary>
    void Notify(string title, string text, Action? onClick = null, bool warning = false);

    // ── Fenster ──────────────────────────────────────────────────────

    IAppWindow ShowRun(RunViewModel vm);
    IAppWindow ShowProfiles(ProfilesViewModel vm);

    /// <summary>Profil-Dialog modal über <paramref name="owner"/> (aus den Einstellungen).</summary>
    void ShowProfilesModal(ProfilesViewModel vm, object? owner);
    IAppWindow ShowSchedule(ScheduleViewModel vm);
    IAppWindow ShowWorkflow(WorkflowEditorViewModel vm);

    /// <summary>Einführung; <paramref name="finished"/> mit true, wenn danach das Panel gezeigt werden soll.</summary>
    IAppWindow ShowWelcome(WelcomeViewModel vm, Action<bool> finished);

    ISettingsWindow ShowSettings(SettingsViewModel vm);

    /// <summary>Ergebnisliste einer Remote-Ausführung; <see cref="IAppWindow.BringToFront"/> öffnet sie wieder, falls geschlossen.</summary>
    IAppWindow ShowResults(ResultsViewModel vm);

    /// <summary>Schwebende Suche in der Bildschirmmitte (Monitor unter dem Mauszeiger).</summary>
    IAppWindow ShowSpotlight(SpotlightViewModel vm, bool closeOnDeactivate);

    /// <summary>Eintragsdialog (modal): eigenen Eintrag anlegen oder bearbeiten.</summary>
    EntryEditorResult EditEntry(AppSettings target, CustomEntry? entry, bool isNew, string? group, IEnumerable<string>? tags, object? owner);

    /// <summary>Anmeldedaten für „als anderer Benutzer“ abfragen; null = abgebrochen.</summary>
    RunCredential? AskCredential(object? owner, string target, bool remote);

    /// <summary>Zwischenablage ins zuvor aktive Fenster einfügen (Strg+V); false = kein Zielfenster.</summary>
    Task<bool> PasteIntoPreviousAsync();

    /// <summary>Angeschlossene Monitore (Nummer, Anzeigename) für die Panel-Einstellungen.</summary>
    IReadOnlyList<KeyValuePair<int, string>> Monitors { get; }

    bool IsMicaSupported { get; }

    /// <summary>Anwendung beenden (nachdem der Kern aufgeräumt hat).</summary>
    void Shutdown();
}

/// <summary>Panelfenster mit seinen Hover-Zonen bzw. Mini-Docks.</summary>
public interface IPanelView : IDisposable
{
    bool IsOpen { get; }

    /// <summary>Per Tastenkürzel/Tray geöffnet (dann schließt dasselbe Kürzel es wieder).</summary>
    bool OpenedViaHotkey { get; }

    /// <summary>Fenster, über dem Rückfragen erscheinen sollen (solange das Panel offen ist).</summary>
    object Owner { get; }

    /// <summary>Per Tastenkürzel öffnen: Monitor unter dem Mauszeiger bzw. erster Monitor des Panels.</summary>
    void ShowViaHotkey();

    void Hide();

    /// <summary>Nach einer Aktion einfahren (angeheftete nur mit <paramref name="force"/>).</summary>
    void HideAfterAction(bool force);

    /// <summary>Hover-Zonen pulsieren lassen, solange Aktivitäten laufen.</summary>
    void SetBusy(bool busy);

    /// <summary>Hover-Zonen und Docks bei Vollbild-Programmen ausblenden; null = Erkennung ausgeschaltet.</summary>
    void ApplyFullscreen(FullscreenProbe? probe);
}

/// <summary>Ein Fenster der Oberfläche aus Sicht des Kerns.</summary>
public interface IAppWindow
{
    /// <summary>Noch nicht geschlossen.</summary>
    bool IsOpen { get; }

    bool IsActive { get; }

    /// <summary>Fenster für Rückfragen darüber.</summary>
    object Owner { get; }

    /// <summary>Wiederherstellen (falls minimiert) und nach vorn holen.</summary>
    void BringToFront();

    void Close();

    event Action? Closed;
}

/// <summary>Einstellungsfenster: sein aktuelles ViewModel (wird beim Verwerfen ersetzt).</summary>
public interface ISettingsWindow : IAppWindow
{
    SettingsViewModel ViewModel { get; }
}

/// <summary>Globale Tastenkürzel (z. B. „Ctrl+Alt+Space“).</summary>
public interface IHotkeys : IDisposable
{
    /// <returns>Fehlermeldung oder null.</returns>
    string? Register(int id, string hotkey);

    void UnregisterAll();

    event Action<int>? Pressed;
}

/// <summary>Ein laufendes Panel im Kern: Einstellungen, ViewModel, Tastenkürzel-Kennung und die Oberfläche dazu.</summary>
public sealed class AppPanel : IPanelContext
{
    public AppPanel(PanelConfig config, int hotkeyId)
    {
        Config = config;
        HotkeyId = hotkeyId;
    }

    public PanelConfig Config { get; }
    public int HotkeyId { get; }
    public PanelViewModel ViewModel { get; internal set; } = null!;
    public IPanelView View { get; internal set; } = null!;
}

/// <summary>Vordergrundfenster für die Vollbild-Erkennung (eigene Fenster, Desktop und Shell ausgenommen).</summary>
public sealed record FullscreenProbe(Rectangle? Foreground, string ClassName)
{
    /// <summary>Bedeckt das Vordergrundfenster diesen Monitor vollständig?</summary>
    public bool Covers(Rectangle monitorBounds) =>
        Foreground is { } r && Fullscreen.IsFullscreen(r, monitorBounds, ClassName);

    /// <summary>Aktuelles Vordergrundfenster (ohne Windows: keines).</summary>
    public static FullscreenProbe Current()
    {
        if (!OperatingSystem.IsWindows()) return new FullscreenProbe(null, string.Empty);
        var hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd) &&
            GetWindowThreadProcessId(hwnd, out var pid) != 0 && pid != Environment.ProcessId &&
            GetWindowRect(hwnd, out var r))
        {
            var cls = new char[256];
            var len = GetClassName(hwnd, cls, cls.Length);
            return new FullscreenProbe(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), new string(cls, 0, Math.Max(0, len)));
        }
        return new FullscreenProbe(null, string.Empty);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, char[] className, int maxCount);
}
