using PowerExec.Core.Localization;
using PowerExec.Infrastructure;
using PowerExec.Core.Config;
using PowerExec.Services;

namespace PowerExec;

/// <summary>
/// Dynamische Rechnergruppen: beim Start und danach im eingestellten Intervall aus dem Active Directory aktualisieren.
/// Die zuletzt gefundene Liste bleibt gespeichert – ohne AD-Verbindung gilt sie weiter.
/// </summary>
public partial class AppController
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

    /// <summary>Eine dynamische Gruppe aus dem AD bzw. ihrer Liste lesen; bei Fehler bleibt die bisherige Liste.</summary>
    /// <returns>Gefundene Rechner oder null bei Fehler.</returns>
    public static async Task<IReadOnlyList<string>?> SyncMachineGroupAsync(MachineGroup g)
    {
        if (g.ListQuery is { } list) return await SyncFromListAsync(g, list);
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
            AppLog.Info(L.F("Rechnergruppe @{0}: {1} Rechner aus dem AD", g.Name, names.Count));
            return names;
        }
        catch (Exception ex)
        {
            g.LastSyncError = ex.Message;
            g.LastSyncUtc = DateTime.UtcNow; // nicht jede Minute erneut versuchen
            AppLog.Info(L.F("Rechnergruppe @{0}: AD nicht erreichbar ({1})", g.Name, ex.Message));
            return null;
        }
    }

    /// <summary>Listengruppe: Datei neu lesen und filtern.</summary>
    private static async Task<IReadOnlyList<string>?> SyncFromListAsync(MachineGroup g, ListGroupQuery q)
    {
        try
        {
            if (q.Path.Trim().Length == 0) throw new InvalidOperationException(L.T("Keine Datei angegeben."));
            var text = await File.ReadAllTextAsync(q.Path.Trim());
            var names = MachineTable.Parse(text).Apply(q);
            g.Machines = names.ToList();
            g.LastSyncUtc = DateTime.UtcNow;
            g.LastSyncError = null;
            AppLog.Info(L.F("Rechnergruppe @{0}: {1} Rechner aus {2}", g.Name, names.Count, q.Path));
            return names;
        }
        catch (Exception ex)
        {
            g.LastSyncError = ex.Message;
            g.LastSyncUtc = DateTime.UtcNow;
            AppLog.Info(L.F("Rechnergruppe @{0}: Liste nicht lesbar ({1})", g.Name, ex.Message));
            return null;
        }
    }
}
