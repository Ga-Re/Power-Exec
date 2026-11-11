using PowerExec.Core.Config;
using PowerExec.UI.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI.Core.Tests;

/// <summary>Marktplatz-Seite: Ordner aus Git, Tabs, Häkchen, Herunterladen in eine Gruppe, Deinstallieren, Quellen.</summary>
[Collection("Marketplace")]
public class MarketplaceViewModelTests
{
    private const string DefaultTree = "https://api.github.com/repos/Ga-Re/Power-Exec/git/trees/main?recursive=1";
    private const string DefaultRaw = "https://raw.githubusercontent.com/Ga-Re/Power-Exec/main/marketplace/";
    private const string TeamSource = "https://github.com/firma/px-katalog";
    private const string TeamTree = "https://api.github.com/repos/firma/px-katalog/git/trees/main?recursive=1";

    private static string Entry(string name, string target, string variable = "") =>
        $$"""{ "Entry": { "Name": "{{name}}", "Kind": "Url", "Target": "{{target}}", "Description": "{{name}} öffnen" }, "Variables": [ {{(variable.Length > 0 ? $$"""{ "Name": "{{variable}}", "Example": "https://x" }""" : "")}} ] }""";

    private static FakeEntryHost Host(string googleSha = "g1")
    {
        MarketplaceViewModel.ResetCache();
        var host = new FakeEntryHost { MarketplaceScriptFolder = Path.Combine(Path.GetTempPath(), "px-mk-" + Guid.NewGuid().ToString("N")) };
        host.Downloads[DefaultTree] = $$"""
            { "tree": [
              { "path": "marketplace/Links/Google.pxentry.json", "type": "blob", "sha": "{{googleSha}}" },
              { "path": "marketplace/Links/Learn.pxentry.json", "type": "blob", "sha": "l1" },
              { "path": "marketplace/Matrix42/Ticket.pxentry.json", "type": "blob", "sha": "m1" },
              { "path": "marketplace/Scripte/System/Get-Disk.ps1", "type": "blob", "sha": "s1" },
              { "path": "marketplace/Designs/Ozean.design.json", "type": "blob", "sha": "d1" } ] }
            """;
        host.Downloads[DefaultRaw + "Links/Google.pxentry.json"] = Entry("Google", "https://google.com/search?q={{Q}}");
        host.Downloads[DefaultRaw + "Links/Learn.pxentry.json"] = Entry("Learn", "https://learn.microsoft.com");
        host.Downloads[DefaultRaw + "Matrix42/Ticket.pxentry.json"] = Entry("Ticket", "{{$Matrix42Url}}/ticket/{{Nr}}", "Matrix42Url");
        host.Downloads[DefaultRaw + "Scripte/System/Get-Disk.ps1"] = "<#\n.SYNOPSIS\n    Zeigt freien Platz.\n#>\nGet-Volume";
        host.Downloads[DefaultRaw + "Designs/Ozean.design.json"] = """{ "Name": "Ozean", "Theme": "Dark", "PanelColor": "#0B2233", "Opacity": 90 }""";
        host.Downloads[TeamTree] = """{ "tree": [ { "path": "Team/Wiki.pxentry.json", "type": "blob", "sha": "w1" } ] }""";
        host.Downloads["https://raw.githubusercontent.com/firma/px-katalog/main/Team/Wiki.pxentry.json"] = Entry("Wiki", "https://wiki.firma.local");
        return host;
    }

    [Fact]
    public async Task FoldersTabsAndSearch()
    {
        var host = Host();
        var vm = new MarketplaceViewModel(host, new AppSettings(), () => { });
        await vm.LoadAsync(force: false);
        Assert.Equal("Einträge (3)", vm.EntryTabText);
        Assert.Equal("Scripte (1)", vm.ScriptTabText);
        Assert.Equal("Designs (1)", vm.DesignTabText);

        // Einträge: Ordner Links und Matrix42 (oberste Ebene aufgeklappt), Namen aus den Dateien
        Assert.Equal(new[] { "Links", "Matrix42" }, vm.Rows.OfType<MarketplaceFolderRow>().Select(f => f.Name));
        var links = vm.Rows.OfType<MarketplaceFolderRow>().First();
        Assert.Equal("2 Dateien", links.CountText);
        Assert.Equal(new[] { "Google", "Learn", "Ticket" }, vm.Rows.OfType<MarketplaceItemRow>().Select(i => i.Name));
        Assert.Equal(1, vm.Rows.OfType<MarketplaceItemRow>().First().Depth);
        links.ToggleCommand.Execute(null);
        Assert.Equal(new[] { "Ticket" }, vm.Rows.OfType<MarketplaceItemRow>().Select(i => i.Name));
        vm.Rows.OfType<MarketplaceFolderRow>().First().ToggleCommand.Execute(null);
        Assert.Equal(3, vm.Rows.OfType<MarketplaceItemRow>().Count());

        // Suche klappt Treffer auf
        vm.Query = "ticket";
        var ticket = Assert.Single(vm.Rows.OfType<MarketplaceItemRow>());
        Assert.Contains("{{$Matrix42Url}}", ticket.Needs);
        vm.Query = string.Empty;

        vm.Tab = "Script";
        var scripts = vm.Rows.OfType<MarketplaceFolderRow>().First();
        Assert.Equal("System", scripts.Name); // Kategorie-Ordner „Scripte“ entfällt, die Gruppe ist der Unterordner
        Assert.Equal("Zeigt freien Platz.", vm.AllItems.Single().Description);
        vm.Tab = "Installed";
        Assert.True(vm.IsEmpty);
        vm.Tab = "Entry";
    }

