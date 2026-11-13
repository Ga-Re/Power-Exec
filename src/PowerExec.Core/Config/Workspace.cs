using PowerExec.Core.Localization;
namespace PowerExec.Core.Config;

/// <summary>
/// Ein Arbeitsbereich (ab 0.13): eigene Startprofile, Favoriten, „Zuletzt ausgeführt“, eingeklappte Gruppen und die
/// Auswahl der sichtbaren Gruppen. Gruppen, Ordner (smarte Gruppen), eigene Einträge, Anzeigenamen, Notizen und
/// Schlagworte sind global.
/// </summary>
public sealed class Workspace
{
    public string Name { get; set; } = "Standard";

    public List<string> Favorites { get; set; } = new();
    public List<RecentRun> RecentRuns { get; set; } = new();
    public List<string> CollapsedGroups { get; set; } = new();

    /// <summary>Startprofile je Eintrag (Pfad bzw. „custom:Id“).</summary>
    public Dictionary<string, List<LaunchProfile>> Profiles { get; set; } = new();

    /// <summary>In diesem Arbeitsbereich ausgeblendete Gruppen (samt Untergruppen). Neue Gruppen sind sichtbar.</summary>
    public List<string> HiddenGroups { get; set; } = new();

    // ---- Bis 0.12: eigene Daten je Arbeitsbereich (nur noch für die Übernahme) ----
    public List<FolderConfig> Folders { get; set; } = new();
    public List<EntryConfig> Entries { get; set; } = new();
    public List<CustomEntry> CustomEntries { get; set; } = new();
    public List<string> CategoryOrder { get; set; } = new();
    public Dictionary<string, List<string>> CustomOrder { get; set; } = new();
    public Dictionary<string, string> Fingerprints { get; set; } = new();
    public Dictionary<string, string> GroupIcons { get; set; } = new();

    internal bool HasLegacyData => Folders.Count > 0 || Entries.Count > 0 || CustomEntries.Count > 0 || CategoryOrder.Count > 0 || GroupIcons.Count > 0;

    internal void ClearLegacyData()
    {
        Folders = new();
        Entries = new();
        CustomEntries = new();
        CategoryOrder = new();
        CustomOrder = new();
        Fingerprints = new();
        GroupIcons = new();
    }
}

