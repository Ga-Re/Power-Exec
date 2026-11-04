using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Search;

namespace PowerExec.Core.Benchmarks;

/// <summary>
/// Nachbildung des Datenteils von <c>PanelViewModel.Rebuild()</c> (Stand 0.16.2) ohne WPF:
/// sichtbare Einträge sammeln, filtern und bewerten, gruppieren und sortieren sowie die Daten,
/// die der Konstruktor von <c>ItemViewModel</c> je Eintrag ermittelt (Eintrag, Profile, Profilmenü).
/// Dieselben Aufrufe in derselben Reihenfolge wie im Panel – damit die Messung zeigt, was der Tipp-Pfad heute kostet.
/// Nicht enthalten (nur im Selbsttest messbar): RelayCommands, Symbole, WPF-Container und Layout.
/// </summary>
public sealed class RebuildMirror
{
    private const int BestHitsCount = 5;
    private readonly AppSettings _settings;
    private readonly IReadOnlyList<LaunchItem> _items;

    public RebuildMirror(AppSettings settings, IReadOnlyList<LaunchItem> items)
    {
        _settings = settings;
        _items = items;
    }

    public readonly record struct Result(int Visible, int Pool, int Groups, int ItemViewModels);

    /// <summary>Kompletter Datenteil eines Rebuilds für einen Suchtext.</summary>
    public Result Run(string searchText, TypeFilter filter = TypeFilter.All)
    {
        var query = SearchQuery.Parse(searchText);
        var visible = CollectVisible();
        var (pool, scores) = FilterAndScore(visible, query, filter);
        var groups = GroupAndOrder(visible, pool, scores, query, filter);
        int vms = 0;
        foreach (var (key, items) in groups) vms += ItemViewModelData(key, items);
        return new Result(visible.Count, pool.Count, groups.Count, vms);
    }

    // ---- Stufe 1: CollectVisible --------------------------------------------

    private string GroupOfItem(LaunchItem i) => SmartGroups.EffectiveGroup(_settings, i.FullPath, i.RootFolder);

