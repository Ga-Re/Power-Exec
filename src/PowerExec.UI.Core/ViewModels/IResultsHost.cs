using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

/// <summary>Was die Ergebnisliste eines Remote-Laufs (<see cref="ResultsViewModel"/>) von der App braucht.</summary>
public interface IResultsHost
{
    AppSettings Settings { get; }
    IShell Shell { get; }
    IDialogs Dialogs { get; }
    IUiDispatcher UiDispatcher { get; }

    /// <summary>Neuen Remote-Lauf (z. B. nur mit den fehlgeschlagenen Rechnern) mit eigener Ergebnisliste starten.</summary>
    bool RetryRemote(ExecutionRequest request);
}
