namespace PowerExec.Core.Config;

/// <summary>
/// Smarte Gruppen: Eine Gruppe kann einen Ordner haben (<see cref="FolderConfig.Group"/>). Dessen Scripte und
/// Programme erscheinen automatisch in der Gruppe – auf Wunsch mit Unterordnern als Untergruppen. Einträge, die
/// man in eine andere Gruppe zieht, erscheinen nur noch dort. Zusätzlich kann die Gruppe eigene Einträge enthalten.
/// </summary>
public static class SmartGroups
{
    /// <summary>Gruppe für eigene Einträge ohne Gruppe.</summary>
    public const string CustomFallbackKey = "custom";

    public static FolderConfig? FolderOf(AppSettings s, string groupPath) =>
        s.Folders.FirstOrDefault(f => f.Group.Equals(groupPath, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Jeder Ordner bekommt eine Gruppe (Name = Ordnername bzw. Anzeigename, eindeutig).</summary>
    public static void EnsureFolderGroups(AppSettings s)
    {
        foreach (var f in s.Folders)
        {
            f.Group = GroupManager.Normalize(f.Group);
            if (f.Group.Length > 0)
            {
                if (!s.CategoryOrder.Contains(f.Group, StringComparer.CurrentCultureIgnoreCase)) s.CategoryOrder = GroupManager.All(s).ToList();
                continue;
            }
            var name = GroupManager.CleanName(LeafName(f.Path));
            if (name.Length == 0) name = "Ordner";
            var candidate = name;
            for (int i = 2; GroupManager.Exists(s, candidate) || s.Folders.Any(o => !ReferenceEquals(o, f) && o.Group.Equals(candidate, StringComparison.CurrentCultureIgnoreCase)); i++)
                candidate = $"{name} {i}";
            f.Group = candidate;
            s.CategoryOrder.Add(candidate);
        }
    }

    /// <summary>Neue smarte Gruppe für einen Ordner anlegen.</summary>
    public static FolderConfig Add(AppSettings s, string folderPath, string? name = null, string? parent = null)
    {
        var folder = new FolderConfig { Path = folderPath.Trim() };
        var path = GroupManager.Create(s, string.IsNullOrWhiteSpace(name) ? LeafName(folderPath) : name, parent);
        folder.Group = path;
        s.Folders.Add(folder);
        return folder;
    }

    /// <summary>
    /// Gruppe eines Eintrags: ausdrücklich zugeordnete Gruppe, sonst die smarte Gruppe seines Ordners
    /// (bei „Unterordner als Untergruppen“ samt Unterordnerpfad), sonst leer (eigene Einträge ohne Gruppe).
    /// </summary>
    public static string EffectiveGroup(AppSettings s, string itemPath, string rootFolder)
    {
        var explicitGroup = GroupManager.GroupOf(s, itemPath);
        if (explicitGroup.Length > 0) return explicitGroup;
        var folder = s.Folders.FirstOrDefault(f => f.Path.Equals(rootFolder, StringComparison.OrdinalIgnoreCase));
        if (folder == null || folder.Group.Length == 0) return string.Empty;
        if (!folder.SubfoldersAsGroups || !folder.Recursive) return folder.Group;
        var dir = RelativeDirectory(folder.Path, itemPath);
        return dir.Length == 0 ? folder.Group : folder.Group + GroupManager.Separator + dir;
    }

    /// <summary>Unterordner relativ zum Ordner, mit „/“ getrennt („Tools/AD“), leer = direkt im Ordner.</summary>
    public static string RelativeDirectory(string root, string itemPath)
    {
        var r = root.TrimEnd('\\', '/');
        if (!itemPath.StartsWith(r, StringComparison.OrdinalIgnoreCase) || itemPath.Length <= r.Length + 1) return string.Empty;
        var rest = itemPath[(r.Length + 1)..].Replace('\\', '/');
        var i = rest.LastIndexOf('/');
        return i <= 0 ? string.Empty : GroupManager.Normalize(rest[..i]);
    }

    private static string LeafName(string path) =>
        path.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault(x => x.Length > 0) ?? path;
}
