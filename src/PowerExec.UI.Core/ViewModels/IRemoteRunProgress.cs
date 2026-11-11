using System.ComponentModel;

namespace PowerExec.UI.ViewModels;

/// <summary>
/// Fortschritt eines Remote-/Massenlaufs, wie ihn die Aktivitäten-Leiste anzeigt
/// (erfüllt von der Ergebnisliste). Änderungen an <see cref="DoneCount"/> bzw. <see cref="Progress"/>
/// werden über <see cref="INotifyPropertyChanged"/> gemeldet.
/// </summary>
public interface IRemoteRunProgress : INotifyPropertyChanged
{
    string Title { get; }
    bool IsRunning { get; }
    int Total { get; }
    int DoneCount { get; }
    int SuccessCount { get; }
    int ErrorCount { get; }
    int UnreachableCount { get; }
    /// <summary>0…1.</summary>
    double Progress { get; }
    /// <summary>Fehler des Runners selbst (nicht einzelner Rechner); null = keiner.</summary>
    string? RunnerError { get; }

    /// <summary>Lauf abbrechen.</summary>
    void Cancel();

    /// <summary>Alle Rechner fertig oder abgebrochen.</summary>
    event Action? Finished;
}
