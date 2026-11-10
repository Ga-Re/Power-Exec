using PowerExec.Core.Localization;
using System.Windows;
using System.Windows.Threading;

namespace PowerExec;

public partial class App : Application
{
    private const string MutexName = "PowerExec.SingleInstance.{6C1D3E4B-8B0F-4C1A-9E2D-3A5B7C9D1E2F}";
    private const string ShowEventName = "PowerExec.ShowPanel.{6C1D3E4B-8B0F-4C1A-9E2D-3A5B7C9D1E2F}";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        Infrastructure.CommandRequery.Backend = new Infrastructure.WpfCommandRequery();
        if (e.Args.Length > 0 && e.Args[0] == "--selftest")
        {
            base.OnStartup(e);
            RunSelfTest(e.Args.Length > 1 ? e.Args[1] : Path.Combine(Path.GetTempPath(), "PowerExec-selftest.txt"));
            return;
        }
        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            // Zweiter Start: laufende Instanz bitten, das Panel zu zeigen.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
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
        }) { IsBackground = true, Name = "PowerExec.ShowEvent" };
        thread.Start();
    }

    /// <summary>Oberflächen-Selbsttest für die CI; Ergebnis in eine Datei, Exitcode 0 = in Ordnung.</summary>
    private void RunSelfTest(string resultFile)
    {
        var selfTest = new SelfTestController();
        _controller = selfTest;
        Dispatcher.BeginInvoke(async () =>
        {
            string? error;
            try { error = await selfTest.RunSelfTestAsync(resultFile + ".progress"); }
            catch (Exception ex) { error = ex.ToString(); }
            try { File.WriteAllText(resultFile, error ?? "OK"); } catch { }
            Shutdown(error == null ? 0 : 1);
        });
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"{DateTime.Now:u} {e.Exception}{Environment.NewLine}");
            Services.AppLog.Error(L.T("Unerwarteter Fehler"), e.Exception);
        }
        catch { }
        e.Handled = true;
        // Beim Beenden (z. B. für ein Update) nur protokollieren – keine Meldung, die das Update aufhält
        if (_controller?.IsExiting == true) return;
        // Ort des Fehlers (erste Stelle im Power-Exec-Code) und Version mit anzeigen, damit sich der Fehler zuordnen lässt
        var frame = new System.Diagnostics.StackTrace(e.Exception, false).GetFrames()
            .Select(f => f.GetMethod())
            .FirstOrDefault(m => m?.DeclaringType?.Namespace?.StartsWith("PowerExec") == true);
        var where = frame != null ? $"{frame.DeclaringType!.Name}.{frame.Name}" : e.Exception.TargetSite?.Name ?? "unbekannt";
        var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec", "error.log");
        var text = L.F("Ein unerwarteter Fehler ist aufgetreten:\n\n{0}\n\nOrt: {1} · Version {2}\n", e.Exception.Message, where, Services.UpdateService.CurrentVersionText) +
                   L.F("Details: {0}", log);
        // Fluent-Dialog; scheitert er (z. B. Fehler beim Aufbau von Fenstern), bleibt die einfache Windows-Meldung
        try { Services.MessageDialog.Error(null, text, L.T("Unerwarteter Fehler")); }
        catch { MessageBox.Show(text, "Power-Exec", MessageBoxButton.OK, MessageBoxImage.Error); }
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _showEvent?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
