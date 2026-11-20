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
        host.Downloads[DefaultRaw + "Designs/Ozean.design.json"] = """{ "Name": "Ozean", "Theme": "Dark", "PanelColor": "#0B2233", "Opacity": 90, "Category": "Nature", "Translations": { "de": { "Category": "Natur" } } }""";
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
    public async Task WindowAppliesInstallsImmediatelyWithGroup()
    {
        var host = Host();
        AppSettings? applied = null;
        var vm = new MarketplaceWindowViewModel(host, s => applied = s);
        await vm.Page.LoadAsync(force: false);
        host.FakeDialogs.Answer = "Suche";
        await vm.Page.InstallAsync(vm.Page.AllItems.Where(i => i.Name == "Google").ToList());

        // Jede Installation wird sofort übernommen – samt der gewählten Gruppe
        Assert.NotNull(applied);
        var e = Assert.Single(applied!.CustomEntries);
        Assert.Equal("Suche", GroupManager.GroupOf(applied, e.Key));
        Assert.Contains("Suche", applied.CategoryOrder);

        vm.Page.Uninstall(vm.Page.AllItems.Where(i => i.Name == "Google").ToList());
        Assert.Empty(applied!.CustomEntries);
    }

    [Fact]
    public async Task WindowFollowsSettingsChangedElsewhere()
    {
        var host = Host();
        var vm = new MarketplaceWindowViewModel(host, s => { host.Settings.CustomDesigns.Clear(); host.Settings.CustomDesigns.AddRange(s.CustomDesigns); host.Settings.MarketplaceInstalls.Clear(); host.Settings.MarketplaceInstalls.AddRange(s.MarketplaceInstalls); });
        await vm.Page.LoadAsync(force: false);
        vm.Tab = "Design";
        await vm.Page.InstallAsync(vm.Page.AllItems.ToList());
        Assert.True(vm.Page.AllItems.Single().IsInstalled);

        // Design unter Darstellung gelöscht (Einstellungsfenster): der Marktplatz arbeitet mit dem neuen Stand weiter
        host.Settings.CustomDesigns.Clear();
        host.Settings.MarketplaceInstalls.Clear();
        var before = vm.Page;
        vm.SettingsChanged();
        Assert.NotSame(before, vm.Page);
        Assert.False(vm.Page.AllItems.Single().IsInstalled);
        Assert.Equal("Design", vm.Tab); // der gewählte Reiter bleibt
    }

    [Fact]
    public async Task GalleryShowsAllTypesWithCategoriesAndOneOpenCard()
    {
        var host = Host();
        var vm = new MarketplaceWindowViewModel(host, _ => { });
        await vm.Page.LoadAsync(force: false);

        // „Entdecken“ zeigt Einträge und Scripte als Karten – Designs haben ihren eigenen Bereich
        Assert.Equal("All", vm.Tab);
        Assert.False(vm.IsDesignArea);
        Assert.Equal(vm.Page.AllCount, vm.Page.Cards.Count);
        Assert.DoesNotContain(vm.Page.Cards, c => c.File.Type == MarketplaceItemType.Design);
        Assert.Equal(vm.Page.AllCount.ToString(System.Globalization.CultureInfo.CurrentCulture), vm.Tabs.First(t => t.Key == "All").CountText);

        // Kategorie filtert die Karten; erneuter Klick hebt den Filter auf
        var links = vm.Page.Categories.First(c => c.Name == "Links");
        links.SelectCommand.Execute(null);
        Assert.Equal(links.Count, vm.Page.Cards.Count);
        Assert.All(vm.Page.Cards, c => Assert.Equal("Links", c.CategoryName));
        vm.Page.Categories.First(c => c.Name == "Links").SelectCommand.Execute(null);
        Assert.Equal(vm.Page.AllCount, vm.Page.Cards.Count);

        // Detailseite: immer nur ein Eintrag offen; „← Entdecken“ führt zurück
        Assert.False(vm.Page.IsDetailOpen);
        Assert.Equal("← Entdecken", vm.Page.BackText);
        vm.Page.Cards[0].OpenCommand.Execute(null);
        vm.Page.Cards[1].OpenCommand.Execute(null);
        Assert.True(vm.Page.IsDetailOpen);
        Assert.Same(vm.Page.Cards[1], vm.Page.OpenItem);
        Assert.False(vm.Page.Cards[0].IsPreviewOpen);
        Assert.True(vm.Page.Cards[1].IsPreviewOpen);
        vm.Page.BackCommand.Execute(null);
        Assert.False(vm.Page.IsDetailOpen);
        Assert.Null(vm.Page.OpenItem);
        Assert.False(vm.Page.Cards[1].IsPreviewOpen);

        // Ein anderer Bereich oder eine Kategorie in der Navigation schließt die Detailseite
        vm.Page.Cards[0].OpenCommand.Execute(null);
        vm.Page.Category = "Links";
        Assert.False(vm.Page.IsDetailOpen);
        vm.Page.Category = string.Empty;
        vm.Page.Cards[0].OpenCommand.Execute(null);
        vm.Tab = "Entry";
        Assert.False(vm.Page.IsDetailOpen);
        Assert.Equal("← Einträge", vm.Page.BackText);
    }

    [Fact]
    public async Task EntryDetailShowsActionsAndStaysOpenAfterInstall()
    {
        var host = Host();
        var vm = new MarketplaceWindowViewModel(host, _ => { });
        await vm.Page.LoadAsync(force: false);
        var google = vm.Page.Cards.First(c => c.Name == "Google");
        google.OpenCommand.Execute(null);

        Assert.Equal("Google öffnen · Links · von Power-Exec", google.MetaText);
        Assert.True(google.ShowInstall);
        Assert.False(google.ShowApply);                         // „Anwenden“ gibt es nur bei Designs

        // Nach dem Herunterladen bleibt die Seite offen – mit dem neuen Stand
        host.FakeDialogs.Answer = "Links";
        await vm.Page.InstallAsync(new[] { google });
        Assert.True(vm.Page.IsDetailOpen);
        var open = vm.Page.OpenItem!;
        Assert.Equal("Google", open.Name);
        Assert.True(open.IsInstalled);
        Assert.False(open.ShowInstall);
        Assert.True(open.IsPreviewOpen);
    }

    [Fact]
    public async Task DesignDetailColorsPaletteFontsAndForm()
    {
        var host = Host();
        host.Downloads[DefaultRaw + "Designs/Ozean.design.json"] = """
            { "Name": "Ozean", "Description": "Tiefes Blau", "Theme": "Dark", "PanelColor": "#0B2233", "AccentColor": "#3AA0FF", "Opacity": 80, "Frost": 20,
              "Backdrop": "Clear", "Corners": "Rounded", "FontFamily": "Fehlt Sans", "MonoFontFamily": "JetBrains Mono",
              "GroupColors": "#FF8A70, #7FD1C7, #F2C14E", "GroupColorsForTags": true,
              "Pair": { "PanelColor": "#EEF4F8", "AccentColor": "#0A6FD6", "GroupColors": "#B4472F, #2F7F76" } }
            """;
        var theme = (FakeTheme)host.Theme;
        theme.IsDark = false;                                   // App läuft hell
        theme.MissingFonts.Add("Fehlt Sans");
        theme.MissingFonts.Add("JetBrains Mono");
        var vm = new MarketplaceWindowViewModel(host, _ => { });
        await vm.Page.LoadAsync(force: false);
        vm.Tab = "Design";
        var card = Assert.Single(vm.Page.Cards);
        Assert.True(card.ShowsDark);                            // Karte: Hauptvariante

        // Öffnen: Vorschau startet in der Variante der App (hell)
        card.OpenCommand.Execute(null);
        Assert.Equal("← Entdecken", vm.Page.BackText);
        Assert.False(card.ShowsDark);
        Assert.True(card.IsPreviewLight);
        Assert.Equal("Tiefes Blau · von Power-Exec", card.MetaText);

        // Farben: erst dunkel, dann hell; je sieben Felder; die gezeigte Variante ist hervorgehoben
        Assert.Equal(new[] { true, false }, card.DesignColorRows.Select(r => r.IsDark));
        Assert.All(card.DesignColorRows, r => Assert.Equal(7, r.Swatches.Count));
        Assert.Equal(new[] { "Panel", "Karte", "Eingabe", "Linie", "Text", "Auswahl", "Akzent" }, card.DesignColorRows[0].Swatches.Select(s => s.Name));
        Assert.Equal("#0B2233", card.DesignColorRows[0].Swatches[0].Hex);
        Assert.Equal("#EEF4F8", card.DesignColorRows[1].Swatches[0].Hex);
        Assert.False(card.DesignColorRows[0].IsActive);
        Assert.True(card.DesignColorRows[1].IsActive);

        // Gruppenpalette: sechs übersetzte Beispielgruppen, Farben der Variante der Reihe nach (umlaufend)
        var darkGroups = card.DesignGroupRows.Single(r => r.IsDark);
        Assert.Equal(new[] { "Favoriten", "Admin", "Tools", "Netzwerk", "Server", "Links" }, darkGroups.Pills.Select(p => p.Name));
        Assert.Equal(new[] { "#FF8A70", "#7FD1C7", "#F2C14E", "#FF8A70", "#7FD1C7", "#F2C14E" }, darkGroups.Pills.Select(p => p.Hex));
        Assert.Equal(new[] { "#B4472F", "#2F7F76", "#B4472F" }, card.DesignGroupRows.Single(r => !r.IsDark).Pills.Take(3).Select(p => p.Hex));
        Assert.Equal("#EEF4F8", card.DesignGroupRows.Single(r => !r.IsDark).SurfaceHex);
        Assert.Equal("gilt auch für Schlagworte · Gruppen „Vom Design“ bekommen sie der Reihe nach", card.GroupPaletteHint);

        // Umschalten (auch per Klick auf die andere Zeile): Vorschau und Hervorhebung wechseln mit
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        card.DesignGroupRows.Single(r => r.IsDark).SelectCommand.Execute(null);
        Assert.True(card.ShowsDark);
        Assert.True(card.IsPreviewDark);
        Assert.True(card.DesignColorRows[0].IsActive);
        Assert.False(card.DesignGroupRows.Single(r => !r.IsDark).IsActive);
        Assert.Contains(nameof(MarketplaceItemRow.PreviewSurface), changed);
        Assert.Equal(0x0B, card.PreviewSurface!.Value.R);
        card.IsPreviewLight = true;
        Assert.Equal(0xEE, card.PreviewSurface!.Value.R);

        // Schrift: fehlende Schriften nennen den Ersatz
        Assert.Equal("Fehlt Sans · fehlt hier, es gilt Segoe UI", card.DesignFontText);
        Assert.Equal("JetBrains Mono · fehlt hier, es gilt Cascadia Mono", card.DesignMonoText);
        Assert.True(card.IsDesignFontMissing);
        Assert.StartsWith("JetBrains Mono, ", card.DesignMonoFont, StringComparison.Ordinal);
        Assert.Equal("Aa Server neu starten 0123", MarketplaceItemRow.FontSample);
        theme.MissingFonts.Clear();
        Assert.Equal("JetBrains Mono", card.DesignMonoText);

        // Form und Hintergrund
        Assert.Equal("Ecken 12 px · Karten 12 px", card.DesignFormText);
        Assert.Equal(12, card.PreviewCornerRadius);
        Assert.Equal("Transparent · Deckkraft 80 % · Milchglas 20 %", card.DesignBackgroundText);
        Assert.Equal(0.8, card.PreviewOpacity, 3);
        Assert.True(card.PreviewFrost > 0);
        Assert.False(card.UsesWindowsAccent);
        Assert.Equal("Eigene Akzentfarbe (nicht die von Windows)", card.AccentText);

        // Aktionen: nicht installiert → „Installieren und anwenden“; danach aktiv
        Assert.Equal("Installieren und anwenden", card.ApplyText);
        Assert.True(card.ShowApply);
        Assert.False(card.IsActiveDesign);
        Assert.False(card.IsInstalled);
        await vm.Page.InstallAndApplyAsync(card);
        var open = vm.Page.OpenItem!;
        Assert.True(vm.Page.IsDetailOpen);
        Assert.True(open.IsInstalled);
        Assert.True(open.IsActiveDesign);
        Assert.False(open.ShowApply);
        Assert.False(open.ShowsDark);                           // gewählte Variante bleibt
    }

    [Fact]
    public async Task DesignDetailWithoutPairOrGroupColors()
    {
        var host = Host();                                      // Ozean: nur dunkel, ohne Gruppenfarben, Akzent von Windows
        host.Downloads[DefaultRaw + "Designs/Ozean.design.json"] = """{ "Name": "Ozean", "Theme": "Dark", "PanelColor": "#0B2233", "WindowsAccent": true, "Backdrop": "Solid" }""";
        ((FakeTheme)host.Theme).IsDark = false;
        var vm = new MarketplaceWindowViewModel(host, _ => { });
        await vm.Page.LoadAsync(force: false);
        vm.Tab = "Design";
        var card = Assert.Single(vm.Page.Cards);
        card.OpenCommand.Execute(null);

        Assert.True(card.ShowsDark);                            // einzige Variante, auch wenn die App hell läuft
        card.IsPreviewLight = true;
        Assert.True(card.ShowsDark);
        Assert.Equal("Dieses Design gibt es nur dunkel", card.VariantHint);
        var row = Assert.Single(card.DesignColorRows);
        Assert.True(row.IsActive);
        Assert.Equal("Akzent (Windows)", row.Swatches[^1].Name);
        Assert.All(Assert.Single(card.DesignGroupRows).Pills, p => Assert.Equal(card.DesignColorRows[0].Swatches[4].Hex, p.Hex)); // neutral: Textfarbe
        Assert.Equal("keine Gruppenfarben – Gruppen behalten ihre eigene Farbe", card.GroupPaletteHint);
        Assert.Equal("Ecken wie in der App eingestellt", card.DesignFormText);
        Assert.Equal("Vollton (deckend)", card.DesignBackgroundText);
        Assert.Equal(1.0, card.PreviewOpacity);
        Assert.Equal(0, card.PreviewFrost);
        Assert.True(card.UsesWindowsAccent);
        Assert.Equal("Windows-Akzentfarbe wird übernommen", card.AccentText);
        Assert.Equal("Cascadia Mono (Schrift der App)", card.DesignMonoText);
    }

    [Fact]
    public async Task DesignDownloadSkipsPreviewAndOffersToApply()
    {
        var host = Host();
        host.Settings.Panels.Add(new PanelConfig());
        AppSettings? applied = null;
        var vm = new MarketplaceWindowViewModel(host, s => applied = s);
        await vm.Page.LoadAsync(force: false);
        vm.Tab = "Design";
        await vm.Page.InstallAsync(vm.Page.AllItems.ToList());

        var confirms = host.FakeDialogs.Messages.Where(m => m.Kind == "confirm").ToList();
        var single = Assert.Single(confirms);                   // keine Vorschau-Rückfrage, nur: „jetzt anwenden?“
        Assert.Contains("Ozean", single.Message, StringComparison.Ordinal);
        Assert.Equal("#0B2233", applied!.Panels[0].PanelColor);   // angewendet
        Assert.Equal("Ozean", Appearance.Current(applied)?.Name);
    }

    [Fact]
    public async Task DesignAreaHasOwnNavigationCategoriesAndTryOut()
    {
        var host = Host();
        host.Settings.Panels.Add(new PanelConfig());
        Appearance.Apply(host.Settings, Appearance.Presets[0]);
        var applied = new List<AppSettings>();
        var vm = new MarketplaceWindowViewModel(host, s => applied.Add(s));
        await vm.Page.LoadAsync(force: false);

        vm.Tab = "designs";                                     // Verweis aus den Einstellungen
        Assert.True(vm.IsDesignArea);
        Assert.Equal("Design", vm.Tab);
        Assert.Equal(new[] { "Design", "DesignInstalled", "Sources" }, vm.Tabs.Select(t => t.Key));
        var card = Assert.Single(vm.Page.Cards);
        Assert.Equal("Natur", card.CategoryName);               // Kategorie aus der Datei, übersetzt
        Assert.Equal("Natur", Assert.Single(vm.Page.Categories).Name);
        Assert.NotEmpty(card.DesignColorRows);

        // Ausprobieren: angewendet, aber nicht installiert; „Zurück“ stellt das vorherige Aussehen wieder her
        card.TryCommand.Execute(null);
        Assert.True(vm.Page.IsTrying);
        Assert.Equal("#0B2233", applied[^1].Panels[0].PanelColor);
        Assert.False(card.IsInstalled);
        vm.Page.RevertTryCommand.Execute(null);
        Assert.False(vm.Page.IsTrying);
        Assert.Equal("fluent", applied[^1].DesignKey);
        Assert.Equal(string.Empty, applied[^1].Panels[0].PanelColor);

        // Ausprobieren und Fenster schließen: ebenfalls zurück
        card.TryCommand.Execute(null);
        vm.Closing();
        Assert.Equal("fluent", applied[^1].DesignKey);

        // Herunterladen und anwenden: ohne Rückfrage, danach aktiv
        await vm.Page.InstallAndApplyAsync(card);
        Assert.True(card.IsInstalled);
        Assert.Equal("#0B2233", applied[^1].Panels[0].PanelColor);
        Assert.StartsWith("market:", applied[^1].DesignKey, StringComparison.Ordinal);
        Assert.DoesNotContain(host.FakeDialogs.Messages, m => m.Kind == "confirm");

        vm.IsItemArea = true;
        Assert.Equal("All", vm.Tab);
    }
}
