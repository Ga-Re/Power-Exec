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
        Recent => "Zuletzt ausgeführt",
        Favorites => "Favoriten",
        Custom => "Eigene Einträge",
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
        Recent => "Die zuletzt gestarteten Einträge (je Arbeitsbereich).",
        Favorites => "Mit ☆ markierte Einträge; Alt+1 … 9 startet sie.",
        Custom => "Eigene Einträge, die (noch) in keiner Gruppe liegen – nur sichtbar, wenn es solche gibt.",
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
