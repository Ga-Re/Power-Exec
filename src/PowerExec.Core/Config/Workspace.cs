namespace PowerExec.Core.Config;

/// <summary>Ein Arbeitsbereich: eigene Ordner, Profile, Kategorien, Favoriten, Verlauf und eigene Einträge.</summary>
public sealed class Workspace
{
    public string Name { get; set; } = "Standard";
    public List<FolderConfig> Folders { get; set; } = new();
    public List<EntryConfig> Entries { get; set; } = new();
    public List<CustomEntry> CustomEntries { get; set; } = new();
    public List<string> Favorites { get; set; } = new();
    public List<string> CategoryOrder { get; set; } = new();
    public List<RecentRun> RecentRuns { get; set; } = new();
    public Dictionary<string, List<string>> CustomOrder { get; set; } = new();
    public Dictionary<string, string> Fingerprints { get; set; } = new();
    public List<string> CollapsedGroups { get; set; } = new();
}

/// <summary>
/// Verwaltet Arbeitsbereiche. Die Daten des aktiven Arbeitsbereichs stehen direkt in <see cref="AppSettings"/>
/// (Folders, Entries …); beim Wechsel werden sie in den Arbeitsbereich zurückgeschrieben und die des Ziels geladen.
/// </summary>
public static class WorkspaceManager
{
    public static void EnsureInitialized(AppSettings s)
    {
        if (s.Workspaces.Count == 0)
            s.Workspaces.Add(new Workspace { Name = string.IsNullOrWhiteSpace(s.ActiveWorkspace) ? "Standard" : s.ActiveWorkspace });
        if (string.IsNullOrWhiteSpace(s.ActiveWorkspace) || Find(s, s.ActiveWorkspace) == null)
            s.ActiveWorkspace = s.Workspaces[0].Name;
    }

    public static Workspace? Find(AppSettings s, string name) =>
        s.Workspaces.FirstOrDefault(w => w.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Aktuelle Daten in den aktiven Arbeitsbereich schreiben.</summary>
    public static void Capture(AppSettings s)
    {
        EnsureInitialized(s);
        var ws = Find(s, s.ActiveWorkspace)!;
        ws.Folders = s.Folders.Select(f => f.Clone()).ToList();
        ws.Entries = s.Entries.Select(e => e.Clone()).ToList();
        ws.CustomEntries = s.CustomEntries.Select(c => c.Clone()).ToList();
        ws.Favorites = new List<string>(s.Favorites);
        ws.CategoryOrder = new List<string>(s.CategoryOrder);
        ws.RecentRuns = s.RecentRuns.Select(r => new RecentRun { Path = r.Path, ProfileName = r.ProfileName, TimeUtc = r.TimeUtc }).ToList();
        ws.CustomOrder = s.CustomOrder.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
        ws.Fingerprints = new Dictionary<string, string>(s.Fingerprints);
        ws.CollapsedGroups = new List<string>(s.CollapsedGroups);
    }

    private static void Load(AppSettings s, Workspace ws)
    {
        s.Folders = ws.Folders.Select(f => f.Clone()).ToList();
        s.Entries = ws.Entries.Select(e => e.Clone()).ToList();
        s.CustomEntries = ws.CustomEntries.Select(c => c.Clone()).ToList();
        s.Favorites = new List<string>(ws.Favorites);
        s.CategoryOrder = new List<string>(ws.CategoryOrder);
        s.RecentRuns = ws.RecentRuns.Select(r => new RecentRun { Path = r.Path, ProfileName = r.ProfileName, TimeUtc = r.TimeUtc }).ToList();
        s.CustomOrder = ws.CustomOrder.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
        s.Fingerprints = new Dictionary<string, string>(ws.Fingerprints);
        s.CollapsedGroups = new List<string>(ws.CollapsedGroups);
        s.ActiveWorkspace = ws.Name;
    }

    /// <returns>false, wenn es den Arbeitsbereich nicht gibt.</returns>
    public static bool Switch(AppSettings s, string name)
    {
        EnsureInitialized(s);
        var target = Find(s, name);
        if (target == null) return false;
        if (target.Name.Equals(s.ActiveWorkspace, StringComparison.CurrentCultureIgnoreCase)) return true;
        Capture(s);
        Load(s, target);
        return true;
    }

    /// <summary>Neuen Arbeitsbereich anlegen – leer oder als Kopie des aktuellen.</summary>
    public static Workspace Create(AppSettings s, string name, bool copyCurrent)
    {
        EnsureInitialized(s);
        name = UniqueName(s, string.IsNullOrWhiteSpace(name) ? "Neuer Arbeitsbereich" : name.Trim());
        Capture(s);
        var ws = new Workspace { Name = name };
        if (copyCurrent)
        {
            var current = Find(s, s.ActiveWorkspace)!;
            ws.Folders = current.Folders.Select(f => f.Clone()).ToList();
            ws.Entries = current.Entries.Select(e => e.Clone()).ToList();
            ws.CustomEntries = current.CustomEntries.Select(c => c.Clone()).ToList();
            ws.Favorites = new List<string>(current.Favorites);
            ws.CategoryOrder = new List<string>(current.CategoryOrder);
        }
        s.Workspaces.Add(ws);
        return ws;
    }

    public static bool Rename(AppSettings s, string oldName, string newName)
    {
        var ws = Find(s, oldName);
        newName = newName.Trim();
        if (ws == null || newName.Length == 0) return false;
        if (Find(s, newName) is { } other && !ReferenceEquals(other, ws)) return false;
        bool active = ws.Name.Equals(s.ActiveWorkspace, StringComparison.CurrentCultureIgnoreCase);
        ws.Name = newName;
        if (active) s.ActiveWorkspace = newName;
        return true;
    }

    /// <summary>Löscht einen Arbeitsbereich (nicht den aktiven und nicht den letzten).</summary>
    public static bool Delete(AppSettings s, string name)
    {
        if (s.Workspaces.Count <= 1 || name.Equals(s.ActiveWorkspace, StringComparison.CurrentCultureIgnoreCase)) return false;
        return s.Workspaces.RemoveAll(w => w.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)) > 0;
    }

    private static string UniqueName(AppSettings s, string name)
    {
        var candidate = name;
        for (int i = 2; Find(s, candidate) != null; i++) candidate = $"{name} {i}";
        return candidate;
    }
}
