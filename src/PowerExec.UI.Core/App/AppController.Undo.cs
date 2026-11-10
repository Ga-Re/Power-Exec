using PowerExec.Core.Localization;
using PowerExec.Infrastructure;
using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.Services;

namespace PowerExec;

/// <summary>
/// Rückgängig für Änderungen im Panel (Ausblenden, Entfernen, Verschieben, Gruppen …):
/// vor der Änderung wird der Stand gemerkt, danach erscheint ein Hinweis mit „Rückgängig“ (auch Strg+Z).
/// </summary>
public partial class AppController
{
    private readonly UndoStack<AppSettings> _undo = new(10);
    private IUiTimer? _undoTimer;
    private bool _undoing;

    /// <summary>Wie lange der Hinweis sichtbar bleibt.</summary>
    public static readonly TimeSpan UndoToastDuration = TimeSpan.FromSeconds(8);

    /// <summary>Text des Hinweises im Panel (null = kein Hinweis).</summary>
    public string? UndoToast { get; private set; }
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Vor einer Änderung aufrufen: merkt den aktuellen Stand.</summary>
    internal void RememberForUndo(string description) => _undo.Push(SettingsStore.Clone(Settings), description);

    /// <summary>Nach der Änderung: Hinweis mit „Rückgängig“ zeigen.</summary>
    internal void ShowUndoToast(string text)
    {
        UndoToast = text;
        _undoTimer ??= CreateUndoTimer();
        _undoTimer.Stop();
        _undoTimer.Start();
        RefreshUndo();
    }

    private string DisplayNameOf(LaunchItem item) =>
        Settings.FindEntry(item.FullPath)?.DisplayName is { Length: > 0 } dn ? dn : item.DisplayName;

    private IUiTimer CreateUndoTimer() => UiDispatcher.CreateTimer(UndoToastDuration, DismissUndoToast);

    public void DismissUndoToast()
    {
        _undoTimer?.Stop();
        if (UndoToast == null) return;
        UndoToast = null;
        RefreshUndo();
    }

    /// <summary>Hält den Hinweis offen, solange die Maus darüber ist.</summary>
    public void PauseUndoToast(bool paused)
    {
        if (_undoTimer == null || UndoToast == null) return;
        if (paused) _undoTimer.Stop();
        else { _undoTimer.Stop(); _undoTimer.Start(); }
    }

    private void RefreshUndo()
    {
        foreach (var h in _hosts) h.ViewModel.RefreshUndo();
    }

    /// <summary>Letzte Änderung zurücknehmen. Laufzeitstand (Verlauf, Häufigkeit, Parameter) bleibt erhalten.</summary>
    public void Undo()
    {
        if (!_undo.TryPop(out var step) || step == null) return;
        var restored = step.Before;
        restored.RecentRuns = Settings.RecentRuns.ToList();
        restored.RunStats = new Dictionary<string, RunStat>(Settings.RunStats);
        restored.ParameterHistory = Settings.ParameterHistory.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
        restored.Fingerprints = new Dictionary<string, string>(Settings.Fingerprints);
        // Laufzeitstand der Zeitpläne (letzter/nächster Lauf) behalten
        foreach (var sch in restored.Schedules)
            if (Settings.Schedules.FirstOrDefault(x => x.Id == sch.Id) is { } current) { sch.LastRunLocal = current.LastRunLocal; sch.NextRunLocal = current.NextRunLocal; }
        _undoing = true;
        try { ApplySettings(restored); }
        finally { _undoing = false; }
        SettingsChangedOutside?.Invoke();
        AppLog.Info(L.F("Rückgängig: {0}", step.Description));
        UndoToast = null;
        _undoTimer?.Stop();
        RefreshUndo();
    }

    /// <summary>Änderungen außerhalb des Panels (Einstellungen, Arbeitsbereich): alte Schritte passen nicht mehr.</summary>
    private void ClearUndo()
    {
        if (_undoing) return;
        _undo.Clear();
        DismissUndoToast();
    }
}
