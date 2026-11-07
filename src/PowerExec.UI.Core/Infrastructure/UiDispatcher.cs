namespace PowerExec.Infrastructure;

/// <summary>
/// UI-Thread der Oberfläche ohne Framework-Bezug (WPF: <c>Dispatcher</c>/<c>DispatcherTimer</c>; in Tests eine Attrappe).
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Aktion später auf dem UI-Thread ausführen (aus beliebigem Thread aufrufbar).</summary>
    void Post(Action action);

    /// <summary>Zeitgeber auf dem UI-Thread mit niedriger Priorität; startet angehalten.</summary>
    IUiTimer CreateTimer(TimeSpan interval, Action tick);
}

public interface IUiTimer
{
    bool IsRunning { get; }
    void Start();
    void Stop();
}
