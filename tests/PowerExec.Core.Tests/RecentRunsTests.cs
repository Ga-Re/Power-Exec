using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Scanning;
using PowerExec.Core.Search;
using System.Drawing;

namespace PowerExec.Core.Tests;

/// <summary>Liste „Zuletzt ausgeführt“.</summary>
public class RecentRunsTests
{
    [Fact]
    public void RecentRunsDeduplicateAndLimit()
    {
        var s = new AppSettings();
        RecentRunList.Add(s, @"C:\a.ps1", "Lokal");
        RecentRunList.Add(s, @"C:\b.ps1", "Standard");
        RecentRunList.Add(s, @"C:\A.ps1", "lokal");
        Assert.Equal(2, s.RecentRuns.Count);
        Assert.Equal(@"C:\A.ps1", s.RecentRuns[0].Path);
        for (int i = 0; i < 30; i++) RecentRunList.Add(s, $@"C:\x{i}.ps1", "Standard");
        Assert.Equal(RecentRunList.MaxStored, s.RecentRuns.Count);
        Assert.Equal("vor 5 Min.", RecentRunList.Ago(DateTime.UtcNow.AddMinutes(-5)));
    }
}
