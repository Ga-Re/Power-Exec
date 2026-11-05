namespace PowerExec.Core.Config;

/// <summary>
/// Momentaufnahme der Einstellungen für einen Rebuild des Panels: Nachschlagen in O(1) statt linearer Suche je Eintrag
/// (<see cref="AppSettings.FindEntry"/>, Favoriten, Startstatistik, Gruppe, Sichtbarkeit im Arbeitsbereich).
/// Liefert dieselben Ergebnisse wie die einzelnen Funktionen. Gilt nur, solange sich die Einstellungen nicht ändern –
/// deshalb je Rebuild neu anlegen und nicht aufbewahren.
/// </summary>
public sealed class SettingsLookup
{
    private readonly AppSettings _s;
    private readonly Dictionary<string, EntryConfig> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _favorites;
    private readonly Dictionary<string, RunStat> _runStats = new(StringComparer.OrdinalIgnoreCase);
    // Exakt nach Pfad: die Untergruppe ergibt sich aus der Schreibweise des Pfads
    private readonly Dictionary<string, string> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _showsGroup = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Workspace _workspace;

    public SettingsLookup(AppSettings settings, DateTime? nowUtc = null)
    {
        _s = settings;
        NowUtc = nowUtc ?? DateTime.UtcNow;
        // Erster Treffer gewinnt – wie FirstOrDefault in den einzelnen Funktionen
        foreach (var e in settings.Entries) _entries.TryAdd(e.Path, e);
        _favorites = new HashSet<string>(settings.Favorites, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, stat) in settings.RunStats) _runStats.TryAdd(key, stat);
        _workspace = WorkspaceManager.Active(settings);
    }

    public DateTime NowUtc { get; }

    public EntryConfig? Entry(string path) => _entries.GetValueOrDefault(path);

    public bool IsFavorite(string path) => _favorites.Contains(path);

    public IReadOnlyList<string> TagsOf(string path) => Entry(path)?.Tags ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>Wie <see cref="RunStats.Frecency"/>: genauer Schlüssel zuerst, sonst ohne Groß-/Kleinschreibung.</summary>
    public double Frecency(string path)
    {
        if (!_s.RunStats.TryGetValue(path, out var stat) && !_runStats.TryGetValue(path, out stat)) return 0;
        return RunStats.ValueOf(stat, NowUtc);
    }

    /// <summary>Wie <see cref="SmartGroups.EffectiveGroup"/> (je Pfad einmal berechnet).</summary>
    public string EffectiveGroup(string itemPath, string rootFolder)
    {
        if (_groups.TryGetValue(itemPath, out var g)) return g;
        var explicitGroup = GroupManager.Normalize(Entry(itemPath)?.Category);
        g = explicitGroup.Length > 0 ? explicitGroup : SmartGroups.ImplicitGroup(_s, itemPath, rootFolder);
        _groups[itemPath] = g;
        return g;
    }

    /// <summary>Wie <see cref="WorkspaceManager.ShowsGroup"/> (je Gruppe einmal berechnet).</summary>
    public bool ShowsGroup(string groupPath)
    {
        if (groupPath.Length == 0) return true;
        if (_showsGroup.TryGetValue(groupPath, out var shows)) return shows;
        shows = WorkspaceManager.ShowsGroupIn(_workspace, groupPath);
        _showsGroup[groupPath] = shows;
        return shows;
    }
}
