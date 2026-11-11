using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Häufigkeit und Aktualität der Starts für die Rangfolge.</summary>
public class RunStatsTests
{
    [Fact]
    public void RunStatsDecayAndMove()
    {
        var s = new AppSettings();
        var t0 = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        RunStats.Record(s, @"C:\a.ps1", t0);
        RunStats.Record(s, @"C:\A.ps1", t0); // gleicher Pfad, andere Schreibweise
        Assert.Single(s.RunStats);
        Assert.Equal(2, RunStats.Frecency(s, @"C:\a.ps1", t0), 3);
        Assert.Equal(1, RunStats.Frecency(s, @"C:\a.ps1", t0.AddDays(RunStats.HalfLifeDays)), 3);

        PathMigration.Apply(s, new PathMove(@"C:\a.ps1", @"C:\b.ps1"));
        Assert.Equal(0.0, RunStats.Frecency(s, @"C:\a.ps1", t0), 3);
        Assert.Equal(2, RunStats.Frecency(s, @"C:\b.ps1", t0), 3);

        Assert.Equal(0, SearchScoring.FrecencyBonus(0));
        Assert.True(SearchScoring.FrecencyBonus(10) > SearchScoring.FrecencyBonus(1));
    }
}
