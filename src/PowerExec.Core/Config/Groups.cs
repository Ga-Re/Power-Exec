namespace PowerExec.Core.Config;

public enum GroupDropPosition { Before, After, Into }

/// <summary>
/// Eigene Gruppen im Panel, auch verschachtelt (Untergruppen). Eine Gruppe wird über ihren Pfad angesprochen,
/// z. B. „Server/Exchange“. Reihenfolge in <see cref="AppSettings.CategoryOrder"/>, Zuordnung über
/// <see cref="EntryConfig.Category"/>. In der Ordneransicht erscheinen gruppierte Einträge in ihrer Gruppe
/// statt in ihrem Ordner.
/// </summary>
public static class GroupManager
{
    public const string KeyPrefix = "cat:";
    public const char Separator = '/';

    public static string GroupKey(string path) => KeyPrefix + path;

    /// <summary>„ Server // Exchange “ → „Server/Exchange“.</summary>
    public static string Normalize(string? path) =>
        string.Join(Separator, (path ?? string.Empty).Split(Separator).Select(p => p.Trim()).Where(p => p.Length > 0));

    public static string? ParentOf(string path)
    {
        var i = path.LastIndexOf(Separator);
        return i < 0 ? null : path[..i];
    }

    public static string LeafOf(string path) => path[(path.LastIndexOf(Separator) + 1)..];

    public static int DepthOf(string path) => path.Count(c => c == Separator);

    /// <summary>Anzeige „Server › Exchange“.</summary>
    public static string Display(string path) => path.Replace(Separator.ToString(), " › ");

