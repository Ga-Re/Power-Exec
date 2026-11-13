using System.Globalization;
using PowerExec.Core.Localization;
using PowerExec.UI.Infrastructure;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.UI.Services;

namespace PowerExec.UI;

/// <summary>
/// Geplante Ausführung (D4): Einträge einmalig, täglich (an Wochentagen) oder im Intervall starten.
/// Läuft, solange Power-Exec läuft (mit „Mit Windows starten“ also dauerhaft); verpasste Termine auf Wunsch nachholen.
/// Ergebnisse landen im Verlauf, Benachrichtigung auf Wunsch nur bei Fehlern.
/// </summary>
public partial class AppController
{
    /// <summary>Wie lange ein Termin zurückliegen darf, um ohne „nachholen“ noch zu starten.</summary>
    public static readonly TimeSpan ScheduleGrace = TimeSpan.FromMinutes(2);
    private IUiTimer? _scheduleTimer;
    private readonly HashSet<string> _scheduleRunning = new();

    /// <summary>Zeitpläne haben sich geändert (Anzeige im Panel aktualisieren).</summary>
    public event Action? SchedulesChanged;

    public void StartScheduler()
    {
        var now = DateTime.Now;
        foreach (var s in Settings.Schedules)
        {
            if (s.NextRunLocal == null) s.NextRunLocal = Schedules.Next(s, now);
            // Verpasst und nicht nachholen: auf den nächsten Termin springen
            else if (s.NextRunLocal < now && !Schedules.IsDue(s, now, ScheduleGrace)) Schedules.Advance(s, now, ran: false);
        }
        _scheduleTimer ??= UiDispatcher.CreateTimer(TimeSpan.FromSeconds(20), OnScheduleTick);
        _scheduleTimer.Start();
        OnScheduleTick();
    }

    private void OnScheduleTick()
    {
        var now = DateTime.Now;
        foreach (var s in Settings.Schedules.ToList())
        {
            if (!Schedules.IsDue(s, now, ScheduleGrace) || _scheduleRunning.Contains(s.Id)) continue;
            Schedules.Advance(s, now, ran: true);
            SaveSettings();
            SchedulesChanged?.Invoke();
            RunScheduledAsync(s.Clone()).Forget();
        }
    }

    private async Task RunScheduledAsync(Schedule s)
    {
        _scheduleRunning.Add(s.Id);
        try
        {
            var item = FindItem(s.Path);
            if (item == null)
            {
                Ui.Notify(L.T("Scheduled launch not possible"), L.F("{0} wasn't found.", s.Name), warning: true);
                return;
            }
            AppLog.Info(string.Format(CultureInfo.InvariantCulture, "Schedule: {0} ({1})", s.Name, Schedules.Describe(s)));
            HistoryResult result;
            if (item.Source is { Kind: CustomEntryKind.Workflow } wf)
                result = await RunWorkflowAsync(item.DisplayName, wf.Steps.ToList(), wf.StopOnError, item) ? HistoryResult.Success : HistoryResult.Error;
            else
                result = await RunStepAsync(new WorkflowStep { Path = s.Path, Name = s.Name, ProfileName = s.ProfileName }, s.Name, () => false, L.T("Schedule · "));
            bool problem = result is HistoryResult.Error or HistoryResult.Warning or HistoryResult.Cancelled;
            if (problem || !s.NotifyOnlyOnError)
                Ui.Notify(problem ? L.F("Scheduled launch: {0} with problems", s.Name) : L.F("Scheduled launch: {0} finished", s.Name),
                    History.Entries.FirstOrDefault(h => h.Path.Equals(s.Path, StringComparison.OrdinalIgnoreCase))?.Summary ?? string.Empty,
                    () => ShowHistoryInPanel(), warning: problem);
        }
        catch (Exception ex)
        {
            AppLog.Error(string.Format(CultureInfo.InvariantCulture, "Schedule {0} failed", s.Name), ex);
        }
        finally
        {
            _scheduleRunning.Remove(s.Id);
        }
    }

    /// <summary>Panel mit geöffnetem Verlauf zeigen (Klick auf die Benachrichtigung).</summary>
    public void ShowHistoryInPanel()
    {
        var host = _hosts.FirstOrDefault();
        if (host == null) return;
        ShowPanelFromHotkey(host, forcePanel: true);
        host.ViewModel.ShowHistory = true;
    }

    /// <summary>Zeitplan anlegen oder ändern (Planen-Dialog).</summary>
    public void SaveSchedule(Schedule s)
    {
        s.NextRunLocal = Schedules.Next(s, DateTime.Now);
        var i = Settings.Schedules.FindIndex(x => x.Id == s.Id);
        if (i >= 0) Settings.Schedules[i] = s;
        else Settings.Schedules.Add(s);
        CommitUserChange();
        SchedulesChanged?.Invoke();
    }

    public void RemoveSchedule(Schedule s)
    {
        RememberForUndo(L.F("Schedule “{0}” removed", s.Name));
        Settings.Schedules.RemoveAll(x => x.Id == s.Id);
        CommitUserChange();
        SchedulesChanged?.Invoke();
        ShowUndoToast(L.F("Schedule for “{0}” removed", s.Name));
    }

    /// <summary>Planen-Dialog für einen Eintrag.</summary>
    public void ShowSchedule(Core.Models.LaunchItem item)
    {
        HidePanels(force: true);
        Ui.ShowSchedule(new ViewModels.ScheduleViewModel(this, item));
    }
}
