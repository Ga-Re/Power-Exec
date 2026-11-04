using PowerExec.Core.Config;
using PowerExec.Core.Diagnostics;

namespace PowerExec.Core.Tests;

public class PerfDatasetTests
{
    private static (PerfDataset Data, AppSettings Settings) Create()
    {
        var data = PerfDataset.Create(Path.Combine(Path.GetTempPath(), "PowerExecPerfTest"));
        var settings = new AppSettings();
        data.ApplyTo(settings, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        return (data, settings);
    }

    [Fact]
    public void Has500EntriesInAboutTwentyGroups()
    {
        var (data, settings) = Create();
        Assert.Equal(500, data.Files.Count + data.CustomEntries.Count);
        // Ordnergruppe + 8 eigene Gruppen (2 davon Untergruppen); dazu kommen im Panel 12 Unterordner
        var groups = GroupManager.All(settings);
        Assert.Equal(9, groups.Count);
        Assert.Contains("Scripte", groups);
        Assert.Contains("Kunde A/AD", groups);
        Assert.DoesNotContain("Scripte 2", groups);
        Assert.Equal(12, data.Files.Select(f => SmartGroups.ImplicitGroup(settings, data.FullPathOf(f), data.Root)).Distinct().Count());
        Assert.Equal(25, settings.Favorites.Count);
        Assert.Equal(120, settings.RunStats.Count);
    }

    [Fact]
    public void IsReproducible()
    {
        var a = Create();
        var b = Create();
        Assert.Equal(a.Data.Files.Select(f => f.RelativePath), b.Data.Files.Select(f => f.RelativePath));
        Assert.Equal(a.Settings.Entries.Select(e => e.Path + string.Join(",", e.Tags)), b.Settings.Entries.Select(e => e.Path + string.Join(",", e.Tags)));
    }

    [Fact]
    public void TypingSequenceEndsEmpty()
    {
        Assert.Equal("disk", PerfDataset.TypingSequence.MaxBy(s => s.Length));
        Assert.Equal(string.Empty, PerfDataset.TypingSequence[^1]);
    }
}
