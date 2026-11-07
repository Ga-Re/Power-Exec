using PowerExec.Infrastructure;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Services;

namespace PowerExec;

/// <summary>
/// Geplante Ausführung (D4): Einträge einmalig, täglich (an Wochentagen) oder im Intervall starten.
/// Läuft, solange Power-Exec läuft (mit „Mit Windows starten“ also dauerhaft); verpasste Termine auf Wunsch nachholen.
/// Ergebnisse landen im Verlauf, Benachrichtigung auf Wunsch nur bei Fehlern.
/// </summary>
public sealed partial class AppController
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
            _ = RunScheduledAsync(s.Clone());
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
                _tray?.ShowBalloon("Geplanter Start nicht möglich", $"{s.Name} wurde nicht gefunden.", warning: true);
                return;
            }
            AppLog.Info($"Zeitplan: {s.Name} ({Schedules.Describe(s)})");
            HistoryResult result;
            if (item.Source is { Kind: CustomEntryKind.Workflow } wf)
                result = await RunWorkflowAsync(item.DisplayName, wf.Steps.ToList(), wf.StopOnError, item) ? HistoryResult.Success : HistoryResult.Error;
            else
                result = await RunStepAsync(new WorkflowStep { Path = s.Path, Name = s.Name, ProfileName = s.ProfileName }, s.Name, () => false, "Zeitplan · ");
            bool problem = result is HistoryResult.Error or HistoryResult.Warning or HistoryResult.Cancelled;
            if (problem || !s.NotifyOnlyOnError)
                _tray?.ShowBalloon(problem ? $"Geplanter Start: {s.Name} mit Problemen" : $"Geplanter Start: {s.Name} fertig",
                    History.Entries.FirstOrDefault(h => h.Path.Equals(s.Path, StringComparison.OrdinalIgnoreCase))?.Summary ?? string.Empty,
                    () => ShowHistoryInPanel(), warning: problem);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Zeitplan {s.Name} fehlgeschlagen", ex);
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
        RememberForUndo($"Zeitplan „{s.Name}“ entfernt");
        Settings.Schedules.RemoveAll(x => x.Id == s.Id);
        CommitUserChange();
        SchedulesChanged?.Invoke();
        ShowUndoToast($"Zeitplan für „{s.Name}“ entfernt");
    }

    /// <summary>Planen-Dialog für einen Eintrag.</summary>
    public void ShowSchedule(Core.Models.LaunchItem item)
    {
        HidePanels(force: true);
        var w = new Views.ScheduleWindow(new ViewModels.ScheduleViewModel(this, item));
        w.Show();
        w.Activate();
    }
}