/// <summary>
/// Verwaltet Arbeitsbereiche. Die Daten des aktiven Arbeitsbereichs stehen direkt in <see cref="AppSettings"/>
/// (Favoriten, Verlauf, Profile in den Einträgen …); beim Wechsel werden sie zurückgeschrieben und die des Ziels geladen.
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
        s.Workspaces.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static Workspace Active(AppSettings s)
    {
        EnsureInitialized(s);
        return Find(s, s.ActiveWorkspace)!;
    }

    /// <summary>Ist die Gruppe im aktiven Arbeitsbereich sichtbar? (Ausgeblendete Gruppe blendet auch ihre Untergruppen aus.)</summary>
    public static bool ShowsGroup(AppSettings s, string groupPath) => ShowsGroupIn(Active(s), groupPath);

    public static bool ShowsGroupIn(Workspace ws, string groupPath) =>
        groupPath.Length == 0 || !ws.HiddenGroups.Any(h => GroupManager.IsSameOrBelow(groupPath, h));

    public static void SetGroupHidden(Workspace ws, string groupPath, bool hidden)
    {
        ws.HiddenGroups.RemoveAll(h => h.Equals(groupPath, StringComparison.OrdinalIgnoreCase));
        if (hidden) ws.HiddenGroups.Add(groupPath);
    }

    private static Dictionary<string, List<LaunchProfile>> ProfilesOf(IEnumerable<EntryConfig> entries) =>
        entries.Where(e => e.Profiles.Count > 0)
            .GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Profiles.Select(p => p.Clone()).ToList(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Aktuelle Daten in den aktiven Arbeitsbereich schreiben.</summary>
    public static void Capture(AppSettings s)
    {
        var ws = Active(s);
        ws.Favorites = new List<string>(s.Favorites);
        ws.RecentRuns = s.RecentRuns.Select(r => new RecentRun { Path = r.Path, ProfileName = r.ProfileName, TimeUtc = r.TimeUtc }).ToList();
        ws.CollapsedGroups = new List<string>(s.CollapsedGroups);
        ws.Profiles = ProfilesOf(s.Entries);
    }

    private static void Load(AppSettings s, Workspace ws)
    {
        s.Favorites = new List<string>(ws.Favorites);
        s.RecentRuns = ws.RecentRuns.Select(r => new RecentRun { Path = r.Path, ProfileName = r.ProfileName, TimeUtc = r.TimeUtc }).ToList();
        s.CollapsedGroups = new List<string>(ws.CollapsedGroups);
        foreach (var e in s.Entries) e.Profiles = new List<LaunchProfile>();
        foreach (var (path, profiles) in ws.Profiles)
        {
            var entry = s.FindEntry(path);
            if (entry == null)
            {
                entry = new EntryConfig { Path = path };
                s.Entries.Add(entry);
            }
            entry.Profiles = profiles.Select(p => p.Clone()).ToList();
        }
        s.ActiveWorkspace = ws.Name;
    }

    /// <returns>false, wenn es den Arbeitsbereich nicht gibt.</returns>
    public static bool Switch(AppSettings s, string name)
    {
        EnsureInitialized(s);
        var target = Find(s, name);
        if (target == null) return false;
        if (target.Name.Equals(s.ActiveWorkspace, StringComparison.OrdinalIgnoreCase)) return true;
        Capture(s);
        Load(s, target);
        return true;
    }

    /// <summary>Neuen Arbeitsbereich anlegen – leer oder als Kopie des aktuellen (Profile, Favoriten, Gruppenauswahl).</summary>
    public static Workspace Create(AppSettings s, string name, bool copyCurrent)
    {
        EnsureInitialized(s);
        name = UniqueName(s, string.IsNullOrWhiteSpace(name) ? L.T("New workspace") : name.Trim());
        Capture(s);
        var ws = new Workspace { Name = name };
        if (copyCurrent)
        {
            var current = Active(s);
            ws.Favorites = new List<string>(current.Favorites);
            ws.Profiles = current.Profiles.ToDictionary(kv => kv.Key, kv => kv.Value.Select(p => p.Clone()).ToList(), StringComparer.OrdinalIgnoreCase);
            ws.HiddenGroups = new List<string>(current.HiddenGroups);
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
        bool active = ws.Name.Equals(s.ActiveWorkspace, StringComparison.OrdinalIgnoreCase);
        ws.Name = newName;
        if (active) s.ActiveWorkspace = newName;
        return true;
    }

    /// <summary>Löscht einen Arbeitsbereich (nicht den aktiven und nicht den letzten).</summary>
    public static bool Delete(AppSettings s, string name)
    {
        if (s.Workspaces.Count <= 1 || name.Equals(s.ActiveWorkspace, StringComparison.OrdinalIgnoreCase)) return false;
        return s.Workspaces.RemoveAll(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    private static string UniqueName(AppSettings s, string name)
    {
        var candidate = name;
        for (int i = 2; Find(s, candidate) != null; i++) candidate = $"{name} {i}";
        return candidate;
    }

    /// <summary>
    /// Übernahme aus Versionen bis 0.12: Arbeitsbereiche hatten eigene Ordner, Einträge, eigene Einträge und Gruppen.
    /// Alles wird zusammengeführt (global); je Arbeitsbereich bleiben Profile, Favoriten, Verlauf – und Gruppen, die
    /// nur zu einem anderen Arbeitsbereich gehörten, werden dort ausgeblendet.
    /// </summary>
    internal static void MigrateLegacy(AppSettings s)
    {
        EnsureInitialized(s);
        var active = Active(s);
        // Der aktive Stand steht in den Einstellungen selbst
        active.Folders = s.Folders.Select(f => f.Clone()).ToList();
        active.Entries = s.Entries.Select(e => e.Clone()).ToList();
        active.CustomEntries = s.CustomEntries.Select(c => c.Clone()).ToList();
        active.CategoryOrder = new List<string>(s.CategoryOrder);
        active.CustomOrder = s.CustomOrder.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
        active.Fingerprints = new Dictionary<string, string>(s.Fingerprints);
        active.GroupIcons = new Dictionary<string, string>(s.GroupIcons);
        active.Favorites = new List<string>(s.Favorites);
        active.RecentRuns = s.RecentRuns.ToList();
        active.CollapsedGroups = new List<string>(s.CollapsedGroups);

        if (s.Workspaces.Count(w => w.HasLegacyData) > 1)
        {
            var ordered = new[] { active }.Concat(s.Workspaces.Where(w => !ReferenceEquals(w, active))).ToList();
            var folders = new List<FolderConfig>();
            var entries = new List<EntryConfig>();
            var custom = new List<CustomEntry>();
            var order = new List<string>();
            var icons = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var ws in ordered)
            {
                foreach (var f in ws.Folders.Where(f => !folders.Any(x => x.Path.Equals(f.Path, StringComparison.OrdinalIgnoreCase)))) folders.Add(f.Clone());
                foreach (var c in ws.CustomEntries.Where(c => !custom.Any(x => x.Id == c.Id))) custom.Add(c.Clone());
                foreach (var e in ws.Entries)
                {
                    var g = entries.FirstOrDefault(x => x.Path.Equals(e.Path, StringComparison.OrdinalIgnoreCase));
                    if (g == null) entries.Add(e.Clone());
                    else
                    {
                        if (g.Category.Length == 0) g.Category = e.Category;
                        if (g.DisplayName.Length == 0) g.DisplayName = e.DisplayName;
                        if (g.Description.Length == 0) g.Description = e.Description;
                        if (g.Notes.Length == 0) g.Notes = e.Notes;
                        g.Tags = g.Tags.Concat(e.Tags).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
                    }
                }
                foreach (var c in ws.CategoryOrder.Where(c => !order.Contains(c, StringComparer.CurrentCultureIgnoreCase))) order.Add(c);
                foreach (var kv in ws.GroupIcons) icons.TryAdd(kv.Key, kv.Value);
                foreach (var kv in ws.CustomOrder) s.CustomOrder.TryAdd(kv.Key, kv.Value);
                foreach (var kv in ws.Fingerprints) s.Fingerprints.TryAdd(kv.Key, kv.Value);
                ws.Profiles = ProfilesOf(ws.Entries);
            }
            s.Folders = folders;
            s.CustomEntries = custom;
            s.Entries = entries;
            foreach (var e in s.Entries) e.Profiles = active.Profiles.TryGetValue(e.Path, out var p) ? p.Select(x => x.Clone()).ToList() : new();
            s.CategoryOrder = order;
            s.GroupIcons = new Dictionary<string, string>(icons);
        }
        else
        {
            active.Profiles = ProfilesOf(s.Entries);
        }

        // Ordner werden zu smarten Gruppen; Arbeitsbereiche blenden fremde Gruppen aus
        SmartGroups.EnsureFolderGroups(s);
        if (s.Workspaces.Count > 1)
        {
            foreach (var ws in s.Workspaces)
            {
                if (!ws.HasLegacyData && !ReferenceEquals(ws, active)) continue;
                var own = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                foreach (var f in ws.Folders)
                    if (s.Folders.FirstOrDefault(x => x.Path.Equals(f.Path, StringComparison.OrdinalIgnoreCase)) is { } gf) own.Add(gf.Group);
                foreach (var c in ws.CategoryOrder.Concat(ws.Entries.Select(e => e.Category))) if (c.Length > 0) own.Add(GroupManager.Normalize(c));
                foreach (var top in GroupManager.All(s).Where(g => GroupManager.ParentOf(g) == null))
                    if (!own.Any(o => GroupManager.IsSameOrBelow(o, top))) SetGroupHidden(ws, top, true);
            }
        }
        foreach (var f in s.Folders.Where(f => !f.ShowGroup))
        {
            foreach (var ws in s.Workspaces) SetGroupHidden(ws, f.Group, true);
            f.ShowGroup = true;
        }
        foreach (var ws in s.Workspaces) ws.ClearLegacyData();
    }
}