    [Fact]
    public async Task DownloadFolderIntoGroupAndUninstall()
    {
        var host = Host();
        var s = new AppSettings();
        var changes = 0;
        var vm = new MarketplaceViewModel(host, s, () => changes++);
        await vm.LoadAsync(force: false);
        string? shown = null;
        Snackbar.Shown += m => shown = m;

        // Ordner anhaken → Auswahlleiste; Gruppe wird mit dem Ordnernamen vorgeschlagen
        var links = vm.Rows.OfType<MarketplaceFolderRow>().First();
        links.IsChecked = true;
        Assert.Equal(2, vm.CheckedCount);
        Assert.Equal("Herunterladen (2)", vm.InstallSelectedText);
        host.FakeDialogs.Answer = "Suche";
        vm.InstallSelectedCommand.Execute(null);
        for (int i = 0; i < 100 && vm.IsLoading; i++) await Task.Delay(20);

        Assert.Equal(2, s.CustomEntries.Count);
        Assert.All(s.CustomEntries, e => Assert.Equal("Suche", s.FindEntry(e.Key)!.Category));
        Assert.Contains("In welche Gruppe?", host.FakeDialogs.Asked);
        Assert.Contains("2 Einträge", shown);
        Assert.Equal(1, changes);
        Assert.Equal(0, vm.CheckedCount);
        Assert.StartsWith("Installiert (2)", vm.InstalledTabText);

        // Deinstallieren über den Tab „Installiert“
        vm.Tab = "Installed";
        foreach (var f in vm.Rows.OfType<MarketplaceFolderRow>().ToList()) f.IsChecked = true;
        Assert.Equal("Deinstallieren (2)", vm.UninstallSelectedText);
        vm.UninstallSelectedCommand.Execute(null);
        Assert.Empty(s.CustomEntries);
        Assert.Empty(s.MarketplaceInstalls);
        Assert.Equal(2, changes);
        vm.Tab = "Entry";
    }

    [Fact]
    public async Task VariablesAskedModifiedEntryKeptOnUninstall()
    {
        var host = Host();
        var s = new AppSettings();
        var vm = new MarketplaceViewModel(host, s, () => { });
        await vm.LoadAsync(force: false);
        vm.Query = "ticket";
        var ticket = Assert.Single(vm.Rows.OfType<MarketplaceItemRow>());
        host.FakeDialogs.Answer = "Matrix42";
        await vm.InstallOneAsync(ticket);
        Assert.Null(vm.Error);
        // fehlende Variable: im Variablen-Dialog angelegt, mit Art und Beispielwert vorbelegt
        var variable = Assert.Single(s.Variables);
        Assert.Equal(("Matrix42Url", VariableKind.Text, "https://x"), (variable.Name, variable.Kind, variable.Value));
        Assert.Single(s.CustomEntries);

        // geändert → Rückfrage; abgelehnt → bleibt
        s.CustomEntries[0].Name = "Mein Ticket";
        var row = Assert.Single(vm.Rows.OfType<MarketplaceItemRow>());
        Assert.True(row.IsInstalled);
        host.FakeDialogs.ConfirmAnswer = true;
        vm.Uninstall(new[] { row });
        // FakeDialogs gibt immer dieselbe Antwort: beide Rückfragen „ja“ → entfernt
        Assert.Contains(host.FakeDialogs.Messages, m => m.Title == "Geänderter Eintrag");
        Assert.Empty(s.CustomEntries);
    }

    [Fact]
    public async Task UpdateKeepsGroupAndScriptsAndDesigns()
    {
        var host = Host();
        var s = new AppSettings();
        var vm = new MarketplaceViewModel(host, s, () => { });
        await vm.LoadAsync(force: false);
        host.FakeDialogs.Answer = "Links";
        await vm.InstallAsync(vm.AllItems.Where(i => i.Name == "Google").ToList());
        s.FindEntry(s.CustomEntries[0].Key)!.Category = "Meine Suche";

        // neuer Stand im Repo → Update verfügbar, landet wieder in derselben Gruppe ohne Rückfrage
        var host2 = Host(googleSha: "g2");
        var vm2 = new MarketplaceViewModel(host2, s, () => { });
        await vm2.LoadAsync(force: true);
        var google = vm2.AllItems.Single(i => i.Name == "Google");
        Assert.True(google.HasUpdate);
        Assert.Equal("Update verfügbar", google.StateText);
        Assert.Contains("1 Updates", vm2.InstalledTabText);
        host2.FakeDialogs.Answer = null;
        await vm2.InstallOneAsync(google);
        Assert.DoesNotContain("In welche Gruppe?", host2.FakeDialogs.Asked);
        Assert.Equal("Meine Suche", s.FindEntry(Assert.Single(s.CustomEntries).Key)!.Category);
        Assert.False(vm2.AllItems.Single(i => i.Name == "Google").HasUpdate);

        // Script und Design
        vm2.Tab = "Script";
        host2.FakeDialogs.Answer = "Werkzeuge";
        await vm2.InstallAsync(vm2.AllItems.ToList());
        Assert.Contains(s.CustomEntries, e => e.Kind == CustomEntryKind.File && File.Exists(e.Target));
        vm2.Tab = "Design";
        await vm2.InstallAsync(vm2.AllItems.ToList());
        Assert.Equal("Ozean", Assert.Single(s.CustomDesigns).Name);
        vm2.Tab = "Entry";
        Directory.Delete(host2.MarketplaceScriptFolder, true);
    }

