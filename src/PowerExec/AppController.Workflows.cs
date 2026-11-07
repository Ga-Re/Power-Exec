using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec;

/// <summary>
/// Abläufe (C3): mehrere Einträge nacheinander starten. Jeder Schritt wartet, bis der vorige beendet ist
/// (lokale Scripte bis zum Prozessende, Remote bis alle Rechner fertig sind); bei „bei Fehler abbrechen“
/// enden die restlichen Schritte nach einem Fehler.
/// </summary>
public sealed partial class AppController
{
    /// <summary>Höchstens so lange auf das Ende eines Schritts warten (z. B. Dialog offen gelassen).</summary>
    public static readonly TimeSpan StepTimeout = TimeSpan.FromHours(2);

    /// <summary>Neuen Ablauf aus Einträgen anlegen (Mehrfachauswahl › „Als Ablauf speichern“).</summary>
    public CustomEntry CreateWorkflow(string name, IEnumerable<LaunchItem> items, string? group = null)
    {
        var entry = new CustomEntry
        {
            Kind = CustomEntryKind.Workflow,
            Name = name.Trim(),
            Icon = "fluent:flow",
            Steps = items.Select(i => new WorkflowStep
            {
                Path = i.FullPath,
                Name = Settings.FindEntry(i.FullPath)?.DisplayName is { Length: > 0 } dn ? dn : i.DisplayName
            }).ToList()
        };
        RememberForUndo($"Ablauf „{entry.Name}“ angelegt");
        Settings.CustomEntries.Add(entry);
        if (group != null) GroupManager.Assign(Settings, entry.Key, group);
        CommitUserChange();
        ShowUndoToast($"Ablauf „{entry.Name}“ mit {entry.Steps.Count} Schritten angelegt");
        return entry;
    }

    /// <summary>Einträge nacheinander starten (gespeicherter Ablauf oder einmalig aus der Mehrfachauswahl).</summary>
    public async Task<bool> RunWorkflowAsync(string name, IReadOnlyList<WorkflowStep> steps, bool stopOnError, LaunchItem? workflowItem = null)
    {
        if (steps.Count == 0) return false;
        HidePanels();
        bool cancelled = false;
        var activity = Activities.Add(new ActivityViewModel(name, $"Schritt 1 / {steps.Count}", "⇢", cancel: () => cancelled = true));
        activity.Progress = 0;
        HistoryEntry? summary = workflowItem != null
            ? BeginHistory(workflowItem, null, ExecutionMode.CurrentUser, PsEngine.WindowsPowerShell, null, null)
            : null;
        int done = 0, failed = 0;
        try
        {
            for (int i = 0; i < steps.Count && !cancelled; i++)
            {
                var step = steps[i];
                activity.Detail = $"Schritt {i + 1} / {steps.Count}: {(step.Name.Length > 0 ? step.Name : Path.GetFileName(step.Path))}";
                activity.Progress = (double)i / steps.Count;
                var result = await RunStepAsync(step, name, () => cancelled);
                if (result is HistoryResult.Success or HistoryResult.Started) done++;
                else
                {
                    failed++;
                    if (stopOnError || result == HistoryResult.Cancelled) break;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Ablauf {name} abgebrochen", ex);
            failed++;
        }
        var text = cancelled ? $"abgebrochen nach {done} von {steps.Count} Schritten"
            : failed > 0 ? $"{done} von {steps.Count} Schritten erfolgreich"
            : $"alle {steps.Count} Schritte erfolgreich";
        var state = failed > 0 || cancelled ? (done > 0 ? ActivityState.Warning : ActivityState.Error) : ActivityState.Success;
        activity.Complete(state, text);
        if (summary != null)
            CompleteHistory(summary, state switch { ActivityState.Success => HistoryResult.Success, ActivityState.Warning => HistoryResult.Warning, _ => HistoryResult.Error }, text);
        AppLog.Info($"Ablauf {name}: {text}");
        return failed == 0 && !cancelled;
    }

    /// <summary>Einen Schritt starten und auf sein Ende warten (über den Verlaufseintrag).</summary>
    private async Task<HistoryResult> RunStepAsync(WorkflowStep step, string workflowName, Func<bool> cancelled, string originPrefix = "Ablauf ")
    {
        var item = FindItem(step.Path);
        if (item == null)
        {
            AppLog.Info($"Ablauf {workflowName}: Schritt {step.Name} nicht gefunden ({step.Path})");
            return HistoryResult.Error;
        }
        if (item.Source?.Kind == CustomEntryKind.Workflow) return HistoryResult.Error; // keine verschachtelten Abläufe
        var profiles = ProfileResolver.GetProfiles(Settings, item);
        var profile = (profiles.FirstOrDefault(p => p.Name.Equals(step.ProfileName, StringComparison.CurrentCultureIgnoreCase))
                       ?? profiles.FirstOrDefault(p => p.IsDefault) ?? profiles[0]).Clone();
        profile.NoExit = false; // sonst bliebe die Konsole offen und der Ablauf stünde

        var startedAfter = DateTime.UtcNow.AddSeconds(-1);
        HistoryOrigin = originPrefix + workflowName;
        try { RunProfile(item, profile); }
        finally { HistoryOrigin = string.Empty; }

        // Eintrag finden (bei Dialog erst nach „Ausführen“) und warten, bis er fertig ist
        var deadline = DateTime.UtcNow + StepTimeout;
        HistoryEntry? entry = null;
        while (DateTime.UtcNow < deadline && !cancelled())
        {
            entry ??= History.Entries.FirstOrDefault(e => e.StartUtc >= startedAfter && e.Path.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
            if (entry != null && entry.Result != HistoryResult.Running) return entry.Result;
            await Task.Delay(400);
        }
        return HistoryResult.Cancelled;
    }

    /// <summary>Gespeicherten Ablauf starten.</summary>
    private void RunWorkflowEntry(LaunchItem item, CustomEntry wf) =>
        _ = RunWorkflowAsync(item.DisplayName, wf.Steps.ToList(), wf.StopOnError, item);

    /// <summary>Ablauf bearbeiten: Name, Schritte (Reihenfolge, entfernen, Startprofil), „bei Fehler abbrechen“.</summary>
    public void EditWorkflow(CustomEntry wf)
    {
        HidePanels(force: true);
        Ui.ShowWorkflow(new WorkflowEditorViewModel(this, wf));
    }

    /// <summary>Vom Ablauf-Editor nach dem Speichern.</summary>
    public void OnWorkflowSaved(CustomEntry original, CustomEntry edited)
    {
        RememberForUndo($"Ablauf „{edited.Name}“ geändert");
        var i = Settings.CustomEntries.FindIndex(c => c.Id == original.Id);
        if (i >= 0) Settings.CustomEntries[i] = edited;
        CommitUserChange();
    }
}
