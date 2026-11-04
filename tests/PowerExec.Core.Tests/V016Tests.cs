using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;
using Xunit;

namespace PowerExec.Core.Tests;

/// <summary>0.16: unscharfe Suche, Operatoren, Häufigkeit.</summary>
public class V016Tests
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

    [Fact]
    public void UndoStackKeepsNewestWithinCapacity()
    {
        var stack = new UndoStack<int>(capacity: 2);
        Assert.False(stack.TryPop(out _));
        stack.Push(1, "eins");
        stack.Push(2, "zwei");
        stack.Push(3, "drei");
        Assert.Equal(2, stack.Count);
        Assert.Equal("drei", stack.Peek!.Description);
        Assert.True(stack.TryPop(out var step));
        Assert.Equal(3, step!.Before);
        Assert.True(stack.TryPop(out step));
        Assert.Equal(2, step!.Before);
        Assert.False(stack.TryPop(out _));
    }

    [Fact]
    public void CodePreviewColorsPowerShell()
    {
        const string code = "param([string]$Name)\nGet-Item 'x' -Force # Kommentar";
        var segments = PowerExec.Core.Metadata.CodePreview.TokenizePowerShell(code);
        Assert.Equal(code, string.Concat(segments.Select(s => s.Text)));
        bool Has(string text, PowerExec.Core.Metadata.CodeTokenKind kind) => segments.Any(s => s.Kind == kind && s.Text.Contains(text));
        Assert.True(Has("param", PowerExec.Core.Metadata.CodeTokenKind.Keyword));
        Assert.True(Has("$Name", PowerExec.Core.Metadata.CodeTokenKind.Variable));
        Assert.True(Has("Get-Item", PowerExec.Core.Metadata.CodeTokenKind.Command));
        Assert.True(Has("'x'", PowerExec.Core.Metadata.CodeTokenKind.String));
        Assert.True(Has("-Force", PowerExec.Core.Metadata.CodeTokenKind.Parameter));
        Assert.True(Has("# Kommentar", PowerExec.Core.Metadata.CodeTokenKind.Comment));
    }

    [Fact]
    public void CodePreviewMarksBatchCommentsAndHead()
    {
        var segments = PowerExec.Core.Metadata.CodePreview.TokenizeBatch("@echo off\nrem Räumt auf\n:: auch Kommentar\n:ende");
        Assert.Contains(segments, s => s.Kind == PowerExec.Core.Metadata.CodeTokenKind.Comment && s.Text.Contains("rem Räumt auf") && s.Text.Contains(":: auch"));
        Assert.Contains(segments, s => s.Kind == PowerExec.Core.Metadata.CodeTokenKind.Keyword && s.Text == ":ende");

        var (head, truncated) = PowerExec.Core.Metadata.CodePreview.Head(string.Join("\n", Enumerable.Range(1, 100)), 10);
        Assert.True(truncated);
        Assert.Equal(10, head.Split('\n').Length);
    }

    [Fact]
    public void AccentIsReadableInBothThemes()
    {
        var darkBlue = new Rgb(0x00, 0x33, 0x99); // zu dunkel für das dunkle Design
        var dark = Appearance.Derive(darkBlue, dark: true);
        Assert.True(PanelColors.Contrast(dark.Accent, new Rgb(0x20, 0x20, 0x20)) >= 3.0);
        var yellow = new Rgb(0xFF, 0xD7, 0x00); // zu hell für das helle Design
        var light = Appearance.Derive(yellow, dark: false);
        Assert.True(PanelColors.Contrast(light.Accent, new Rgb(0xF3, 0xF3, 0xF3)) >= 3.0);
        Assert.True(PanelColors.Contrast(light.OnAccent, light.Accent) >= 3.0); // Schrift auf Akzent-Knöpfen
        Assert.Equal(new Rgb(0x10, 0x20, 0x30), Appearance.FromAbgr(0xFF302010));
    }

    [Fact]
    public void DesignPresetAppliesAndIsRecognized()
    {
        var s = new AppSettings();
        s.Panels.Add(new PanelConfig());
        var glass = Appearance.Presets.Single(p => p.Key == "glass");
        Appearance.Apply(s, glass);
        Assert.True(s.Panels[0].Acrylic);
        Assert.True(s.UseWindowsAccent);
        Assert.Equal(WindowBackdrop.Mica, s.WindowBackdrop);
        Assert.Equal("glass", Appearance.Current(s)?.Key);
        s.Panels[0].PanelOpacity = 60;
        Assert.Null(Appearance.Current(s));
    }

    [Fact]
    public void ReleaseNotesSinceLastSeenVersion()
    {
        var current = new Version(0, 16, 0);
        Assert.NotEmpty(PowerExec.Core.Updates.ReleaseNotes.Since(new Version(0, 15, 0), current));
        Assert.NotEmpty(PowerExec.Core.Updates.ReleaseNotes.Since(null, current));
        Assert.Empty(PowerExec.Core.Updates.ReleaseNotes.Since(new Version(0, 16, 0), current));
        Assert.Empty(PowerExec.Core.Updates.ReleaseNotes.Since(new Version(0, 14, 0), new Version(0, 15, 2)));
        Assert.Equal(new Version(0, 16, 0), PowerExec.Core.Updates.ReleaseNotes.Parse("0.16.0"));
        Assert.Null(PowerExec.Core.Updates.ReleaseNotes.Parse(""));
    }

    [Fact]
    public void DockSitsAtWorkAreaEdgeCentered()
    {
        var work = new System.Drawing.Rectangle(0, 0, 1920, 1040); // Taskleiste unten
        var right = PowerExec.Core.Layout.PanelLayout.Dock(work, new PanelConfig { Edge = ScreenEdge.Right }, 1.0, 4);
        Assert.Equal(1920, right.Right);
        Assert.Equal(PowerExec.Core.Layout.PanelLayout.DockThickness, right.Width);
        Assert.Equal(4 * 36 + 16, right.Height);
        Assert.Equal((1040 - right.Height) / 2, right.Top);
        var bottom = PowerExec.Core.Layout.PanelLayout.Dock(work, new PanelConfig { Edge = ScreenEdge.Bottom }, 1.5, 2);
        Assert.Equal(1040, bottom.Bottom); // über der Taskleiste
        Assert.Equal(60, bottom.Height);
    }

    [Fact]
    public void HistoryKeepsNoSecretsAndReplaysExactly()
    {
        var item = new LaunchItem
        {
            FullPath = @"C:\s\Reset.ps1", RootFolder = @"C:\s", Type = ItemType.PowerShell,
            Parameters = new[]
            {
                new ScriptParameter { Name = "User", TypeName = "string" },
                new ScriptParameter { Name = "Password", TypeName = "string" },
                new ScriptParameter { Name = "Force", TypeName = "switch" }
            }
        };
        var raw = new Dictionary<string, string?> { ["User"] = "lena", ["Password"] = "geheim", ["Force"] = "true" };
        var e = RunHistory.Create(item, "Reset", "Standard", ExecutionMode.Administrator, PsEngine.WindowsPowerShell, new[] { "A", "B" }, raw);
        Assert.False(e.Parameters.ContainsKey("Password"));
        Assert.Equal("lena", e.Parameters["User"]);
        e.FailedTargets = new List<string> { "B" };

        var replay = RunHistory.ReplayProfile(e);
        Assert.True(replay.RunAsAdmin);
        Assert.Equal(ProfileTarget.Fixed, replay.Target);
        Assert.Equal(new[] { "A", "B" }, replay.Machines);
        Assert.Equal(ParameterMode.Fixed, replay.Parameters["User"].Mode);
        Assert.Equal(new[] { "B" }, RunHistory.ReplayProfile(e, onlyFailed: true).Machines);
        Assert.Equal(ProfileTarget.Local, RunHistory.ReplayProfile(new HistoryEntry()).Target);
    }

    [Fact]
    public void HistoryFiltersAndRoundTrips()
    {
        var h = new RunHistory();
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Local);
        h.Add(new HistoryEntry { Name = "Alt", StartUtc = now.AddDays(-2).ToUniversalTime(), Result = HistoryResult.Error });
        h.Add(new HistoryEntry { Name = "Heute", StartUtc = now.ToUniversalTime(), Result = HistoryResult.Success, Targets = new List<string> { "SRV01" } });
        Assert.Equal("Heute", h.Entries[0].Name);
        Assert.Single(h.Filter("today", null, now));
        Assert.Single(h.Filter("errors", null, now));
        Assert.Single(h.Filter("remote", null, now));
        Assert.Single(h.Filter("all", "srv01", now));

        var path = Path.Combine(Path.GetTempPath(), "pe-history-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            h.Save(path);
            var loaded = RunHistory.Load(path);
            Assert.Equal(2, loaded.Entries.Count);
            Assert.Equal(HistoryResult.Error, loaded.Entries[1].Result);
        }
        finally { File.Delete(path); }
        Assert.Empty(RunHistory.Load(path + ".fehlt").Entries);
    }
}

