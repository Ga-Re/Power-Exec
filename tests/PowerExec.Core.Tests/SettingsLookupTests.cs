using PowerExec.Core.Config;
using PowerExec.Core.Diagnostics;

namespace PowerExec.Core.Tests;

public class SettingsLookupTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static (AppSettings Settings, List<string> Paths, List<string> Roots) Create()
    {
        var data = PerfDataset.Create(Path.Combine(Path.GetTempPath(), "PowerExecLookupTest"));
        var s = new AppSettings();
        data.ApplyTo(s, Now);
        var items = data.CreateItems();
        var paths = items.Select(i => i.FullPath).Concat(data.CustomEntries.Select(c => c.Key)).ToList();
        var roots = items.Select(i => i.RootFolder).Concat(data.CustomEntries.Select(_ => PanelConfig.CustomEntriesKey)).ToList();
        // Abweichende Schreibweise und unbekannte Pfade gehören dazu
        paths.Add(paths[3].ToUpperInvariant()); roots.Add(roots[3]);
        paths.Add(@"C:\gibt\es\nicht.ps1"); roots.Add(@"C:\gibt");
        return (s, paths, roots);
    }

    [Fact]
    public void MatchesTheSingleFunctionsForEveryEntry()
    {
        var (s, paths, roots) = Create();
        var lookup = new SettingsLookup(s, Now);
        for (int i = 0; i < paths.Count; i++)
        {
            var p = paths[i];
            Assert.Same(s.FindEntry(p), lookup.Entry(p));
            Assert.Equal(s.Favorites.Contains(p, StringComparer.OrdinalIgnoreCase), lookup.IsFavorite(p));
            Assert.Equal(Tags.Of(s, p), lookup.TagsOf(p));
            Assert.Equal(RunStats.Frecency(s, p, Now), lookup.Frecency(p), 10);
            Assert.Equal(SmartGroups.EffectiveGroup(s, p, roots[i]), lookup.EffectiveGroup(p, roots[i]));
            // zweiter Aufruf aus dem Zwischenspeicher
            Assert.Equal(SmartGroups.EffectiveGroup(s, p, roots[i]), lookup.EffectiveGroup(p, roots[i]));
        }
        foreach (var g in GroupManager.All(s).Append(string.Empty).Append("Scripte/AD"))
            Assert.Equal(WorkspaceManager.ShowsGroup(s, g), lookup.ShowsGroup(g));
    }

    [Fact]
    public void HiddenGroupsInTheWorkspaceAreRespected()
    {
        var (s, _, _) = Create();
        WorkspaceManager.SetGroupHidden(WorkspaceManager.Active(s), "Kunde A", true);
        var lookup = new SettingsLookup(s, Now);
        Assert.False(lookup.ShowsGroup("Kunde A"));
        Assert.False(lookup.ShowsGroup("Kunde A/AD"));
        Assert.True(lookup.ShowsGroup("Kunde B"));
    }

    [Fact]
    public void ExactRunStatsKeyWinsOverOtherCasing()
    {
        var s = new AppSettings();
        s.RunStats["C:\\a.ps1"] = new RunStat { Score = 1, LastUtc = Now };
        s.RunStats["c:\\A.PS1"] = new RunStat { Score = 5, LastUtc = Now };
        var lookup = new SettingsLookup(s, Now);
        Assert.Equal(RunStats.Frecency(s, "c:\\A.PS1", Now), lookup.Frecency("c:\\A.PS1"), 10);
        Assert.Equal(RunStats.Frecency(s, "C:\\A.ps1", Now), lookup.Frecency("C:\\A.ps1"), 10);
    }
}
