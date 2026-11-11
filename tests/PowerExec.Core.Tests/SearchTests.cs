using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Suche: unscharfer Abgleich, Rangfolge, Operatoren (#, typ:, gruppe:, ist:).</summary>
public class SearchTests
{
    [Fact]
    public void FuzzyFindsContiguousMatch()
    {
        var r = FuzzyMatcher.Match("Get-DiskSpace", "disk");
        Assert.NotNull(r);
        Assert.Equal(new[] { new TextRange(4, 4) }, r!.Ranges);
    }

    [Fact]
    public void FuzzyFindsAbbreviations()
    {
        var usr = FuzzyMatcher.Match("Reset-ADUserPassword", "usr");
        Assert.NotNull(usr);
        Assert.Equal(new[] { new TextRange(8, 2), new TextRange(11, 1) }, usr!.Ranges);

        var gds = FuzzyMatcher.Match("Get-DiskSpace", "gds");
        Assert.NotNull(gds);
        Assert.Equal(new[] { new TextRange(0, 1), new TextRange(4, 1), new TextRange(8, 1) }, gds!.Ranges);
    }

    [Fact]
    public void FuzzyToleratesOneTypo()
    {
        var r = FuzzyMatcher.Match("Get-DiskSpace", "dsik");
        Assert.NotNull(r);
        Assert.Equal(new[] { new TextRange(4, 4) }, r!.Ranges);
        Assert.Equal(1, FuzzyMatcher.Distance("disk", "dsik"));
    }

    [Fact]
    public void FuzzyRejectsUnrelated()
    {
        Assert.Null(FuzzyMatcher.Match("Get-DiskSpace", "xyz"));
        Assert.Null(FuzzyMatcher.Match("Get-Disk", "gk")); // zwei Zeichen nur als Anfangsbuchstaben
    }

    [Fact]
    public void ContiguousBeatsAbbreviationBeatsTypo()
    {
        var contiguous = FuzzyMatcher.Match("Restart-Spooler", "spool")!.Score;
        var abbreviation = FuzzyMatcher.Match("Restart-Spooler", "rsp")!.Score;
        Assert.True(contiguous > abbreviation);
    }

    [Fact]
    public void NameWeighsMoreThanDescription()
    {
        var byName = SearchScoring.Score(new (string?, int)[] { ("Get-DiskSpace", 3), ("Zeigt freien Platz", 1) }, new[] { "disk" });
        var byDescription = SearchScoring.Score(new (string?, int)[] { ("Get-Volume", 3), ("Prüft jede Disk", 1) }, new[] { "disk" });
        Assert.NotNull(byName);
        Assert.NotNull(byDescription);
        Assert.True(byName > byDescription);
        Assert.Null(SearchScoring.Score(new (string?, int)[] { ("Get-DiskSpace", 3) }, new[] { "disk", "xyz" }));
    }

    [Fact]
    public void QueryParsesOperators()
    {
        var q = SearchQuery.Parse("typ:ps1 #ad gruppe:\"Kunde A\" ist:fav reset");
        Assert.Equal(new[] { "reset" }, q.Terms);
        Assert.Equal(new[] { "ad" }, q.Tags);
        Assert.Equal(new[] { SearchKind.PowerShell }, q.Kinds);
        Assert.Equal(new[] { "Kunde A" }, q.Groups);
        Assert.True(q.Favorites);
        Assert.Equal(4, q.Operators.Count());
    }

    [Fact]
    public void QueryAcceptsPrefixesAndKeepsUnknownAsText()
    {
        Assert.Equal(new[] { SearchKind.PowerShell }, SearchQuery.Parse("typ:po").Kinds);

        var invalid = SearchQuery.Parse("typ:xyz");
        Assert.False(invalid.HasFilters);
        Assert.Equal(SearchTokenKind.Invalid, invalid.Tokens.Single().Kind);

        var path = SearchQuery.Parse(@"C:\Tools");
        Assert.Equal(new[] { @"C:\Tools" }, path.Terms);
        Assert.False(path.HasFilters);
    }

    [Fact]
    public void QueryRemovesToken()
    {
        const string text = "reset typ:ps1 x";
        var q = SearchQuery.Parse(text);
        var token = q.Operators.Single();
        Assert.Equal("reset x", SearchQuery.Remove(text, token));
    }

    [Fact]
    public void QueryFiltersByKindAndFlags()
    {
        var web = new LaunchItem { FullPath = "custom:1", RootFolder = string.Empty, Type = ItemType.Other, CustomKind = CustomEntryKind.Url };
        var script = new LaunchItem { FullPath = @"C:\s\a.ps1", RootFolder = @"C:\s", Type = ItemType.PowerShell, RequiresAdmin = true };
        Assert.Equal(SearchKind.Web, SearchQuery.KindOf(web));

        var q = SearchQuery.Parse("typ:web");
        Assert.True(q.MatchesFilters(web, "Intranet", false, Array.Empty<string>()));
        Assert.False(q.MatchesFilters(script, "Intranet", false, Array.Empty<string>()));

        var admin = SearchQuery.Parse("ist:admin gruppe:kunde");
        Assert.True(admin.MatchesFilters(script, "Kunde A/AD", false, Array.Empty<string>()));
        Assert.False(admin.MatchesFilters(script, "Intern", false, Array.Empty<string>()));

        var tags = SearchQuery.Parse("#ex");
        Assert.True(tags.MatchesFilters(script, string.Empty, false, new[] { "Exchange" }));
        Assert.False(tags.MatchesFilters(script, string.Empty, false, new[] { "AD" }));
    }
}
