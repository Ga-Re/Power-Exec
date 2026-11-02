namespace PowerExec.Core.Config;

public enum EntryVisibility { Visible, Hidden, Pending }

/// <summary>Sichtbarkeit einzelner Einträge eines Ordners (Haken in den Einstellungen, Freigabe neuer Dateien).</summary>
public static class FolderVisibility
{
    public static string Normalize(string relativePath) => relativePath.Replace('/', '\\').TrimStart('\\');

    public static string RelativePath(FolderConfig folder, string fullPath) =>
        Normalize(Path.GetRelativePath(folder.Path, fullPath));

    public static bool IsHidden(FolderConfig folder, string relativePath)
    {
        var rel = Normalize(relativePath);
        var name = rel.Contains('\\') ? rel[(rel.LastIndexOf('\\') + 1)..] : rel;
        foreach (var raw in folder.Hidden)
        {
            var h = Normalize(raw);
            if (h.Equals(rel, StringComparison.OrdinalIgnoreCase)) return true;
            if (!h.Contains('\\') && h.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static bool IsKnown(FolderConfig folder, string relativePath)
    {
        var rel = Normalize(relativePath);
        return folder.KnownFiles.Any(k => Normalize(k).Equals(rel, StringComparison.OrdinalIgnoreCase));
    }

    public static EntryVisibility State(FolderConfig folder, string relativePath)
    {
        if (IsHidden(folder, relativePath)) return EntryVisibility.Hidden;
        if (folder.NewFiles == NewFilesMode.RequireApproval && !IsKnown(folder, relativePath)) return EntryVisibility.Pending;
        return EntryVisibility.Visible;
    }

    /// <summary>Haken setzen oder entfernen. Der Eintrag gilt danach in jedem Fall als bekannt.</summary>
    public static void SetVisible(FolderConfig folder, string relativePath, bool visible)
    {
        var rel = Normalize(relativePath);
        var name = rel.Contains('\\') ? rel[(rel.LastIndexOf('\\') + 1)..] : rel;
        MarkKnown(folder, rel);
        folder.Hidden.RemoveAll(h =>
        {
            var n = Normalize(h);
            return n.Equals(rel, StringComparison.OrdinalIgnoreCase) || (!n.Contains('\\') && n.Equals(name, StringComparison.OrdinalIgnoreCase));
        });
        if (!visible) folder.Hidden.Add(rel);
    }

    public static void MarkKnown(FolderConfig folder, string relativePath)
    {
        if (!IsKnown(folder, relativePath)) folder.KnownFiles.Add(Normalize(relativePath));
    }

    /// <summary>
    /// Nach einem erfolgreichen Scan: Im Modus „Automatisch anzeigen“ werden alle gefundenen Dateien als bekannt
    /// gemerkt (damit ein späterer Wechsel zu „Erst nach Freigabe“ nicht alles als neu meldet).
    /// Nicht mehr vorhandene Dateien werden aus der Liste der bekannten entfernt.
    /// </summary>
    /// <returns>true, wenn sich etwas geändert hat.</returns>
    public static bool UpdateKnown(FolderConfig folder, IEnumerable<string> foundRelativePaths)
    {
        var found = new HashSet<string>(foundRelativePaths.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        int before = folder.KnownFiles.Count;
        var known = new HashSet<string>(folder.KnownFiles.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        if (folder.NewFiles == NewFilesMode.AutoShow)
            foreach (var f in found)
                if (known.Add(f)) changed = true;
        var pruned = known.Where(found.Contains).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        if (pruned.Count != before || changed)
        {
            folder.KnownFiles = pruned;
            return true;
        }
        return false;
    }

    /// <summary>Alte Einzeldatei-Ausschlüsse (vor 0.5) in die Liste der ausgeblendeten Dateien übernehmen.</summary>
    public static void MigrateLegacyExcludes(FolderConfig folder)
    {
        var files = folder.Excludes.Where(e => Scanning.ExclusionMatcher.KindOf(e) == Scanning.ExclusionKind.File).ToList();
        foreach (var f in files)
        {
            folder.Excludes.Remove(f);
            if (!folder.Hidden.Contains(f, StringComparer.OrdinalIgnoreCase)) folder.Hidden.Add(Normalize(f));
        }
    }
}