    [Fact]
    public async Task AddSourceAskTrustOnce()
    {
        var host = Host();
        var s = new AppSettings();
        var vm = new MarketplaceViewModel(host, s, () => { });
        await vm.LoadAsync(force: false);
        Assert.False(vm.HasSeveralSources);

        host.FakeDialogs.Answer = "keine url";
        vm.AddSourceCommand.Execute(null);
        Assert.Empty(s.MarketplaceSources);

        host.FakeDialogs.Answer = TeamSource;
        vm.AddSourceCommand.Execute(null);
        for (int i = 0; i < 100 && vm.IsLoading; i++) await Task.Delay(20);
        await vm.LoadAsync(force: false);
        var src = Assert.Single(s.MarketplaceSources);
        Assert.False(src.Trusted);
        Assert.True(vm.HasSeveralSources);
        Assert.Equal(2, vm.SourceList.Count);
        // mehrere Quellen: oberste Ebene sind die Quellen
        Assert.Equal(new[] { "Power-Exec", "firma/px-katalog" }, vm.Rows.OfType<MarketplaceFolderRow>().Where(f => f.IsSource).Select(f => f.Name));

        var wiki = vm.AllItems.Single(i => i.Name == "Wiki");
        host.FakeDialogs.ConfirmAnswer = false;
        await vm.InstallOneAsync(wiki);
        Assert.Empty(s.CustomEntries);
        Assert.False(src.Trusted);

        host.FakeDialogs.ConfirmAnswer = true;
        host.FakeDialogs.Answer = "Team";
        await vm.InstallOneAsync(vm.AllItems.Single(i => i.Name == "Wiki"));
        Assert.True(src.Trusted);
        Assert.Single(s.CustomEntries);

        vm.SourceList[1].RemoveCommand.Execute(null);
        Assert.Empty(s.MarketplaceSources);
        Assert.Single(s.CustomEntries); // Installiertes bleibt
    }

    [Fact]
    public async Task UnreachableSourceShowsError()
    {
        var host = Host();
        host.Downloads.Remove(DefaultTree);
        var vm = new MarketplaceViewModel(host, new AppSettings(), () => { });
        await vm.LoadAsync(force: false);
        Assert.NotNull(vm.Error);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task InstalledEntryKeepsGroupAfterSettingsAreApplied()
    {
        var host = Host();
        var vm = new SettingsViewModel(host, focusFolder: null);
        await vm.MarketplacePage.LoadAsync(force: false);
        host.FakeDialogs.Answer = "Suche";
        await vm.MarketplacePage.InstallAsync(vm.MarketplacePage.AllItems.Where(i => i.Name == "Google").ToList());

        // Übernehmen baut die Einstellungen aus den Listen neu – die Gruppe darf dabei nicht verloren gehen
        var s = vm.Build();
        var e = Assert.Single(s.CustomEntries);
        Assert.Equal("Suche", GroupManager.GroupOf(s, e.Key));
        Assert.Contains("Suche", s.CategoryOrder);
        Assert.Contains(vm.EntryList.Rows, r => r.Group == "Suche");

        // Deinstallieren entfernt den Eintrag auch aus der Liste
        vm.MarketplacePage.Uninstall(vm.MarketplacePage.AllItems.Where(i => i.Name == "Google").ToList());
        Assert.Empty(vm.Build().CustomEntries);
    }

    [Fact]
    public async Task DeletingDesignUnderAppearanceMarksItNotInstalled()
    {
        var host = Host();
        var vm = new SettingsViewModel(host, focusFolder: null);
        await vm.MarketplacePage.LoadAsync(force: false);
        vm.MarketplacePage.Tab = "Design";
        await vm.MarketplacePage.InstallAsync(vm.MarketplacePage.AllItems.ToList());
        Assert.True(vm.MarketplacePage.AllItems.Single().IsInstalled);

        // unter Darstellung löschen → im Marktplatz wieder „nicht installiert“
        vm.DesignPresets.Single(p => p.IsCustom).DeleteCommand!.Execute(null);
        Assert.Empty(vm.Build().CustomDesigns);
        Assert.False(vm.MarketplacePage.AllItems.Single().IsInstalled);
        Assert.Empty(vm.Build().MarketplaceInstalls);
        vm.MarketplacePage.Tab = "Entry";
    }
}
