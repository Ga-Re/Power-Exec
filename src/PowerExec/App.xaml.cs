using PowerExec.Core.Localization;
using System.Windows;
using System.Windows.Threading;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;

namespace PowerExec.Desktop;

public partial class App : Application
{
    private const string MutexName = "PowerExec.SingleInstance.{6C1D3E4B-8B0F-4C1A-9E2D-3A5B7C9D1E2F}";
    private const string ShowEventName = "PowerExec.ShowPanel.{6C1D3E4B-8B0F-4C1A-9E2D-3A5B7C9D1E2F}";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        CommandRequery.Backend = new Infrastructure.WpfCommandRequery();
#if SELFTEST
        if (e.Args.Length > 0 && e.Args[0] == "--selftest")
        {
            base.OnStartup(e);
            RunSelfTest(e.Args.Length > 1 ? e.Args[1] : Path.Combine(Path.GetTempPath(), "PowerExec-selftest.txt"));
            return;
        }
#endif
        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            // Zweiter Start: laufende Instanz bitten, das Panel zu zeigen.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* laufende Instanz antwortet nicht mehr – der zweite Start endet ohnehin */ }
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        _controller = new AppController(app => new Services.WpfAppUi(app));
        _controller.Start();

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _controller?.ShowPanelFromHotkey());
        })
        { IsBackground = true, Name = "PowerExec.ShowEvent" };
        thread.Start();
    }

#if SELFTEST
    /// <summary>
    /// Oberflächen-Selbsttest für die CI; Ergebnis in eine Datei, Exitcode 0 = in Ordnung. Nur in Builds mit
    /// <c>-p:SelfTest=true</c> (CI) bzw. Debug enthalten – die ausgelieferte EXE trägt den Testcode nicht.
    /// </summary>
    private void RunSelfTest(string resultFile)
    {
        var selfTest = new SelfTestController();
        _controller = selfTest;
        Dispatcher.BeginInvoke(async () =>
        {
            string? error;
            try { error = await selfTest.RunSelfTestAsync(resultFile + ".progress"); }
            catch (Exception ex) { error = ex.ToString(); }
            try { File.WriteAllText(resultFile, error ?? "OK"); } catch { /* ohne Ergebnisdatei wertet die CI den Lauf als fehlgeschlagen */ }
            Shutdown(error == null ? 0 : 1);
        });
    }
#endif

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"{DateTime.Now:u} {e.Exception}{Environment.NewLine}");
            AppLog.Error("Unexpected error", e.Exception);
        }
        catch { /* Protokoll nicht schreibbar – die Meldung unten zeigt den Fehler trotzdem */ }
        e.Handled = true;
        // Beim Beenden (z. B. für ein Update) nur protokollieren – keine Meldung, die das Update aufhält
        if (_controller?.IsExiting == true) return;
        // Ort des Fehlers (erste Stelle im Power-Exec-Code) und Version mit anzeigen, damit sich der Fehler zuordnen lässt
        var frame = new System.Diagnostics.StackTrace(e.Exception, false).GetFrames()
            .Select(f => f.GetMethod())
            .FirstOrDefault(m => m?.DeclaringType?.Namespace?.StartsWith("PowerExec", StringComparison.Ordinal) == true);
        var where = frame != null ? $"{frame.DeclaringType!.Name}.{frame.Name}" : e.Exception.TargetSite?.Name ?? "unbekannt";
        var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec", "error.log");
        var text = L.F("An unexpected error occurred:\n\n{0}\n\nLocation: {1} · version {2}\n", e.Exception.Message, where, UpdateService.CurrentVersionText) +
                   L.F("Details: {0}", log);
        // Fluent-Dialog; scheitert er (z. B. Fehler beim Aufbau von Fenstern), bleibt die einfache Windows-Meldung
        try { Services.MessageDialog.Error(null, text, L.T("Unexpected error")); }
        catch { MessageBox.Show(text, "Power-Exec", MessageBoxButton.OK, MessageBoxImage.Error); }
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _showEvent?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { /* Mutex schon frei oder von einem anderen Thread gehalten – beim Beenden ohne Belang */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
