using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

/// <summary>Marktplatz: Quellen, Dateilisten aus Git, Installation, Updates, Deinstallation – und die mitgelieferten Dateien im Repo.</summary>
[Collection("Placeholders")]
public class MarketplaceTests
{
    private static string RepoDir(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PowerExec.sln"))) return Path.Combine(dir.FullName, relative);
        throw new DirectoryNotFoundException(relative);
    }

    [Theory]
    [InlineData("https://github.com/Ga-Re/Power-Exec/tree/main/marketplace", true, "Ga-Re/Power-Exec", "main", "marketplace")]
    [InlineData("https://github.com/firma/katalog", true, "firma/katalog", "main", "")]
    [InlineData("https://github.com/firma/katalog.git", true, "firma/katalog", "main", "")]
    [InlineData("https://github.com/firma/katalog/tree/dev/px/team", true, "firma/katalog", "dev", "px/team")]
    [InlineData("https://gitlab.firma.local/it/px-katalog/-/tree/main/markt", false, "it/px-katalog", "main", "markt")]
    [InlineData("https://gitlab.com/gruppe/projekt", false, "gruppe/projekt", "main", "")]
    public void SourcesAreParsed(string url, bool github, string project, string branch, string folder)
    {
        var r = Marketplace.ParseSource(url);
        Assert.Equal((github, project, branch, folder), (r.IsGitHub, r.Project, r.Branch, r.Folder));
    }

    [Fact]
    public void TreeAndRawUrls()
    {
        var gh = Marketplace.ParseSource(Marketplace.DefaultSource);
        Assert.Equal("https://api.github.com/repos/Ga-Re/Power-Exec/git/trees/main?recursive=1", gh.TreeUri().ToString());
        Assert.Equal("https://raw.githubusercontent.com/Ga-Re/Power-Exec/main/marketplace/Links/Google%20Suche.pxentry.json",
            gh.RawUri("Links/Google Suche.pxentry.json").AbsoluteUri);
        var gl = Marketplace.ParseSource("https://gitlab.firma.local/it/px/-/tree/main/markt");
        Assert.Contains("/api/v4/projects/it%2Fpx/repository/tree?ref=main&recursive=true&per_page=100&page=2&path=markt", gl.TreeUri(2).ToString());
        Assert.Equal("https://gitlab.firma.local/it/px/-/raw/main/markt/A/b.ps1", gl.RawUri("A/b.ps1").ToString());
        Assert.Throws<ArgumentException>(() => Marketplace.ParseSource("kein link"));
        Assert.Throws<ArgumentException>(() => Marketplace.ParseSource("https://intranet.local/index.json"));
    }

    [Fact]
    public void TreesOfGitHubAndGitLab()
    {
        var gh = Marketplace.ParseSource(Marketplace.DefaultSource);
        var files = Marketplace.ParseTree("""
            { "tree": [
              { "path": "marketplace/Links/Google.pxentry.json", "type": "blob", "sha": "a1" },
              { "path": "marketplace/Links", "type": "tree", "sha": "x" },
              { "path": "marketplace/README.md", "type": "blob", "sha": "r" },
              { "path": "marketplace/Scripte/System/Get-Disk.ps1", "type": "blob", "sha": "b2" },
              { "path": "marketplace/Designs/Ozean.design.json", "type": "blob", "sha": "c3" },
              { "path": "samples/Other.ps1", "type": "blob", "sha": "d4" } ] }
            """, gh);
        Assert.Equal(new[] { "Designs/Ozean.design.json", "Links/Google.pxentry.json", "Scripte/System/Get-Disk.ps1" }, files.Select(f => f.Path));
        Assert.Equal(new[] { MarketplaceItemType.Design, MarketplaceItemType.Entry, MarketplaceItemType.Script }, files.Select(f => f.Type));
        Assert.Equal("Scripte/System", files[2].Folder);
        Assert.Equal("Get-Disk", files[2].DefaultName);

        var gl = Marketplace.ParseTree("""[ { "path": "a/x.pxentry.json", "type": "blob", "id": "s1" }, { "path": "a", "type": "tree", "id": "t" } ]""",
            Marketplace.ParseSource("https://gitlab.com/g/p"));
        Assert.Equal("s1", Assert.Single(gl).Sha);
    }

