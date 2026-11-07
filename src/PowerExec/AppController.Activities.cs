using System.Diagnostics;
using System.Windows;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.ViewModels;

namespace PowerExec;

/// <summary>Aktivitäten-Leiste: laufende und gerade beendete Starts für alle Panels.</summary>
public sealed partial class AppController
{
    private ActivityList? _activities;

    public ActivityList Activities
    {
        get
        {
            if (_activities != null) return _activities;
            _activities = new ActivityList(UiDispatcher, Services.Launcher.DoneFileOf);
            _activities.Changed += OnActivitiesChanged;
            return _activities;
        }
    }

    private void OnActivitiesChanged()
    {
        var busy = _activities?.HasRunning == true;
        foreach (var h in _hosts)
        {
            h.ViewModel.RefreshActivities();
            h.View.SetBusy(busy);
        }
    }

    /// <summary>Für lokale Starts: Prozess in der Aktivitäten-Leiste verfolgen (nur Scripte), sonst null.</summary>
    public Action<Process>? TrackStarted(LaunchItem item, ExecutionMode mode, Action<ActivityViewModel>? created = null, Action? restart = null)
    {
        if (!ActivityList.ShouldTrack(item)) return null;
        var name = Settings.FindEntry(item.FullPath)?.DisplayName is { Length: > 0 } dn ? dn : item.DisplayName;
        var detail = mode switch
        {
            ExecutionMode.Administrator => "lokal · als Administrator",
            ExecutionMode.OtherUser => "lokal · als anderer Benutzer",
            _ => "lokal"
        };
        return p => Activities.TrackProcess(item, name, p, detail, created, restart);
    }

    /// <summary>
    /// Lokal starten mit Verlauf und Aktivität; „Erneut starten“ in der Aktivität wiederholt denselben Start
    /// (gleiche Werte, neuer Verlaufseintrag).
    /// </summary>
    public bool StartLocalTracked(LaunchItem item, ExecutionRequest request, Func<HistoryEntry> beginHistory, object? owner)
    {
        var entry = beginHistory();
        var ok = Launcher.StartLocal(request, owner as System.Windows.Window, TrackLocal(item, request.Mode, entry, () => StartLocalTracked(item, request, beginHistory, null)));
        if (!ok) CompleteHistory(entry, HistoryResult.Cancelled, "nicht gestartet");
        return ok;
    }
}
