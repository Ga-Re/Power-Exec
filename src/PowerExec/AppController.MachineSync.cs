using PowerExec.Infrastructure;
using PowerExec.Core.Config;
using PowerExec.Services;

namespace PowerExec;

/// <summary>
/// Dynamische Rechnergruppen: beim Start und danach im eingestellten Intervall aus dem Active Directory aktualisieren.
/// Die zuletzt gefundene Liste bleibt gespeichert – ohne AD-Verbindung gilt sie weiter.
/// </summary>
public sealed partial class AppController
{
    private IUiTimer? _adSyncTimer;
    private bool _adSyncRunning;

    /// <summary>Synchronisierung beendet (Einstellungsfenster aktualisiert seine Anzeige).</summary>
    public event Action? MachineGroupsSynced;

    private void StartMachineSync()
    {
        _adSyncTimer ??= UiDispatcher.CreateTimer(TimeSpan.FromMinutes(10), () => _ = SyncDueMachineGroupsAsync());
        _adSyncTimer.Start();
        // Kurz nach dem Start, damit der Start selbst nicht auf das AD wartet
        _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ => UiDispatcher.Post(() => _ = SyncDueMachineGroupsAsync()));
    }

    /// <summary>Alle fälligen dynamischen Gruppen aktualisieren (nie synchronisiert oder älter als das Intervall).</summary>
    public async Task SyncDueMachineGroupsAsync(bool force = false)
    {
        if (_adSyncRunning) return;
        var due = Settings.MachineGroups.Where(g => g.IsDynamic && (force || AdRules.IsDue(g, Settings.AdSyncIntervalHours, DateTime.UtcNow))).ToList();
        if (due.Count == 0) return;
        _adSyncRunning = true;
        try
        {
            foreach (var g in due) await SyncMachineGroupAsync(g);
            SaveSettings();
            MachineGroupsSynced?.Invoke();
        }
        finally { _adSyncRunning = false; }
    }

    /// <summary>Eine dynamische Gruppe aus dem AD lesen; bei Fehler bleibt die bisherige Liste.</summary>
    /// <returns>Gefundene Rechner oder null bei Fehler.</returns>
    public static async Task<IReadOnlyList<string>?> SyncMachineGroupAsync(MachineGroup g)
    {
        if (g.AdQuery is not { } q) return null;
        if (AdRules.Validate(q) is { } invalid)
        {
            g.LastSyncError = invalid;
            return null;
        }
        try
        {
            var names = await AdSearch.QueryAsync(AdRules.BuildFilter(q, DateTime.UtcNow), q.SearchBase);
            g.Machines = names.ToList();
            g.LastSyncUtc = DateTime.UtcNow;
            g.LastSyncError = null;
            AppLog.Info($"Rechnergruppe @{g.Name}: {names.Count} Rechner aus dem AD");
            return names;
        }
        catch (Exception ex)
        {
            g.LastSyncError = ex.Message;
            g.LastSyncUtc = DateTime.UtcNow; // nicht jede Minute erneut versuchen
            AppLog.Info($"Rechnergruppe @{g.Name}: AD nicht erreichbar ({ex.Message})");
            return null;
        }
    }
}
