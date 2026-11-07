using PowerExec.Infrastructure;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec;

/// <summary>Verlauf aller Starts (B3): mit Ergebnis, Dauer, Zielen und Parametern; „Erneut ausführen“ mit denselben Werten.</summary>
public sealed partial class AppController
{
    private RunHistory? _history;
    private IUiTimer? _historySaveTimer;

    /// <summary>Speicherort; der Selbsttest leitet ihn in seinen Temp-Ordner um.</summary>
    public string HistoryPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec", "history.json");

    public RunHistory History => _history ??= RunHistory.Load(HistoryPath);

    /// <summary>Herkunft neuer Verlaufseinträge (Ablauf, Zeitplan); leer = direkt gestartet.</summary>
    public string HistoryOrigin { get; set; } = string.Empty;

    /// <summary>Der Verlauf hat sich geändert (Panel-Ansicht aktualisieren).</summary>
    public event Action? HistoryChanged;

    /// <summary>Neuen Eintrag anlegen (vor dem Start).</summary>
    public HistoryEntry BeginHistory(LaunchItem item, LaunchProfile? profile, ExecutionMode mode, PsEngine engine,
        IEnumerable<string>? targets, IReadOnlyDictionary<string, string?>? raw, string origin = "")
    {
        var name = Settings.FindEntry(item.FullPath)?.DisplayName is { Length: > 0 } dn ? dn : item.DisplayName;
        var entry = RunHistory.Create(item, name, profile == null || profile.IsImplicit ? "Standard" : profile.Name, mode, engine, targets, raw);
        entry.Origin = origin;
        entry.Result = HistoryResult.Running;
        History.Add(entry);
        HistoryUpdated();
        return entry;
    }

    /// <summary>Ergebnis eintragen.</summary>
    public void CompleteHistory(HistoryEntry entry, HistoryResult result, string summary, int? exitCode = null,
        IEnumerable<string>? failedTargets = null, string? logFile = null)
    {
        entry.Result = result;
        entry.EndUtc = DateTime.UtcNow;
        entry.Summary = summary;
        entry.ExitCode = exitCode;
        if (failedTargets != null) entry.FailedTargets = failedTargets.ToList();
        if (logFile != null) entry.LogFile = logFile;
        HistoryUpdated();
    }

    private void HistoryUpdated()
    {
        HistoryChanged?.Invoke();
        // Gesammelt speichern (viele Rechner melden kurz nacheinander)
        _historySaveTimer ??= CreateHistorySaveTimer();
        _historySaveTimer.Stop();
        _historySaveTimer.Start();
    }

    private IUiTimer CreateHistorySaveTimer()
    {
        IUiTimer? t = null;
        t = UiDispatcher.CreateTimer(TimeSpan.FromSeconds(1), () =>
        {
            t!.Stop();
            SaveHistory();
        });
        return t;
    }

    public void SaveHistory()
    {
        try { _history?.Save(HistoryPath); }
        catch (Exception ex) { AppLog.Error("Verlauf konnte nicht gespeichert werden", ex); }
    }

    /// <summary>Lauf mit denselben Werten wiederholen (bei Remote wahlweise nur die fehlgeschlagenen Rechner).</summary>
    public bool Replay(HistoryEntry entry, bool onlyFailed = false)
    {
        var item = FindItem(entry.Path);
        if (item == null)
        {
            Ui.Notify("Nicht mehr vorhanden", $"{entry.Name} wurde nicht gefunden (umbenannt, gelöscht oder ausgeblendet).", warning: true);
            return false;
        }
        if (onlyFailed && entry.FailedTargets.Count == 0) return false;
        RunProfile(item, RunHistory.ReplayProfile(entry, onlyFailed));
        return true;
    }

    /// <summary>Lokaler Start: Verlaufseintrag und Aktivität verbinden.</summary>
    public Action<System.Diagnostics.Process>? TrackLocal(LaunchItem item, ExecutionMode mode, HistoryEntry entry, Action? restart = null)
    {
        var track = TrackStarted(item, mode, restart: restart, created: activity => activity.Completed += () => CompleteHistory(entry,
            activity.State switch { ActivityState.Error => HistoryResult.Error, ActivityState.Warning => HistoryResult.Warning, _ => HistoryResult.Success },
            activity.Detail, activity.ExitCode));
        // Programme, Webseiten, Ordner …: nur „gestartet“, kein Ende
        if (track == null) CompleteHistory(entry, HistoryResult.Started, "gestartet");
        return track;
    }
}
