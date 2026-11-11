using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Team-Kataloge: Änderungen übernehmen.</summary>
public class TeamCatalogTests
{
    [Fact]
    public void TeamCatalogAppliesUpdatesAndRemoves()
    {
        var publisher = new AppSettings();
        var wiki = new CustomEntry { Id = "w1", Kind = CustomEntryKind.Url, Name = "Wiki", Target = "https://wiki" };
        var ts = new CustomEntry { Id = "t1", Kind = CustomEntryKind.Rdp, Name = "TS01", Target = "ts01" };
        publisher.CustomEntries.AddRange(new[] { wiki, ts });
        var file = EntryShare.Parse(EntryShare.Export(publisher, publisher.CustomEntries));

        var s = new AppSettings();
        var sub = new CatalogSubscription { Id = "abc", Name = "IT-Team", Path = @"\\fs01\it\katalog.pxentries.json" };
        s.Catalogs.Add(sub);
        var first = TeamCatalogs.Apply(s, sub, file);
        Assert.Equal(2, first.Added.Count);
        Assert.All(TeamCatalogs.EntriesOf(s, sub), e => Assert.True(e.IsFromCatalog));
        Assert.Equal("IT-Team", GroupManager.GroupOf(s, "custom:cat-abc-w1"));
        Assert.True(TeamCatalogs.Diff(s, sub, file).IsEmpty);

        // Herausgeber ändert einen, entfernt einen, fügt einen hinzu
        wiki.Target = "https://wiki.neu";
        publisher.CustomEntries.Remove(ts);
        publisher.CustomEntries.Add(new CustomEntry { Id = "n1", Kind = CustomEntryKind.Folder, Name = "Ablage", Target = @"\\fs01\ablage" });
        var file2 = EntryShare.Parse(EntryShare.Export(publisher, publisher.CustomEntries));
        var diff = TeamCatalogs.Diff(s, sub, file2);
        Assert.Equal("1 neu, 1 geändert, 1 entfernt", diff.Summary);
        TeamCatalogs.Apply(s, sub, file2);
        Assert.Equal(2, TeamCatalogs.EntriesOf(s, sub).Count());
        Assert.Equal("https://wiki.neu", s.CustomEntries.Single(c => c.Id == "cat-abc-w1").Target);

        var copy = s.CustomEntries.Single(c => c.Id == "cat-abc-w1").Duplicate();
        Assert.False(copy.IsFromCatalog); // eigene Kopie ist änderbar

        TeamCatalogs.Unsubscribe(s, sub);
        Assert.Empty(s.Catalogs);
        Assert.DoesNotContain(s.CustomEntries, c => c.IsFromCatalog);
    }
}
