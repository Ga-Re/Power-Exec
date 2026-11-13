using PowerExec.Core.Localization;
namespace PowerExec.Core.Config;

/// <summary>
/// Feste Gruppen des Panels („Zuletzt ausgeführt“, „Favoriten“, „Eigene Einträge“): nicht löschbar, aber wie eigene
/// Gruppen auf oberster Ebene verschiebbar und je Arbeitsbereich ein- oder ausblendbar (Schlüssel in
/// <see cref="Workspace.HiddenGroups"/>). Die Position zählt, wie viele Gruppen der obersten Ebene davor stehen.
/// </summary>
public static class FixedGroups
{
    public const string Recent = "@recent", Favorites = "@fav", Custom = "@custom";

    /// <summary>Standardreihenfolge: Zuletzt ausgeführt, Favoriten oben, Eigene Einträge unten.</summary>
    public static readonly IReadOnlyList<string> Keys = new[] { Recent, Favorites, Custom };

    public static bool IsFixed(string? key) => key != null && Keys.Contains(key, StringComparer.OrdinalIgnoreCase);

    public static string Title(string key) => key switch
    {
        Recent => L.T("Recently run"),
        Favorites => L.T("Favorites"),
        Custom => L.T("Custom entries"),
        _ => key
    };

    /// <summary>Symbol (Segoe Fluent Icons) wie im Panel.</summary>
    public static string Glyph(string key) => key switch
    {
        Recent => "",
        Favorites => "",
        Custom => "",
        _ => ""
    };

    public static string Description(string key) => key switch
    {
        Recent => L.T("The most recently launched entries (per workspace)."),
        Favorites => L.T("Entries marked with ☆; Alt+1 … 9 runs them."),
        Custom => L.T("Custom entries that aren't in any group (yet) – only visible if there are any."),
        _ => string.Empty
    };

    /// <summary>Position einer festen Gruppe: Zahl der Gruppen der obersten Ebene davor (sehr groß = ganz unten).</summary>
    public static int PositionOf(AppSettings s, string key) =>
        s.FixedGroupPositions.TryGetValue(key, out var p) ? p : key == Custom ? int.MaxValue : 0;

    /// <summary>Oberste Ebene in Anzeigereihenfolge: Gruppenpfade und feste Schlüssel gemischt.</summary>
    public static List<string> Compose(AppSettings s, IReadOnlyList<string> topLevelGroups)
    {
        var result = new List<string>();
        var fixedByPos = Keys.Select((k, i) => (Key: k, Pos: PositionOf(s, k), Index: i)).OrderBy(x => x.Pos).ThenBy(x => x.Index).ToList();
        int f = 0;
        for (int g = 0; g <= topLevelGroups.Count; g++)
        {
            while (f < fixedByPos.Count && (fixedByPos[f].Pos <= g || g == topLevelGroups.Count)) result.Add(fixedByPos[f++].Key);
            if (g < topLevelGroups.Count) result.Add(topLevelGroups[g]);
        }
        return result;
    }

    /// <summary>Positionen der festen Gruppen aus einer gemischten Reihenfolge übernehmen.</summary>
    public static void SetPositions(AppSettings s, IReadOnlyList<string> topOrder)
    {
        int groups = 0;
        var total = topOrder.Count(k => !IsFixed(k));
        foreach (var k in topOrder)
        {
            if (IsFixed(k)) s.FixedGroupPositions[Keys.First(x => x.Equals(k, StringComparison.OrdinalIgnoreCase))] = groups >= total ? int.MaxValue : groups;
            else groups++;
        }
    }

    /// <summary>
    /// Oberste Ebene umordnen (Ziehen im Panel oder in den Einstellungen): <paramref name="key"/> – feste Gruppe oder
    /// eigene Gruppe – vor bzw. hinter <paramref name="anchor"/>. Eine Untergruppe wird dabei zur Gruppe der obersten Ebene.
    /// </summary>
    /// <returns>Neuer Schlüssel (Pfad ändert sich, wenn eine Untergruppe nach oben wandert) oder null, wenn nichts geht.</returns>
    public static string? MoveTopLevel(AppSettings s, string key, string anchor, bool after)
    {
        static bool Eq(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
        if (Eq(key, anchor)) return null;
        if (!IsFixed(key) && GroupManager.ParentOf(key) != null)
        {
            var root = GroupManager.All(s).LastOrDefault(p => GroupManager.ParentOf(p) == null && !GroupManager.IsSameOrBelow(key, p));
            root ??= GroupManager.All(s).LastOrDefault(p => GroupManager.ParentOf(p) == null);
            if (root == null || GroupManager.MoveGroup(s, key, root, GroupDropPosition.After) is not { } moved) return null;
            key = moved;
        }
        var all = GroupManager.All(s);
        var full = Compose(s, all.Where(p => GroupManager.ParentOf(p) == null).ToList());
        full.RemoveAll(k => Eq(k, key));
        var at = full.FindIndex(k => Eq(k, anchor));
        if (at < 0) return null;
        full.Insert(after ? at + 1 : at, key);
        SetPositions(s, full);
        s.CategoryOrder = full.Where(k => !IsFixed(k))
            .SelectMany(r => all.Where(p => GroupManager.IsSameOrBelow(p, r))).ToList();
        return key;
    }

    /// <summary>
    /// Einmalig: die früheren Schalter „Favoriten anzeigen“ / „Zuletzt ausgeführt anzeigen“ werden zu
    /// ausgeblendeten Gruppen in allen Arbeitsbereichen (die Anzeige steuern jetzt die Arbeitsbereiche).
    /// </summary>
    public static void MigrateToggles(AppSettings s)
    {
        var panel = s.Panels.FirstOrDefault();
        if (panel == null) return;
        foreach (var ws in s.Workspaces)
        {
            if (!panel.ShowFavorites) WorkspaceManager.SetGroupHidden(ws, Favorites, true);
            if (!panel.ShowRecent) WorkspaceManager.SetGroupHidden(ws, Recent, true);
        }
        panel.ShowFavorites = true;
        panel.ShowRecent = true;
    }
}