    public List<LaunchItem> CollectVisible()
    {
        var settings = _settings;
        var folderByPath = settings.Folders.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var visible = new List<LaunchItem>();
        bool CategoryOk(LaunchItem i) => WorkspaceManager.ShowsGroup(settings, GroupOfItem(i));
        foreach (var i in _items)
        {
            if (!folderByPath.TryGetValue(i.RootFolder, out var folder)) continue;
            var state = FolderVisibility.State(folder, FolderVisibility.RelativePath(folder, i.FullPath));
            if (state == EntryVisibility.Visible && CategoryOk(i)) visible.Add(i);
        }
        var seen = new HashSet<string>(visible.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
        foreach (var c in settings.CustomEntries)
        {
            if (c.Kind == CustomEntryKind.Workflow ? c.Steps.Count == 0 : c.Target.Trim().Length == 0 && (c.Kind != CustomEntryKind.Snippet || c.Code.Trim().Length == 0)) continue;
            var li = DescribeCustom(c);
            if (!seen.Add(li.FullPath)) continue;
            if (CategoryOk(li)) visible.Add(li);
        }
        return visible;
    }

    /// <summary>Wie <c>AppController.DescribeCustom</c>: bei PowerShell-Snippets wird je Aufruf die Datei geprüft.</summary>
    private static LaunchItem DescribeCustom(CustomEntry entry)
    {
        var item = entry.ToLaunchItem();
        if (entry.Kind == CustomEntryKind.Snippet && entry.Language == SnippetLanguage.PowerShell)
            _ = File.Exists(entry.SnippetPath);
        return item;
    }

    // ---- Stufe 2: Filtern und Bewerten --------------------------------------

    private bool IsFavorite(string path) => _settings.Favorites.Contains(path, StringComparer.OrdinalIgnoreCase);

    private int? ScoreItem(LaunchItem i, SearchQuery query)
    {
        var e = _settings.FindEntry(i.FullPath);
        var fields = new List<(string?, int)>
        {
            (i.DisplayName, 3), (e?.DisplayName, 3), (i.FileName, 2), (e == null ? null : string.Join(' ', e.Tags), 2),
            (i.Synopsis, 1), (i.Description, 1), (e?.Description, 1), (e?.Notes, 1), (e?.Category, 1), (i.Source?.Code, 1)
        };
        return SearchScoring.Score(fields, query.Terms);
    }

    private bool FilterItem(LaunchItem i, SearchQuery query, TypeFilter filter)
    {
        var fav = IsFavorite(i.FullPath);
        return ItemFilter.MatchesType(i, filter, fav) &&
               query.MatchesFilters(i, GroupOfItem(i), fav, Tags.Of(_settings, i.FullPath), () => false);
    }

    public (List<LaunchItem> Pool, Dictionary<LaunchItem, int> Scores) FilterAndScore(List<LaunchItem> visible, SearchQuery query, TypeFilter filter)
    {
        var scores = new Dictionary<LaunchItem, int>(ReferenceEqualityComparer.Instance);
        var pool = new List<LaunchItem>();
        foreach (var i in visible)
        {
            if (!FilterItem(i, query, filter)) continue;
            if (query.HasTerms)
            {
                if (ScoreItem(i, query) is not { } score) continue;
                scores[i] = score + SearchScoring.FrecencyBonus(RunStats.Frecency(_settings, i.FullPath));
            }
            pool.Add(i);
        }
        return (pool, scores);
    }

    // ---- Stufe 3: Gruppieren und Sortieren ----------------------------------

    public List<(string Key, List<LaunchItem> Items)> GroupAndOrder(List<LaunchItem> visible, List<LaunchItem> pool,
        Dictionary<LaunchItem, int> scores, SearchQuery query, TypeFilter filter)
    {
        var settings = _settings;
        bool searching = !query.IsEmpty;
        IEnumerable<LaunchItem> Order(IEnumerable<LaunchItem> items) => query.HasTerms
            ? items.OrderByDescending(i => scores.GetValueOrDefault(i)).ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            : items.OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase);
        List<LaunchItem> Arrange(string key, IEnumerable<LaunchItem> items)
        {
            var ordered = Order(items).ToList();
            return searching ? ordered : ItemOrdering.Apply(ordered, i => i.FullPath, settings.CustomOrder.GetValueOrDefault(key));
        }

        var groups = new List<(string, List<LaunchItem>)>();
        if (query.HasTerms && pool.Count > BestHitsCount) groups.Add(("best", Order(pool).Take(BestHitsCount).ToList()));
        if (!searching && filter == TypeFilter.All)
        {
            var favs = pool.Where(i => IsFavorite(i.FullPath)).ToList();
            if (favs.Count > 0) groups.Add(("fav", Arrange("fav", favs)));
        }

        var byGroup = pool.GroupBy(GroupOfItem, StringComparer.CurrentCultureIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.CurrentCultureIgnoreCase);
        var paths = GroupManager.All(settings).ToList();
        var virtualPaths = new List<string>();
        foreach (var g in byGroup.Keys.Where(k => k.Length > 0))
            for (var p = g; p != null; p = GroupManager.ParentOf(p))
                if (!paths.Contains(p, StringComparer.CurrentCultureIgnoreCase) && !virtualPaths.Contains(p, StringComparer.CurrentCultureIgnoreCase)) virtualPaths.Add(p);
        paths.AddRange(virtualPaths.OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase));

        foreach (var path in paths)
        {
            if (!WorkspaceManager.ShowsGroup(settings, path)) continue;
            var items = byGroup.GetValueOrDefault(path) ?? new List<LaunchItem>();
            // FolderInfo: smarte Gruppe bzw. Unterordner einer smarten Gruppe
            if (SmartGroups.FolderOf(settings, path) == null)
                for (var parent = GroupManager.ParentOf(path); parent != null; parent = GroupManager.ParentOf(parent))
                    if (SmartGroups.FolderOf(settings, parent) is { SubfoldersAsGroups: true }) break;
            groups.Add((GroupManager.GroupKey(path), Arrange(GroupManager.GroupKey(path), items)));
        }
        var rest = byGroup.GetValueOrDefault(string.Empty);
        if (rest is { Count: > 0 }) groups.Add((SmartGroups.CustomFallbackKey, Arrange(SmartGroups.CustomFallbackKey, rest)));

        // Alt+1 … 9: Favoriten unabhängig von Suche und Filter
        _ = Arrange("fav", visible.Where(i => IsFavorite(i.FullPath))).Take(9).ToList();
        return groups;
    }

    // ---- Stufe 4: Daten je ItemViewModel ------------------------------------

    /// <summary>Was der Konstruktor von ItemViewModel je Eintrag nachschlägt (ohne Commands und Symbole).</summary>
    public int ItemViewModelData(string groupKey, List<LaunchItem> items)
    {
        int n = 0;
        foreach (var item in items)
        {
            var key = groupKey + "|" + item.FullPath;
            var entry = _settings.FindEntry(item.FullPath);
            var profiles = ProfileResolver.GetProfiles(_settings, item);
            var menu = profiles.Select(p => (p.Name, ProfileResolver.Summary(p, item))).ToList();
            if (key.Length > 0 && menu.Count > 0 && (entry == null || entry.Path.Length > 0)) n++;
        }
        return n;
    }
}
