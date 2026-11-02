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

        _controller = new AppController();
        _controller.Start();

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _controller?.ShowPanelFromHotkey());
        }) { IsBackground = true, Name = "PowerExec.ShowEvent" };
        thread.Start();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"{DateTime.Now:u} {e.Exception}{Environment.NewLine}");
            Services.AppLog.Error("Unerwarteter Fehler", e.Exception);
        }
        catch { }
        MessageBox.Show("Ein unerwarteter Fehler ist aufgetreten:\n\n" + e.Exception.Message, "Power-Exec",
            MessageBoxButton.OK, MessageBoxImage.Error);
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