    public static bool IsSameOrBelow(string path, string ancestor) =>
        path.Equals(ancestor, StringComparison.CurrentCultureIgnoreCase) ||
        path.StartsWith(ancestor + Separator, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Alle Gruppen (Pfade) in Anzeigereihenfolge; übergeordnete Gruppen stehen immer vor ihren Untergruppen.</summary>
    public static IReadOnlyList<string> All(AppSettings s)
    {
        var list = new List<string>();
        void Add(string p)
        {
            if (list.Contains(p, StringComparer.CurrentCultureIgnoreCase)) return;
            if (ParentOf(p) is { } parent) Add(parent);
            list.Add(p);
        }
        foreach (var c in s.CategoryOrder.Concat(s.Entries.Select(e => e.Category)))
        {
            var n = Normalize(c);
            if (n.Length > 0) Add(n);
        }
        return list;
    }

    public static IReadOnlyList<string> ChildrenOf(AppSettings s, string? parent) =>
        All(s).Where(p => string.Equals(ParentOf(p), parent, StringComparison.CurrentCultureIgnoreCase)).ToList();

    public static bool Exists(AppSettings s, string path) =>
        All(s).Contains(Normalize(path), StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Gruppenname ohne Pfadtrenner.</summary>
    public static string CleanName(string name) => name.Replace(Separator, '-').Trim();

    /// <returns>Pfad der angelegten Gruppe (bei Namensgleichheit mit Zusatz „2“, „3“ …).</returns>
    public static string Create(AppSettings s, string name, string? parent = null)
    {
        name = CleanName(string.IsNullOrWhiteSpace(name) ? "Neue Gruppe" : name);
        parent = string.IsNullOrEmpty(parent) ? null : Normalize(parent);
        string PathOf(string n) => parent == null ? n : parent + Separator + n;
        var candidate = name;
        for (int i = 2; Exists(s, PathOf(candidate)); i++) candidate = $"{name} {i}";
        var path = PathOf(candidate);
        var order = All(s).ToList();
        // Untergruppe ans Ende der Geschwister (hinter deren Untergruppen) einfügen
        int insert = order.Count;
        if (parent != null)
        {
            var last = order.FindLastIndex(p => IsSameOrBelow(p, parent));
            insert = last < 0 ? order.Count : last + 1;
        }
        order.Insert(insert, path);
        s.CategoryOrder = order;
        return path;
    }

    /// <summary>Gruppe umbenennen (nur der letzte Teil des Pfads); Untergruppen wandern mit.</summary>
    public static bool Rename(AppSettings s, string path, string newName)
    {
        path = Normalize(path);
        newName = CleanName(newName);
        if (newName.Length == 0 || !Exists(s, path)) return false;
        var parent = ParentOf(path);
        var newPath = parent == null ? newName : parent + Separator + newName;
        if (!newPath.Equals(path, StringComparison.CurrentCultureIgnoreCase) && Exists(s, newPath)) return false;
        RenamePath(s, path, newPath);
        return true;
    }

    /// <summary>Löscht die Gruppe samt Untergruppen; ihre Einträge erscheinen wieder in ihrem Ordner bzw. unter „Eigene Einträge“.</summary>
    public static void Delete(AppSettings s, string path)
    {
        path = Normalize(path);
        s.CategoryOrder = All(s).Where(c => !IsSameOrBelow(c, path)).ToList();
        foreach (var e in s.Entries.Where(e => IsSameOrBelow(Normalize(e.Category), path)))
            e.Category = string.Empty;
        foreach (var key in s.CustomOrder.Keys.Where(k => k.StartsWith(KeyPrefix) && IsSameOrBelow(k[KeyPrefix.Length..], path)).ToList())
            s.CustomOrder.Remove(key);
        s.CollapsedGroups.RemoveAll(k => k.StartsWith(KeyPrefix) && IsSameOrBelow(k[KeyPrefix.Length..], path));
        foreach (var p in s.Panels) p.Categories.RemoveAll(c => IsSameOrBelow(Normalize(c), path));
    }

    /// <summary>Anzahl der Untergruppen (alle Ebenen).</summary>
    public static int DescendantCount(AppSettings s, string path) =>
        All(s).Count(p => !p.Equals(path, StringComparison.CurrentCultureIgnoreCase) && IsSameOrBelow(p, path));

    /// <summary>Gruppe unter ihren Geschwistern verschieben (−1 = nach oben, +1 = nach unten).</summary>
    public static void Move(AppSettings s, string path, int delta)
    {
        path = Normalize(path);
        var siblings = ChildrenOf(s, ParentOf(path)).ToList();
        var i = siblings.FindIndex(c => c.Equals(path, StringComparison.CurrentCultureIgnoreCase));
        var j = i + delta;
        if (i < 0 || j < 0 || j >= siblings.Count) return;
        MoveGroup(s, path, siblings[j], delta < 0 ? GroupDropPosition.Before : GroupDropPosition.After);
    }

    /// <summary>
    /// Drag &amp; Drop von Gruppen: vor oder hinter eine andere Gruppe (wird dabei deren Geschwister)
    /// oder in sie hinein (wird Untergruppe).
    /// </summary>
    /// <returns>Neuer Pfad der Gruppe oder null, wenn das nicht geht (z. B. in die eigene Untergruppe).</returns>
    public static string? MoveGroup(AppSettings s, string path, string target, GroupDropPosition position)
    {
        path = Normalize(path);
        target = Normalize(target);
        if (!Exists(s, path) || !Exists(s, target) || IsSameOrBelow(target, path)) return null;
        var newParent = position == GroupDropPosition.Into ? target : ParentOf(target);
        var newPath = newParent == null ? LeafOf(path) : newParent + Separator + LeafOf(path);
        if (!newPath.Equals(path, StringComparison.CurrentCultureIgnoreCase))
        {
            if (Exists(s, newPath)) return null; // gleichnamige Gruppe am Ziel
            RenamePath(s, path, newPath);
        }

        // Reihenfolge: Teilbaum herausnehmen und an der Zielstelle einfügen
        var order = All(s).ToList();
        var subtree = order.Where(p => IsSameOrBelow(p, newPath)).ToList();
        order.RemoveAll(p => IsSameOrBelow(p, newPath));
        int index;
        if (position == GroupDropPosition.Before)
            index = order.FindIndex(p => p.Equals(target, StringComparison.CurrentCultureIgnoreCase));
        else
        {
            var last = order.FindLastIndex(p => IsSameOrBelow(p, target));
            index = last + 1;
        }
        if (index < 0) index = order.Count;
        order.InsertRange(index, subtree);
        s.CategoryOrder = order;
        return newPath;
    }

    /// <summary>Eintrag einer Gruppe zuordnen; leerer Name = aus der Gruppe nehmen.</summary>
    public static void Assign(AppSettings s, string path, string? group)
    {
        group = Normalize(group);
        var entry = s.FindEntry(path);
        if (entry == null)
        {
            if (group.Length == 0) return;
            entry = new EntryConfig { Path = path };
            s.Entries.Add(entry);
        }
        entry.Category = group;
        if (group.Length > 0 && !s.CategoryOrder.Contains(group, StringComparer.CurrentCultureIgnoreCase))
            s.CategoryOrder = All(s).ToList();
        // Leere Konfiguration nicht aufbewahren
        if (group.Length == 0 && entry.Profiles.Count == 0 && string.IsNullOrWhiteSpace(entry.DisplayName) &&
            string.IsNullOrWhiteSpace(entry.Description) && string.IsNullOrWhiteSpace(entry.Notes))
            s.Entries.Remove(entry);
    }

    public static string GroupOf(AppSettings s, string path) => Normalize(s.FindEntry(path)?.Category);

    /// <summary>Benennt einen Gruppenpfad samt Untergruppen um (Zuordnungen, Reihenfolge, Einklappzustand, Panel-Filter).</summary>
    private static void RenamePath(AppSettings s, string oldPath, string newPath)
    {
        string Map(string p) => IsSameOrBelow(p, oldPath) ? newPath + p[oldPath.Length..] : p;
        s.CategoryOrder = All(s).Select(Map).ToList();
        foreach (var e in s.Entries)
        {
            var c = Normalize(e.Category);
            if (c.Length > 0 && IsSameOrBelow(c, oldPath)) e.Category = Map(c);
        }
        foreach (var key in s.CustomOrder.Keys.Where(k => k.StartsWith(KeyPrefix) && IsSameOrBelow(k[KeyPrefix.Length..], oldPath)).ToList())
        {
            s.CustomOrder.Remove(key, out var order);
            s.CustomOrder[KeyPrefix + Map(key[KeyPrefix.Length..])] = order!;
        }
        for (int i = 0; i < s.CollapsedGroups.Count; i++)
            if (s.CollapsedGroups[i].StartsWith(KeyPrefix) && IsSameOrBelow(s.CollapsedGroups[i][KeyPrefix.Length..], oldPath))
                s.CollapsedGroups[i] = KeyPrefix + Map(s.CollapsedGroups[i][KeyPrefix.Length..]);
        foreach (var p in s.Panels)
            for (int i = 0; i < p.Categories.Count; i++)
                if (IsSameOrBelow(Normalize(p.Categories[i]), oldPath)) p.Categories[i] = Map(Normalize(p.Categories[i]));
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
