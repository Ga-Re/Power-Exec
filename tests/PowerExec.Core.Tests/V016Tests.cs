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

    [Fact]
    public void ParameterSetBecomesProfileAndKeepsDefault()
    {
        var item = new LaunchItem
        {
            FullPath = @"C:\s\New-User.ps1", RootFolder = @"C:\s", Type = ItemType.PowerShell,
            Parameters = new[] { new ScriptParameter { Name = "Name", TypeName = "string" }, new ScriptParameter { Name = "Credential", TypeName = "pscredential" } }
        };
        var s = new AppSettings();
        var standard = new LaunchProfile { Name = "Standard", IsDefault = true, IsImplicit = true, Dialog = DialogMode.Always };
        var values = new Dictionary<string, string?> { ["Name"] = "Azubi" };
        Assert.Null(PowerExec.Core.Config.ParameterSets.Save(s, item, "Neuer Azubi", values, standard));
        var profiles = s.FindEntry(item.FullPath)!.Profiles;
        Assert.Equal(2, profiles.Count);
        Assert.True(profiles[0].IsDefault);
        Assert.Equal(DialogMode.Always, profiles[0].Dialog); // Klick im Panel tut weiter dasselbe
        var set = profiles[1];
        Assert.False(set.IsDefault);
        Assert.Equal(ParameterMode.Fixed, set.Parameters["Name"].Mode);
        Assert.Equal("Azubi", set.Parameters["Name"].Value);
        Assert.Equal(ParameterMode.Ask, set.Parameters["Credential"].Mode);
        Assert.NotNull(PowerExec.Core.Config.ParameterSets.Save(s, item, "neuer azubi", values, standard)); // Name vergeben
        Assert.NotNull(PowerExec.Core.Config.ParameterSets.Save(s, item, "  ", values, standard));
    }

    [Fact]
    public void WorkflowEntryDescribesStepsAndClonesDeep()
    {
        var wf = new CustomEntry
        {
            Kind = CustomEntryKind.Workflow, Name = "Outlook reparieren",
            Steps = new List<WorkflowStep> { new() { Path = @"C:\s\Stop.ps1", Name = "Stop" }, new() { Path = @"C:\s\Cache.ps1" }, new() { Path = "custom:1", Name = "Start" } }
        };
        var item = wf.ToLaunchItem();
        Assert.Equal("Outlook reparieren", item.DisplayName);
        Assert.Equal(CustomEntryKind.Workflow, item.CustomKind);
        Assert.Contains("3 Schritte", item.Synopsis);
        Assert.Contains("Stop → Cache.ps1 → Start", item.Synopsis);
        Assert.True(wf.StopOnError);

        var copy = wf.Clone();
        copy.Steps.RemoveAt(0);
        copy.Steps[0].Name = "geändert";
        Assert.Equal(3, wf.Steps.Count);
        Assert.Equal(string.Empty, wf.Steps[1].Name);
    }

    [Fact]
    public void ScheduleNextRunAndDescription()
    {
        var wed = new DateTime(2026, 11, 4, 8, 0, 0); // Mittwoch
        var workdays = new Schedule { Kind = ScheduleKind.Daily, TimeOfDay = new TimeSpan(7, 0, 0),
            Days = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday } };
        Assert.Equal(new DateTime(2026, 11, 5, 7, 0, 0), Schedules.Next(workdays, wed)); // heute 07:00 vorbei → Donnerstag
        Assert.Equal(new DateTime(2026, 11, 9, 7, 0, 0), Schedules.Next(workdays, new DateTime(2026, 11, 7, 8, 0, 0))); // Samstag → Montag
        Assert.Equal("Mo–Fr 07:00", Schedules.Describe(workdays));

        var daily = new Schedule { Kind = ScheduleKind.Daily, TimeOfDay = new TimeSpan(9, 30, 0) };
        Assert.Equal(new DateTime(2026, 11, 4, 9, 30, 0), Schedules.Next(daily, wed));
        Assert.Equal("tägl. 09:30", Schedules.Describe(daily));

        var every = new Schedule { Kind = ScheduleKind.Interval, IntervalMinutes = 30, LastRunLocal = wed };
        Assert.Equal(wed.AddMinutes(30), Schedules.Next(every, wed));
        Assert.Equal("alle 30 min", Schedules.Describe(every));
        Assert.Equal("alle 2 Std.", Schedules.Describe(new Schedule { Kind = ScheduleKind.Interval, IntervalMinutes = 120 }));

        var once = new Schedule { Kind = ScheduleKind.Once, OnceAt = wed.AddHours(1) };
        Assert.Equal(wed.AddHours(1), Schedules.Next(once, wed));
        Assert.Null(Schedules.Next(once, wed.AddHours(2)));
    }

    [Fact]
    public void ScheduleDueGraceAndCatchUp()
    {
        var now = new DateTime(2026, 11, 4, 10, 0, 0);
        var s = new Schedule { Kind = ScheduleKind.Once, OnceAt = now.AddMinutes(-1), NextRunLocal = now.AddMinutes(-1) };
        Assert.True(Schedules.IsDue(s, now, TimeSpan.FromMinutes(2)));
        s.NextRunLocal = now.AddHours(-3);
        Assert.False(Schedules.IsDue(s, now, TimeSpan.FromMinutes(2))); // verpasst, nicht nachholen
        s.CatchUp = true;
        Assert.True(Schedules.IsDue(s, now, TimeSpan.FromMinutes(2)));
        Schedules.Advance(s, now, ran: true);
        Assert.False(s.Enabled); // einmalig: nach dem Lauf erledigt
        Assert.Equal(now, s.LastRunLocal);
        s.Enabled = true;
        Assert.False(Schedules.IsDue(s, now, TimeSpan.FromMinutes(2)));
    }

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

    [Fact]
    public void SuggestionsOfferOnlyWhatExists()
    {
        var src = new SuggestionSource();
        src.Tags["Exchange"] = 4; src.Tags["AD"] = 7;
        src.Kinds[SearchKind.PowerShell] = 23; src.Kinds[SearchKind.Rdp] = 2;
        src.Groups["Kunde A"] = 5;
        src.Favorites = 3;

        var tags = SearchSuggestions.For("reset #e", 8, src);
        Assert.Equal("#Exchange", tags.Single().Insert);
        Assert.Equal(4, tags.Single().Count);

        var all = SearchSuggestions.For("#", 1, src);
        Assert.Equal("#AD", all[0].Insert); // häufigstes zuerst

        var kinds = SearchSuggestions.For("typ:", 4, src);
        Assert.Equal(new[] { "typ:ps1", "typ:rdp" }, kinds.Select(k => k.Insert));
        Assert.DoesNotContain(kinds, k => k.Insert == "typ:ssh"); // nicht vorhanden → nicht angeboten

        Assert.Equal("gruppe:\"Kunde A\"", SearchSuggestions.For("gruppe:ku", 9, src).Single().Insert);
        Assert.Equal(new[] { "ist:fav" }, SearchSuggestions.For("ist:", 4, src).Select(x => x.Insert)); // Admin/Remote ohne Treffer fehlen
        Assert.Contains(SearchSuggestions.For("ty", 2, src), x => x.Insert == "typ:");
        Assert.Empty(SearchSuggestions.For("disk", 4, src));
    }

    [Fact]
    public void SuggestionReplacesCurrentWord()
    {
        var (text, caret) = SearchSuggestions.Apply("reset #ex", 9, new SearchSuggestion("#Exchange", "#Exchange", "", 1));
        Assert.Equal("reset #Exchange ", text);
        Assert.Equal(text.Length, caret);
        var (t2, c2) = SearchSuggestions.Apply("ty disk", 2, new SearchSuggestion("typ:", "typ:", "", 1));
        Assert.Equal("typ: disk", t2);
        Assert.Equal(4, c2);
    }

    [Fact]
    public void ColorMathRoundTripsAndExtensionsNeedSemicolons()
    {
        foreach (var hex in new[] { "#3B82F6", "#EF4444", "#000000", "#FFFFFF", "#22C55E" })
        {
            Assert.True(PanelColors.TryParse(hex, out var c));
            var (h, sat, v) = ColorMath.ToHsv(c);
            Assert.Equal(hex, ColorMath.FromHsv(h, sat, v).Hex);
        }
        Assert.Equal(new[] { ".ps1", ".cmd", ".exe" }, ExtensionList.Parse(".ps1; cmd;.EXE"));
        Assert.Null(ExtensionList.Error(".ps1; .cmd"));
        Assert.NotNull(ExtensionList.Error(".ps1 .cmd")); // mit Leerzeichen statt Semikolon
        Assert.Equal(".ps1; .cmd", ExtensionList.Format(new[] { ".ps1", ".cmd" }));
    }
}

