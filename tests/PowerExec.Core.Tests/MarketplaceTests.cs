using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

/// <summary>Marktplatz: Adressen, Index, Installation, Updates – und die mitgelieferten Dateien im Repo.</summary>
[Collection("Placeholders")]
public class MarketplaceTests
{
    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PowerExec.sln"))) return Path.Combine(dir.FullName, relative);
        throw new FileNotFoundException(relative);
    }

    [Theory]
    [InlineData("https://github.com/Ga-Re/Power-Exec/tree/main/marketplace", "https://raw.githubusercontent.com/Ga-Re/Power-Exec/main/marketplace/index.json")]
    [InlineData("https://github.com/firma/katalog", "https://raw.githubusercontent.com/firma/katalog/main/index.json")]
    [InlineData("https://github.com/firma/katalog.git", "https://raw.githubusercontent.com/firma/katalog/main/index.json")]
    [InlineData("https://github.com/firma/katalog/tree/dev/px", "https://raw.githubusercontent.com/firma/katalog/dev/px/index.json")]
    [InlineData("https://gitlab.firma.local/it/px-katalog/-/tree/main/markt", "https://gitlab.firma.local/it/px-katalog/-/raw/main/markt/index.json")]
    [InlineData("https://gitlab.com/gruppe/projekt", "https://gitlab.com/gruppe/projekt/-/raw/main/index.json")]
    [InlineData("https://intranet.firma.local/px/index.json", "https://intranet.firma.local/px/index.json")]
    public void RepoUrlsBecomeIndexUrls(string source, string expected) => Assert.Equal(expected, Marketplace.IndexUri(source).ToString());

    [Fact]
    public void FilesResolveRelativeToIndex()
    {
        var index = Marketplace.IndexUri(Marketplace.DefaultSource);
        Assert.Equal("https://raw.githubusercontent.com/Ga-Re/Power-Exec/main/samples/New-Password.ps1",
            Marketplace.FileUri(index, "../samples/New-Password.ps1").ToString());
        Assert.Throws<ArgumentException>(() => Marketplace.IndexUri("kein link"));
    }

    [Fact]
    public void BundledMarketplaceIsComplete()
    {
        var index = Marketplace.ParseIndex(File.ReadAllText(RepoFile("marketplace/index.json")));
        Assert.Contains(index.Items, i => i.Type == MarketplaceItemType.Package && i.Id == "matrix42" && i.Variables.Any(v => v.Name == "Matrix42Url"));
        Assert.Equal(8, index.Items.Count(i => i.Type == MarketplaceItemType.Script));
        Assert.True(index.Items.Count(i => i.Type == MarketplaceItemType.Design) >= 3);
        Assert.Equal(index.Items.Count, index.Items.Select(i => i.Id).Distinct().Count());
        foreach (var item in index.Items)
        {
            var path = Path.GetFullPath(Path.Combine(RepoFile("marketplace"), item.File));
            Assert.True(File.Exists(path), item.File);
            var payload = File.ReadAllText(path);
            Assert.NotEmpty(Marketplace.Preview(item, payload));
            if (item.Type is MarketplaceItemType.Entry or MarketplaceItemType.Package)
                foreach (var e in EntryShare.Parse(payload).Entries)
                {
                    Assert.Empty(EntryValidation.Check(e.Entry).Where(x => x.Field == EntryField.Target));
                    // jede benutzte {{$Variable}} ist eingebaut oder wird vom Element mitgebracht
                    foreach (var v in Placeholders.VariableNames(e.Entry.Target, e.Entry.Code))
                        Assert.True(Variables.IsBuiltin(v) || item.Variables.Any(x => x.Name == Variables.Normalize(v)), $"{item.Id}: {v}");
                }
        }
    }

    [Fact]
    public void InstallsPackageWithVariablesAndDetectsUpdates()
    {
        var s = new AppSettings();
        var index = Marketplace.ParseIndex(File.ReadAllText(RepoFile("marketplace/index.json")));
        var m42 = index.Items.Single(i => i.Id == "matrix42");
        var payload = File.ReadAllText(RepoFile("marketplace/packages/matrix42.json"));

        Assert.Single(Marketplace.MissingVariables(s, m42));
        s.Variables.Add(new UserVariable { Name = "Matrix42Url", Value = "https://m42.firma.local" });
        Assert.Empty(Marketplace.MissingVariables(s, m42));

        var msg = Marketplace.Install(s, Marketplace.DefaultSource, m42, payload, Path.GetTempPath());
        Assert.Contains("2 Einträge neu", msg);
        Assert.True(GroupManager.Exists(s, "Matrix42"));
        var ticket = s.CustomEntries.Single(e => e.Id == "mk-m42-ticket");
        Assert.Equal("Matrix42", s.FindEntry(ticket.Key)!.Category);
        Assert.False(Marketplace.HasUpdate(s, Marketplace.DefaultSource, m42));

        // Variable vor dem ? bleibt unverändert, Ticketnummer wird eingesetzt
        Placeholders.Resolver = n => Variables.Find(s, n)?.Value;
        try
        {
            Assert.Equal("https://m42.firma.local/m42_workspace/index.html#/service/ticket/4711",
                ticket.WithValues(new Dictionary<string, string> { ["Ticketnummer"] = "4711" }).Target);
        }
        finally { Placeholders.Resolver = null; }

        // neue Version im Marktplatz: Update, Einträge werden ersetzt statt verdoppelt
        m42.Version = "2";
        Assert.True(Marketplace.HasUpdate(s, Marketplace.DefaultSource, m42));
        Assert.Contains("2 aktualisiert", Marketplace.Install(s, Marketplace.DefaultSource, m42, payload, Path.GetTempPath()));
        Assert.Equal(2, s.CustomEntries.Count);
        Assert.Single(s.MarketplaceInstalls);
        Assert.False(Marketplace.HasUpdate(s, Marketplace.DefaultSource, m42));
    }

    [Fact]
    public void InstallsDesignAndScript()
    {
        var s = new AppSettings();
        var index = Marketplace.ParseIndex(File.ReadAllText(RepoFile("marketplace/index.json")));
        var design = index.Items.First(i => i.Type == MarketplaceItemType.Design);
        Marketplace.Install(s, Marketplace.DefaultSource, design, File.ReadAllText(Path.Combine(RepoFile("marketplace"), design.File)), Path.GetTempPath());
        var d = Assert.Single(s.CustomDesigns);
        Assert.True(d.IsCustom);
        Assert.Equal("market:" + design.Id, d.Key);

        var script = index.Items.First(i => i.Type == MarketplaceItemType.Script);
        var folder = Path.Combine(Path.GetTempPath(), "px-market-" + Guid.NewGuid().ToString("N"));
        try
        {
            Marketplace.Install(s, Marketplace.DefaultSource, script, "Write-Host 'hallo'", folder);
            var installed = Marketplace.FindInstall(s, Marketplace.DefaultSource, script.Id)!;
            Assert.True(File.Exists(installed.ScriptPath));
            Assert.Equal(".ps1", Path.GetExtension(installed.ScriptPath));
            Assert.Contains(s.Folders, f => f.Path == folder);
            Marketplace.Install(s, Marketplace.DefaultSource, script, "Write-Host 'neu'", folder);
            Assert.Single(s.Folders, f => f.Path == folder); // Ordner nur einmal eingebunden
        }
        finally { Directory.Delete(folder, true); }
    }
}
