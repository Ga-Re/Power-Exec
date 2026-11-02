namespace PowerExec.Core.Config;

/// <summary>
/// Eigene Gruppen im Panel. Eine Gruppe ist eine Kategorie: Reihenfolge in <see cref="AppSettings.CategoryOrder"/>,
/// Zuordnung über <see cref="EntryConfig.Category"/>. In der Ordneransicht erscheinen gruppierte Einträge in ihrer Gruppe
/// statt in ihrem Ordner.
/// </summary>
public static class GroupManager
{
    public const string KeyPrefix = "cat:";

    public static string GroupKey(string name) => KeyPrefix + name;

    /// <summary>Alle Gruppen in Anzeigereihenfolge (angelegte zuerst, dann nur über Einträge bekannte).</summary>
    public static IReadOnlyList<string> All(AppSettings s) =>
        s.CategoryOrder.Concat(s.Entries.Select(e => e.Category))
            .Select(c => (c ?? string.Empty).Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static bool Exists(AppSettings s, string name) =>
        All(s).Contains(name.Trim(), StringComparer.CurrentCultureIgnoreCase);

    /// <returns>Name der angelegten Gruppe (bei Namensgleichheit mit Zusatz „2“, „3“ …).</returns>
    public static string Create(AppSettings s, string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "Neue Gruppe" : name.Trim();
        var candidate = name;
        for (int i = 2; Exists(s, candidate); i++) candidate = $"{name} {i}";
        s.CategoryOrder.Add(candidate);
        return candidate;
    }

    public static bool Rename(AppSettings s, string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return false;
        if (!oldName.Equals(newName, StringComparison.CurrentCultureIgnoreCase) && Exists(s, newName)) return false;
        bool found = false;
        for (int i = 0; i < s.CategoryOrder.Count; i++)
            if (s.CategoryOrder[i].Equals(oldName, StringComparison.CurrentCultureIgnoreCase)) { s.CategoryOrder[i] = newName; found = true; }
        foreach (var e in s.Entries.Where(e => oldName.Equals(e.Category?.Trim(), StringComparison.CurrentCultureIgnoreCase)))
        {
            e.Category = newName;
            found = true;
        }
        if (!found) return false;
        RenameKey(s, GroupKey(oldName), GroupKey(newName));
        foreach (var p in s.Panels)
            for (int i = 0; i < p.Categories.Count; i++)
                if (p.Categories[i].Equals(oldName, StringComparison.CurrentCultureIgnoreCase)) p.Categories[i] = newName;
        return true;
    }

    /// <summary>Löscht die Gruppe; ihre Einträge erscheinen wieder in ihrem Ordner bzw. unter „Eigene Einträge“.</summary>
    public static void Delete(AppSettings s, string name)
    {
        s.CategoryOrder.RemoveAll(c => c.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        foreach (var e in s.Entries.Where(e => name.Equals(e.Category?.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            e.Category = string.Empty;
        s.CustomOrder.Remove(GroupKey(name));
        s.CollapsedGroups.RemoveAll(k => k.Equals(GroupKey(name), StringComparison.OrdinalIgnoreCase));
        foreach (var p in s.Panels) p.Categories.RemoveAll(c => c.Equals(name, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Gruppe in der Reihenfolge verschieben (−1 = nach oben, +1 = nach unten).</summary>
    public static void Move(AppSettings s, string name, int delta)
    {
        var all = All(s).ToList();
        var i = all.FindIndex(c => c.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        var j = i + delta;
        if (i < 0 || j < 0 || j >= all.Count) return;
        (all[i], all[j]) = (all[j], all[i]);
        s.CategoryOrder = all;
    }

    /// <summary>Eintrag einer Gruppe zuordnen; leerer Name = aus der Gruppe nehmen.</summary>
    public static void Assign(AppSettings s, string path, string? group)
    {
        group = (group ?? string.Empty).Trim();
        var entry = s.FindEntry(path);
        if (entry == null)
        {
            if (group.Length == 0) return;
            entry = new EntryConfig { Path = path };
            s.Entries.Add(entry);
        }
        entry.Category = group;
        if (group.Length > 0 && !s.CategoryOrder.Contains(group, StringComparer.CurrentCultureIgnoreCase)) s.CategoryOrder.Add(group);
        // Leere Konfiguration nicht aufbewahren
        if (group.Length == 0 && entry.Profiles.Count == 0 && string.IsNullOrWhiteSpace(entry.DisplayName) &&
            string.IsNullOrWhiteSpace(entry.Description) && string.IsNullOrWhiteSpace(entry.Notes))
            s.Entries.Remove(entry);
    }

    public static string GroupOf(AppSettings s, string path) => s.FindEntry(path)?.Category?.Trim() ?? string.Empty;

    private static void RenameKey(AppSettings s, string oldKey, string newKey)
    {
        if (s.CustomOrder.Remove(oldKey, out var order)) s.CustomOrder[newKey] = order;
        for (int i = 0; i < s.CollapsedGroups.Count; i++)
            if (s.CollapsedGroups[i].Equals(oldKey, StringComparison.OrdinalIgnoreCase)) s.CollapsedGroups[i] = newKey;
    }
}

/// <summary>Was aus dem Explorer oder Browser ins Panel gezogen wurde.</summary>
public sealed record DroppedTarget(CustomEntryKind Kind, string Target, string Name);

public static class DropClassifier
{
    /// <summary>
    /// Ordnet einen gezogenen Pfad ein: Ordner → Ordner-Eintrag, .url-Datei → Webseite, sonst Datei.
    /// <paramref name="isDirectory"/> und <paramref name="readText"/> sind austauschbar (Tests).
    /// </summary>
    public static DroppedTarget Classify(string path, Func<string, bool> isDirectory, Func<string, string?> readText)
    {
        path = path.Trim().TrimEnd('\\', '/') is { Length: > 2 } p ? p : path.Trim();
        if (isDirectory(path))
        {
            var name = LastSegment(path);
            return new DroppedTarget(CustomEntryKind.Folder, path, name.Length > 0 ? name : path);
        }
        if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase) && readText(path) is { } text)
        {
            var url = text.Replace("\r", "").Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim();
            if (!string.IsNullOrEmpty(url))
            {
                var file = LastSegment(path);
                return new DroppedTarget(CustomEntryKind.Url, url, file[..^4]);
            }
        }
        return new DroppedTarget(CustomEntryKind.File, path, LastSegment(path));
    }

    /// <summary>Letzter Teil eines Windows- oder UNC-Pfads (unabhängig vom Betriebssystem, auf dem die Tests laufen).</summary>
    private static string LastSegment(string path) =>
        path.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault(p => p.Length > 0) ?? path;

    /// <summary>Eine aus dem Browser gezogene Adresse (http/https), sonst null.</summary>
    public static DroppedTarget? FromUrl(string? text, string? title = null)
    {
        var url = text?.Replace("\r", "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;
        return new DroppedTarget(CustomEntryKind.Url, url, string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim());
    }

    /// <summary>
    /// Fügt gezogene Ziele als eigene Einträge hinzu (Duplikate werden wiederverwendet) und ordnet sie der Gruppe zu.
    /// Dateien, die schon in einem durchsuchten Ordner liegen, werden nur der Gruppe zugeordnet.
    /// </summary>
    /// <returns>Schlüssel (Pfad bzw. „custom:Id“) der betroffenen Einträge.</returns>
    public static IReadOnlyList<string> AddToSettings(AppSettings s, IEnumerable<DroppedTarget> targets, string? group, Func<string, bool> isScanned)
    {
        var keys = new List<string>();
        foreach (var t in targets)
        {
            string key;
            if (t.Kind == CustomEntryKind.File && isScanned(t.Target))
                key = t.Target;
            else
            {
                var existing = s.CustomEntries.FirstOrDefault(c => c.Kind == t.Kind && c.Target.Trim().Equals(t.Target, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = new CustomEntry { Kind = t.Kind, Target = t.Target, Name = t.Kind == CustomEntryKind.File ? string.Empty : t.Name };
                    s.CustomEntries.Add(existing);
                }
                key = existing.Key;
            }
            if (group != null) GroupManager.Assign(s, key, group);
            keys.Add(key);
        }
        return keys;
    }
}
