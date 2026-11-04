using System.Security.Cryptography;

namespace PowerExec.Core.Config;

public sealed record PathMove(string OldPath, string NewPath);

/// <summary>
/// Findet umbenannte oder verschobene Dateien wieder und übernimmt Profile, Favorit, Sortierung und Verlauf.
/// Erkennung über einen Fingerabdruck (Größe + SHA-256), ersatzweise über den gleichen Dateinamen im gleichen Ordner-Baum.
/// </summary>
public static class PathMigration
{
    public static string? Fingerprint(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var hash = SHA256.HashData(fs);
            return $"{fs.Length}:{Convert.ToHexString(hash).ToLowerInvariant()}";
        }
        catch
        {
            return null;
        }
    }

    public static long? SizeOf(string fingerprint)
    {
        var i = fingerprint.IndexOf(':');
        return i > 0 && long.TryParse(fingerprint[..i], out var size) ? size : null;
    }

    /// <summary>Pfade, die etwas Gespeichertes haben (Profil/Anzeigename oder Favorit).</summary>
    public static HashSet<string> TrackedPaths(AppSettings s)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in s.Entries) set.Add(e.Path);
        foreach (var f in s.Favorites) set.Add(f);
        return set;
    }

    /// <param name="existing">Alle Dateien der erfolgreich eingelesenen Ordner.</param>
    /// <param name="scannedRoots">Erfolgreich eingelesene Ordner (nur dort werden fehlende Dateien als verschwunden gewertet).</param>
    /// <param name="fingerprintOf">Berechnet den Fingerabdruck einer Datei.</param>
    /// <param name="sizeOf">Liefert die Dateigröße (schneller Vorfilter).</param>
    public static IReadOnlyList<PathMove> Migrate(AppSettings s, IReadOnlyCollection<string> existing, IReadOnlyCollection<string> scannedRoots,
        Func<string, string?> fingerprintOf, Func<string, long?> sizeOf)
    {
        var exists = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var tracked = TrackedPaths(s);
        var orphans = tracked.Where(p => !exists.Contains(p) && RootOf(p, scannedRoots) != null).ToList();
        if (orphans.Count == 0) return Array.Empty<PathMove>();

        var candidates = existing.Where(p => !tracked.Contains(p)).ToList();
        var moves = new List<PathMove>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fpCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var orphan in orphans)
        {
            var ext = Path.GetExtension(orphan);
            var root = RootOf(orphan, scannedRoots)!;
            string? match = null;

            if (s.Fingerprints.TryGetValue(orphan, out var fp))
            {
                var size = SizeOf(fp);
                var byHash = candidates
                    .Where(c => !claimed.Contains(c) && Path.GetExtension(c).Equals(ext, StringComparison.OrdinalIgnoreCase))
                    .Where(c => size == null || sizeOf(c) == size)
                    .Where(c =>
                    {
                        if (!fpCache.TryGetValue(c, out var cfp)) fpCache[c] = cfp = fingerprintOf(c);
                        return cfp == fp;
                    })
                    .ToList();
                if (byHash.Count == 1) match = byHash[0];
            }

            if (match == null)
            {
                // Verschoben: gleicher Dateiname, eindeutig, im gleichen eingelesenen Ordner
                var name = Path.GetFileName(orphan);
                var byName = candidates
                    .Where(c => !claimed.Contains(c) && Path.GetFileName(c).Equals(name, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(RootOf(c, scannedRoots), root, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (byName.Count == 1) match = byName[0];
            }

            if (match != null)
            {
                claimed.Add(match);
                moves.Add(new PathMove(orphan, match));
            }
        }

        foreach (var m in moves) Apply(s, m);
        return moves;
    }

    public static void Apply(AppSettings s, PathMove m)
    {
        foreach (var e in s.Entries.Where(e => e.Path.Equals(m.OldPath, StringComparison.OrdinalIgnoreCase))) e.Path = m.NewPath;
        for (int i = 0; i < s.Favorites.Count; i++)
            if (s.Favorites[i].Equals(m.OldPath, StringComparison.OrdinalIgnoreCase)) s.Favorites[i] = m.NewPath;
        foreach (var order in s.CustomOrder.Values)
            for (int i = 0; i < order.Count; i++)
                if (order[i].Equals(m.OldPath, StringComparison.OrdinalIgnoreCase)) order[i] = m.NewPath;
        foreach (var r in s.RecentRuns.Where(r => r.Path.Equals(m.OldPath, StringComparison.OrdinalIgnoreCase))) r.Path = m.NewPath;
        RunStats.Rename(s, m.OldPath, m.NewPath);
        var key = s.Fingerprints.Keys.FirstOrDefault(k => k.Equals(m.OldPath, StringComparison.OrdinalIgnoreCase));
        if (key != null)
        {
            s.Fingerprints[m.NewPath] = s.Fingerprints[key];
            s.Fingerprints.Remove(key);
        }
    }

    /// <summary>Fingerabdrücke für alle gemerkten, vorhandenen Dateien aktualisieren; nicht mehr gemerkte entfernen.</summary>
    /// <returns>true, wenn sich etwas geändert hat.</returns>
    public static bool UpdateFingerprints(AppSettings s, IReadOnlyCollection<string> existing, Func<string, string?> fingerprintOf)
    {
        var exists = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var tracked = TrackedPaths(s);
        bool changed = false;
        foreach (var key in s.Fingerprints.Keys.ToList())
            if (!tracked.Contains(key)) { s.Fingerprints.Remove(key); changed = true; }
        foreach (var path in tracked.Where(exists.Contains))
        {
            var fp = fingerprintOf(path);
            if (fp == null) continue;
            if (!s.Fingerprints.TryGetValue(path, out var old) || old != fp)
            {
                s.Fingerprints[path] = fp;
                changed = true;
            }
        }
        return changed;
    }

    private static string? RootOf(string path, IReadOnlyCollection<string> roots) =>
        roots.Where(r => path.StartsWith(r.TrimEnd('\\', '/') + (path.Contains('\\') ? "\\" : "/"), StringComparison.OrdinalIgnoreCase))
             .OrderByDescending(r => r.Length).FirstOrDefault();
}