    [Fact]
    public void BundledMarketplaceIsValid()
    {
        var root = RepoDir("marketplace");
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Where(p => Marketplace.TypeOf(p) != null)
            .Select(p => new MarketplaceFile(p, "v1", Marketplace.TypeOf(p)!.Value)).ToList();
        Assert.True(files.Count(f => f.Type == MarketplaceItemType.Entry) >= 8);
        Assert.True(files.Count(f => f.Type == MarketplaceItemType.Design) >= 4);
        Assert.True(files.Count(f => f.Type == MarketplaceItemType.Script) >= 8);
        var details = files.ToDictionary(f => f.Path, f => Marketplace.Describe(f, File.ReadAllText(Path.Combine(root, f.Path))));
        Assert.All(details.Values, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
        // Designs: jede Variante bleibt lesbar (Text 4,5:1, Akzent 3:1 zur Fläche) und bringt Gruppenfarben mit
        foreach (var design in details.Values.Select(d => d.Design).OfType<DesignPreset>())
        {
            var variants = new List<(DesignColors Colors, bool Dark)> { (new DesignColors(design.PanelColor, design.AccentColor, design.Palette), design.PrimaryIsDark) };
            if (design.Pair is { } pair) variants.Add((new DesignColors(pair.PanelColor, pair.AccentColor, pair.Palette), !design.PrimaryIsDark));
            foreach (var (colors, dark) in variants)
            {
                var r = Appearance.Resolve(colors, dark);
                Assert.True(PanelColors.Contrast(r.Text, r.Surface) >= 4.5, $"{design.Name}: Text auf Fläche");
                Assert.True(PanelColors.Contrast(r.Text, r.Card) >= 4.5, $"{design.Name}: Text auf Karte");
                Assert.True(PanelColors.Contrast(r.TextSecondary, r.Surface) >= 4.5, $"{design.Name}: Zweittext");
                Assert.True(PanelColors.Contrast(r.Accent, r.Surface) >= 3.0, $"{design.Name}: Akzent");
            }
            Assert.True(Appearance.SplitColors(design.GroupColors).Count >= 3, $"{design.Name}: Gruppenfarben");
        }
        Assert.All(files.Where(f => f.Type == MarketplaceItemType.Entry), f => Assert.False(string.IsNullOrWhiteSpace(details[f.Path].Description)));
        Assert.Contains(details, kv => kv.Key.StartsWith("Einträge/Matrix42/", StringComparison.Ordinal) && kv.Value.Variables.Any(v => v.Name == "Matrix42Url"));
        // Variablen der Einträge sind deklariert
        var s = new AppSettings();
        foreach (var f in files.Where(f => f.Type == MarketplaceItemType.Entry))
        {
            var e = Marketplace.ParseEntry(File.ReadAllText(Path.Combine(root, f.Path)));
            var declared = e.Variables.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(Variables.Unknown(s, e.Entry.Target, e.Entry.Arguments, e.Entry.Code), n => Assert.Contains(Variables.Normalize(n), declared));
        }
    }

    private const string Source = "https://github.com/firma/katalog";

    private static string EntryJson(string target) =>
        $$"""{ "Entry": { "Name": "Wiki", "Kind": "Url", "Target": "{{target}}", "Description": "Team-Wiki" }, "Variables": [ { "Name": "WikiUrl", "Example": "https://wiki" } ] }""";

    [Fact]
    public void InstallUpdateModifyUninstallEntry()
    {
        var s = new AppSettings();
        var file = new MarketplaceFile("Team/Wiki.pxentry.json", "v1", MarketplaceItemType.Entry);
        Assert.Single(Marketplace.MissingVariables(s, Marketplace.Describe(file, EntryJson("{{$WikiUrl}}")).Variables));

        var record = Marketplace.Install(s, Source, file, EntryJson("{{$WikiUrl}}/start"), "Team", "unused");
        var entry = Assert.Single(s.CustomEntries);
        Assert.Equal("Wiki", entry.Name);
        Assert.Equal("Team", s.FindEntry(entry.Key)!.Category);
        Assert.Contains("Team", s.CategoryOrder);
        Assert.False(Marketplace.HasUpdate(s, Source, file));
        Assert.True(Marketplace.HasUpdate(s, Source, file with { Sha = "v2" }));
        Assert.Equal("Team", Marketplace.InstalledGroup(s, record));

        // Update ersetzt statt zu verdoppeln
        Marketplace.Install(s, Source, file with { Sha = "v2" }, EntryJson("{{$WikiUrl}}/neu"), "Team", "unused");
        Assert.Equal("{{$WikiUrl}}/neu", Assert.Single(s.CustomEntries).Target);
        Assert.Single(s.MarketplaceInstalls);
        Assert.False(Marketplace.IsModified(s, record));

        s.CustomEntries[0].Name = "Mein Wiki";
        Assert.True(Marketplace.IsModified(s, record));
        s.Favorites.Add(s.CustomEntries[0].Key);

        Marketplace.Uninstall(s, record);
        Assert.Empty(s.CustomEntries);
        Assert.Empty(s.Favorites);
        Assert.Empty(s.MarketplaceInstalls);
        Assert.Null(Marketplace.FindInstall(s, Source, file.Path));
    }

    [Fact]
    public void InstallScriptAndDesign()
    {
        var s = new AppSettings();
        var dir = Path.Combine(Path.GetTempPath(), "px-market-" + Guid.NewGuid().ToString("N"));
        try
        {
            var script = new MarketplaceFile("System/Get-Disk.ps1", "s1", MarketplaceItemType.Script);
            var rec = Marketplace.Install(s, Source, script, "<#\n.SYNOPSIS\n    Zeigt Platz.\n#>\nGet-Volume", "Scripte", dir);
            Assert.True(File.Exists(rec.ScriptPath));
            Assert.StartsWith(Path.Combine(dir, "System"), rec.ScriptPath);
            var e = Assert.Single(s.CustomEntries);
            Assert.Equal(CustomEntryKind.File, e.Kind);
            Assert.Equal("Zeigt Platz.", e.Description);
            Assert.Equal("Scripte", s.FindEntry(e.Key)!.Category);

            var design = new MarketplaceFile("Designs/Ozean.design.json", "d1", MarketplaceItemType.Design);
            var drec = Marketplace.Install(s, Source, design, """{ "Name": "Ozean", "Theme": "Dark", "PanelColor": "#0B2233", "Opacity": 90 }""", "", dir);
            Assert.Equal("Ozean", Assert.Single(s.CustomDesigns).Name);

            Marketplace.Uninstall(s, rec);
            Assert.False(File.Exists(rec.ScriptPath));
            Marketplace.Uninstall(s, drec);
            Assert.Empty(s.CustomDesigns);
            Assert.Empty(s.CustomEntries);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SourcesTrustAndValidation()
    {
        var s = new AppSettings();
        Assert.True(Marketplace.IsTrusted(s, Marketplace.DefaultSource));
        Assert.NotNull(Marketplace.ValidateSource(s, Marketplace.DefaultSource + "/"));
        Assert.Null(Marketplace.ValidateSource(s, Source));
        s.MarketplaceSources.Add(new MarketplaceSource { Url = Source });
        Assert.False(Marketplace.IsTrusted(s, Source));
        Assert.Equal(2, Marketplace.Sources(s).Count);
        Assert.Equal("firma/katalog", Marketplace.NameFromUrl(Source));
    }
}
