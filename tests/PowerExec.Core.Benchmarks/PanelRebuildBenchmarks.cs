using BenchmarkDotNet.Attributes;
using PowerExec.Core.Config;
using PowerExec.Core.Diagnostics;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Benchmarks;

/// <summary>Gemeinsamer Aufbau: Testbestand mit 500 Einträgen in ~20 Gruppen (siehe <see cref="PerfDataset"/>).</summary>
public abstract class PanelBenchmarkBase
{
    protected AppSettings Settings = null!;
    protected List<LaunchItem> Items = null!;
    protected RebuildMirror Mirror = null!;

    [GlobalSetup]
    public void Setup()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerExecPerf");
        var data = PerfDataset.Create(root);
        Settings = new AppSettings();
        data.ApplyTo(Settings, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        Items = data.CreateItems();
        Mirror = new RebuildMirror(Settings, Items);
    }
}

/// <summary>Ein Rebuild je Suchtext – entspricht einem Tastendruck im Suchfeld.</summary>
[MemoryDiagnoser]
public class PanelRebuildBenchmarks : PanelBenchmarkBase
{
    [Params("", "d", "disk", "#ad", "typ:ps1 user")]
    public string Query { get; set; } = string.Empty;

    [Benchmark(Description = "Rebuild (Datenteil)")]
    public RebuildMirror.Result Rebuild() => Mirror.Run(Query);
}

/// <summary>Das Szenario des Selbsttests: „disk“ tippen und wieder löschen (8 Rebuilds).</summary>
[MemoryDiagnoser]
public class TypingBenchmarks : PanelBenchmarkBase
{
    [Benchmark(Description = "d→disk→leer (8 Rebuilds)")]
    public int TypeAndDelete()
    {
        int n = 0;
        foreach (var text in PerfDataset.TypingSequence) n += Mirror.Run(text).Pool;
        return n;
    }
}

/// <summary>Die Stufen eines Rebuilds einzeln, für den Suchtext „disk“.</summary>
[MemoryDiagnoser]
public class RebuildStageBenchmarks : PanelBenchmarkBase
{
    private SearchQuery _query = null!;
    private List<LaunchItem> _visible = null!;
    private List<LaunchItem> _pool = null!;
    private Dictionary<LaunchItem, int> _scores = null!;
    private List<(string Key, List<LaunchItem> Items)> _groups = null!;

    [GlobalSetup(Targets = new[] { nameof(CollectVisible), nameof(FilterAndScore), nameof(GroupAndOrder), nameof(ItemViewModelData) })]
    public void SetupStages()
    {
        Setup();
        _query = SearchQuery.Parse("disk");
        _visible = Mirror.CollectVisible();
        (_pool, _scores) = Mirror.FilterAndScore(_visible, _query, TypeFilter.All);
        _groups = Mirror.GroupAndOrder(_visible, _pool, _scores, _query, TypeFilter.All);
    }

    [Benchmark(Description = "1 CollectVisible")]
    public int CollectVisible() => Mirror.CollectVisible().Count;

    [Benchmark(Description = "2 Filtern + Bewerten")]
    public int FilterAndScore() => Mirror.FilterAndScore(_visible, _query, TypeFilter.All).Pool.Count;

    [Benchmark(Description = "3 Gruppieren + Sortieren")]
    public int GroupAndOrder() => Mirror.GroupAndOrder(_visible, _pool, _scores, _query, TypeFilter.All).Count;

    [Benchmark(Description = "4 Daten je ItemViewModel")]
    public int ItemViewModelData()
    {
        int n = 0;
        foreach (var (key, items) in _groups) n += Mirror.ItemViewModelData(key, items);
        return n;
    }
}

/// <summary>Einzelne Nachschlag-Funktionen, die je Eintrag mehrfach laufen (Kandidaten für den LookupSnapshot).</summary>
[MemoryDiagnoser]
public class LookupBenchmarks : PanelBenchmarkBase
{
    private string[] _paths = null!;

    [GlobalSetup(Targets = new[] { nameof(FindEntry), nameof(IsFavorite), nameof(Frecency), nameof(EffectiveGroup), nameof(ShowsGroup) })]
    public void SetupLookups()
    {
        Setup();
        _paths = Items.Select(i => i.FullPath).ToArray();
    }

    [Benchmark(Description = "FindEntry × 500")]
    public int FindEntry() { int n = 0; foreach (var p in _paths) if (Settings.FindEntry(p) != null) n++; return n; }

    [Benchmark(Description = "IsFavorite × 500")]
    public int IsFavorite() { int n = 0; foreach (var p in _paths) if (Settings.Favorites.Contains(p, StringComparer.OrdinalIgnoreCase)) n++; return n; }

    [Benchmark(Description = "RunStats.Frecency × 500")]
    public double Frecency() { double s = 0; foreach (var p in _paths) s += RunStats.Frecency(Settings, p); return s; }

    [Benchmark(Description = "EffectiveGroup × 500")]
    public int EffectiveGroup() { int n = 0; foreach (var i in Items) n += SmartGroups.EffectiveGroup(Settings, i.FullPath, i.RootFolder).Length; return n; }

    [Benchmark(Description = "WorkspaceManager.ShowsGroup × 500")]
    public int ShowsGroup() { int n = 0; foreach (var i in Items) if (WorkspaceManager.ShowsGroup(Settings, "Scripte/AD")) n++; return n; }
}
