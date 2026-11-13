using PowerExec.Core.Localization;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Search;
using PowerExec.Core.Updates;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI;

/// <content>Umbenannte und verschobene Dateien erkennen (Profile und Favoriten ziehen mit).</content>
public partial class AppController
{
    /// <summary>
    /// Nach jedem Scan: im Modus „Automatisch anzeigen“ neue Dateien als bekannt merken,
    /// verschwundene vergessen. Ordner mit Fehler (z. B. Freigabe nicht erreichbar) bleiben unverändert.
    /// </summary>
    private void UpdateKnownFiles()
    {
        if (Repository.IsScanning) return;
        bool changed = false;
        foreach (var folder in Settings.Folders)
        {
            var status = Repository.FolderStatuses.FirstOrDefault(s => s.Path.Equals(folder.Path, StringComparison.OrdinalIgnoreCase));
            if (status == null || status.Error != null || status.FromCache) continue;
            var found = Repository.Items
                .Where(i => i.RootFolder.Equals(folder.Path, StringComparison.OrdinalIgnoreCase))
                .Select(i => FolderVisibility.RelativePath(folder, i.FullPath));
            changed |= FolderVisibility.UpdateKnown(folder, found);
        }
        if (changed) SaveSettings();
    }

    private static readonly Dictionary<string, (DateTime Written, string? Fingerprint)> FingerprintCache = new(StringComparer.OrdinalIgnoreCase);

    private static string? CachedFingerprint(string path)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(path);
            lock (FingerprintCache)
                if (FingerprintCache.TryGetValue(path, out var c) && c.Written == written) return c.Fingerprint;
            var fp = PathMigration.Fingerprint(path);
            lock (FingerprintCache) FingerprintCache[path] = (written, fp);
            return fp;
        }
        catch
        {
            return null;
        }
    }

    private bool _maintaining;

    /// <summary>
    /// Nach jedem Scan: umbenannte/verschobene Dateien wiederfinden (Profile, Favorit, Sortierung übernehmen)
    /// und Fingerabdrücke der gemerkten Dateien aktualisieren. Läuft im Hintergrund.
    /// </summary>
    private async Task MaintainPathsAsync()
    {
        if (Repository.IsScanning || _maintaining) return;
        _maintaining = true;
        try
        {
            var okRoots = Repository.FolderStatuses.Where(s => s.Error == null && !s.FromCache).Select(s => s.Path).ToList();
            if (okRoots.Count == 0) return;
            var existing = Repository.Items
                .Where(i => okRoots.Any(r => r.Equals(i.RootFolder, StringComparison.OrdinalIgnoreCase)))
                .Select(i => i.FullPath).ToList();
            var snapshot = SettingsStore.Clone(Settings);
            var (moves, fpChanged) = await Task.Run(() =>
            {
                var m = PathMigration.Migrate(snapshot, existing, okRoots, CachedFingerprint, p => { try { return new FileInfo(p).Length; } catch { return (long?)null; } });
                var c = PathMigration.UpdateFingerprints(snapshot, existing, CachedFingerprint);
                return (m, c);
            });
            if (moves.Count == 0 && !fpChanged) return;
            foreach (var m in moves)
            {
                PathMigration.Apply(Settings, m);
                History.Rename(m.OldPath, m.NewPath);
                AppLog.Info(string.Format("Renamed/moved detected: {0} → {1} (profiles and favorite carried over)", m.OldPath, m.NewPath));
            }
            Settings.Fingerprints = snapshot.Fingerprints;
            SaveSettings();
            if (moves.Count > 0)
            {
                RebuildPanels();
                Ui.Notify(L.T("File found again"), moves.Count == 1
                    ? L.F("{0} is now called {1}. Profiles and favorite were carried over.", Path.GetFileName(moves[0].OldPath), Path.GetFileName(moves[0].NewPath))
                    : L.F("{0} renamed or moved files detected. Profiles and favorites were carried over.", moves.Count));
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Matching renamed files failed", ex);
        }
        finally
        {
            _maintaining = false;
        }
    }
}
