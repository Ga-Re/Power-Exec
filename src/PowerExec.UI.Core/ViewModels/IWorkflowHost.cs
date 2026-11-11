using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.UI.ViewModels;

/// <summary>Was der Ablauf-Editor (<see cref="WorkflowEditorViewModel"/>) von der App braucht.</summary>
public interface IWorkflowHost
{
    AppSettings Settings { get; }

    /// <summary>Eintrag zu einem Pfad (gefundene Datei oder eigener Eintrag); null = nicht vorhanden.</summary>
    LaunchItem? FindItem(string path);

    /// <summary>Schritte nacheinander starten; true = alle erfolgreich.</summary>
    Task<bool> RunWorkflowAsync(string name, IReadOnlyList<WorkflowStep> steps, bool stopOnError);

    void OnWorkflowSaved(CustomEntry original, CustomEntry edited);
}
