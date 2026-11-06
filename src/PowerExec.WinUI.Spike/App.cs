using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerExec.Infrastructure;

namespace PowerExec.WinUI.Spike;

/// <summary>Prototyp-App: Panel, Hover-Zone und Tray; hält sich selbst am Leben, bis „Beenden“ gewählt wird.</summary>
public sealed class App : Application
{
    private readonly SpikeOptions _options;
    private SpikeHost? _host;
    private PanelWindow? _panel;
    private HoverZoneWindow? _zone;
    private TrayIcon? _tray;
    private Hotkey? _hotkey;

    public static int ExitCode { get; private set; }

    public App(SpikeOptions options)
    {
        _options = options;
        UnhandledException += (_, e) =>
        {
            SpikeLog.Write("Unbehandelter Fehler: " + e.Exception);
            ExitCode = 2;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Ohne XAML-Datei: Standard-Stile der Steuerelemente selbst einbinden
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        // Panel und Zone werden versteckt statt geschlossen – die App läuft, bis sie ausdrücklich beendet wird
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        CommandRequery.Backend = new ManualCommandRequery();

        _host = new SpikeHost(this);
        _panel = new PanelWindow(_host);
        _zone = new HoverZoneWindow(_panel);
        _tray = new TrayIcon(this, _panel, _zone);
        _hotkey = new Hotkey(_panel, () => _panel.Toggle());

        if (_options.SmokeFile is { } file)
        {
            _ = SmokeTest.RunAsync(this, _host, _panel, _zone, _tray, _hotkey, file);
            return;
        }
        _zone.ShowZone();
        if (!_options.Background) _panel.SlideIn();
    }

    /// <summary>Alles aufräumen und beenden.</summary>
    public void Quit(int exitCode = 0)
    {
        ExitCode = exitCode;
        _hotkey?.Dispose();
        _tray?.Dispose();
        _zone?.Close();
        _panel?.CloseForGood();
        Exit();
    }
}

/// <summary>Einfaches Protokoll neben der EXE (für Rückmeldungen aus dem Prototyp).</summary>
public static class SpikeLog
{
    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "spike.log");

    public static void Write(string message)
    {
        try { File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); } catch { }
    }
}
