using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI.Core.Tests;

/// <summary>Panel, Gruppen und Einträge ohne Oberfläche – mit echten Einstellungen und einem Fake-Host.</summary>
public class PanelViewModelTests
{
    private static FakePanelHost HostWithScripts()
    {
        var host = new FakePanelHost();
        SmartGroups.Add(host.Settings, @"C:\Scripts");
        host.Items.List.Add(Item(@"C:\Scripts\Get-DiskSpace.ps1", "Get-DiskSpace", ItemType.PowerShell, "Freier Platz je Laufwerk"));
        host.Items.List.Add(Item(@"C:\Scripts\Reset-ADUserPassword.ps1", "Reset-ADUserPassword", ItemType.PowerShell));
        host.Items.List.Add(Item(@"C:\Scripts\procexp.exe", "procexp", ItemType.Executable));
        return host;
    }

    private static LaunchItem Item(string path, string name, ItemType type, string synopsis = "") =>
        new() { FullPath = path, RootFolder = @"C:\Scripts", Type = type, DisplayName = name, Synopsis = synopsis };

    private static (FakePanelHost host, PanelViewModel vm) Panel()
    {
        var host = HostWithScripts();
        var vm = host.CreatePanel();
        vm.Rebuild();
        return (host, vm);
    }

    [Fact]
    public void FolderBecomesSmartGroupWithItemsAndFooter()
    {
        var (_, vm) = Panel();

        var g = Assert.Single(vm.AllGroups);
        Assert.Equal("Scripts", g.Title);
        Assert.True(g.IsSmartGroup);
        Assert.Equal(3, g.TotalCount);
        // alphabetisch, unabhängig von Groß-/Kleinschreibung
        Assert.Equal(new[] { "Get-DiskSpace", "procexp", "Reset-ADUserPassword" }, g.Items.Select(i => i.DisplayName));
        Assert.Equal(new[] { "PS1", "EXE", "PS1" }, g.Items.Select(i => i.BadgeText));
        Assert.StartsWith("3 Einträge", vm.Footer);
    }

    [Fact]
    public void TileDetailsAppearWhenOnlyTheGroupIsAGrid()
    {
        var (host, vm) = Panel();
        Assert.False(vm.IsGrid); // Panel als Liste
        var group = Assert.Single(vm.AllGroups);
        vm.Select(group.Items[0]);
        Assert.False(vm.ShowTileDetails); // Liste: Details klappen am Eintrag auf

        GroupManager.SetLayout(host.Settings, group.GroupPath!, ItemLayout.Grid);
        vm.GroupLayoutsChanged();
        Assert.True(group.IsGrid);
        Assert.True(vm.ShowTileDetails); // Kacheln dieser Gruppe: Details unten im Panel

        vm.Select(group.Items[0]); // Auswahl aufgehoben
        Assert.False(vm.ShowTileDetails);
    }

    [Fact]
    public void FuzzySearchFindsInitialsAndHighlights()
    {
        var (_, vm) = Panel();

        vm.SearchText = "gds";

        Assert.True(vm.IsSearching);
        var hit = Assert.Single(vm.AllItems);
        Assert.Equal("Get-DiskSpace", hit.DisplayName);
        Assert.Contains(hit.NameParts, p => p.IsHit);

        vm.ClearSearchCommand.Execute(null);
        Assert.Equal(3, vm.AllItems.Count());
    }

    [Fact]
    public void TypeFilterInSearchShowsChip()
    {
        var (_, vm) = Panel();

        vm.SearchText = "typ:exe";

        Assert.Equal(new[] { "procexp" }, vm.AllItems.Select(i => i.DisplayName));
        Assert.NotEmpty(vm.SearchChips);
    }

    [Fact]
    public void SearchFlatRanksForSpotlight()
    {
        var (_, vm) = Panel();

        var hits = vm.SearchFlat("pass", 8);

        Assert.Equal("Reset-ADUserPassword", hits.First().DisplayName);
    }

    [Fact]
    public void ItemCommandsGoThroughHostAndShell()
    {
        var (host, vm) = Panel();
        var item = vm.AllItems.First(i => i.DisplayName == "procexp");

        item.RunAdminCommand.Execute(null);
        item.CopyPathCommand.Execute(null);
        item.OpenLocationCommand.Execute(null);
        item.EditCommand.Execute(null);
        item.ToggleFavoriteCommand.Execute(null);

        Assert.Contains(host.Calls, c => c.StartsWith("RunProfile procexp Administrator", StringComparison.Ordinal) || c.StartsWith("OpenRunDialog procexp Administrator", StringComparison.Ordinal));
        Assert.Equal(new[] { @"C:\Scripts\procexp.exe" }, host.FakeShell.Clipboard);
        Assert.Equal((@"C:\Scripts\procexp.exe", false), Assert.Single(host.FakeShell.Opened));
        Assert.Contains("ToggleFavorite procexp", host.Calls);
    }

    [Fact]
    public void GroupWithoutColorHasTransparentCard()
    {
        var (_, vm) = Panel();
        var g = vm.AllGroups.Single();

        Assert.False(g.HasColor);
        Assert.Null(g.AccentColor);
        Assert.Equal(ArgbColor.Transparent, g.CardBackgroundColor);
        Assert.Contains(g.ColorChoices, c => c.Name.StartsWith("Eigene Farbe", StringComparison.Ordinal));
        Assert.Contains(g.ColorChoices, c => c.Name == "Keine Farbe");
        Assert.True(g.ColorChoices.First().IsCurrent);                     // ohne eigene Farbe: „Vom Design (automatisch)“
        Assert.True(g.ColorChoices.Where(c => c.IsRow).SelectMany(c => c.Swatches!).All(c => c.SwatchColor != null));
    }

    [Fact]
    public void GroupColorIsTintedAndReadableOnLightPanel()
    {
        var host = HostWithScripts();
        GroupManager.SetColor(host.Settings, "Scripts", "#FFEB3B"); // helles Gelb
        var vm = host.CreatePanel(c => c.PanelColor = "#FFFFFF");
        vm.Rebuild();
        var g = vm.AllGroups.Single();

        Assert.False(vm.IsDarkSurface);
        Assert.Equal(new ArgbColor(0x24, 0xFF, 0xEB, 0x3B), g.CardBackgroundColor);
        var accent = Assert.IsType<ArgbColor>(g.AccentColor);
        Assert.Equal(255, accent.A);
        Assert.NotEqual(new ArgbColor(255, 0xFF, 0xEB, 0x3B), accent); // auf Weiß abgedunkelt
        Assert.Contains(g.ColorChoices, c => c.Name.StartsWith("Eigene Farbe #FFEB3B", StringComparison.Ordinal) && c.IsCurrent);
    }

    [Fact]
    public void GroupCommandsGoThroughHost()
    {
        var (host, vm) = Panel();
        var g = vm.AllGroups.Single();

        g.SetColorCommand.Execute("#FF0000");
        g.CustomColorCommand.Execute(null);
        g.RescanCommand.Execute(null);
        g.HideFolderCommand.Execute(null);

        Assert.Contains("SetGroupColor Scripts #FF0000", host.Calls);
        Assert.Contains("PickGroupColor Scripts", host.Calls);
        Assert.Contains("HideGroupInWorkspace Scripts", host.Calls);
        Assert.Equal(1, host.Items.ScanCount);
    }

    [Fact]
    public void MultiSelectionCanBeSavedAsWorkflow()
    {
        var (host, vm) = Panel();
        var items = vm.AllItems.Take(2).ToList();
        foreach (var i in items) vm.ToggleChecked(i);
        Assert.Equal("2 ausgewählt", vm.MultiSelectionText);

        host.FakeDialogs.Answer = "Wartung";
        vm.SaveCheckedAsWorkflowCommand.Execute(null);

        Assert.Equal(new[] { "Als Ablauf speichern" }, host.FakeDialogs.Asked);
        Assert.Contains("CreateWorkflow Wartung 2", host.Calls);
        Assert.False(vm.HasMultiSelection);
    }

    [Fact]
    public void CancelledWorkflowNameKeepsSelection()
    {
        var (host, vm) = Panel();
        vm.ToggleChecked(vm.AllItems.First());
        host.FakeDialogs.Answer = null;

        vm.SaveCheckedAsWorkflowCommand.Execute(null);

        Assert.DoesNotContain(host.Calls, c => c.StartsWith("CreateWorkflow", StringComparison.Ordinal));
        Assert.True(vm.HasMultiSelection);
    }

    [Fact]
    public void ScrollBarAndHotkeyComeFromPanelConfig()
    {
        var host = HostWithScripts();
        var vm = host.CreatePanel(c => { c.ShowScrollBar = false; c.Hotkey = "Ctrl+Alt+Space"; });

        Assert.False(vm.ShowListScrollBar);
        Assert.Equal("Strg+Alt+Leertaste", vm.HotkeyHint);
    }

    [Fact]
    public void PeekShowsCodeFactsAndParameters()
    {
        var file = Path.Combine(Path.GetTempPath(), $"peek-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(file, "param([string]$Drive = 'C:')\nGet-PSDrive $Drive\n");
        try
        {
            var host = new FakePanelHost();
            var dir = Path.GetDirectoryName(file)!;
            SmartGroups.Add(host.Settings, dir);
            host.Items.List.Add(new LaunchItem
            {
                FullPath = file,
                RootFolder = dir,
                Type = ItemType.PowerShell,
                DisplayName = "Peek",
                RequiresAdmin = true,
                Parameters = new[] { new ScriptParameter { Name = "Drive", TypeName = "string", DefaultValueText = "'C:'" } }
            });
            var vm = host.CreatePanel();
            vm.Rebuild();

            var peek = new PeekViewModel(host, vm.AllItems.Single());

            Assert.True(peek.HasCode);
            Assert.Contains(peek.Code, s => s.Text.Contains("Get-PSDrive"));
            Assert.Contains(peek.Facts, f => f.Label == "Pfad" && f.Value == file);
            Assert.Contains(peek.Facts, f => f.Label == "Rechte");
            var p = Assert.Single(peek.Parameters);
            Assert.Equal("-Drive", p.Name);
            Assert.Contains("Vorgabe", p.Summary);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void EntryBadgesPerKind()
    {
        Assert.Equal("URL", EntryBadge.For(CustomEntryKind.Url));
        Assert.Equal("TXT", EntryBadge.For(CustomEntryKind.Snippet, language: SnippetLanguage.Text));
        Assert.Equal("PDF", EntryBadge.For(CustomEntryKind.File, @"C:\a\b.pdf"));
        Assert.Equal("DAT", EntryBadge.For(CustomEntryKind.File, @"C:\a\ohne"));
    }

    [Fact]
    public void UnreachableFolderShowsShortWarningAndErrorOnlyAtTheGroup()
    {
        var host = HostWithScripts();
        SmartGroups.Add(host.Settings, @"X:\Fehlt");
        host.Items.FolderStatuses = new[]
        {
            new UI.Services.FolderStatus(@"X:\Fehlt", 0, Array.Empty<PowerExec.Core.Scanning.ExcludedFile>(), "Ordner nicht gefunden oder nicht erreichbar.")
        };
        var vm = host.CreatePanel();
        vm.Rebuild();

        // Oben nur kurz; Pfad und Fehler im Tooltip und an der Gruppe
        Assert.Equal("1 Ordner nicht erreichbar", vm.FolderWarning);
        Assert.Contains(@"X:\Fehlt", vm.FolderWarningDetails, StringComparison.Ordinal);
        var missing = vm.AllGroups.Single(g => g.HasError);
        Assert.False(missing.ShowDropHint);                     // kein „Leer – hierher ziehen“ unter dem Fehler

        // Klick auf den Hinweis springt zur Gruppe
        GroupViewModel? jumped = null;
        vm.ScrollToGroupRequested += g => jumped = g;
        vm.ShowFolderWarningCommand.Execute(null);
        Assert.Same(missing, jumped);

        // Arbeitsbereich steht oben in der Auswahl, nicht noch einmal in der Fußzeile
        Assert.DoesNotContain(host.Settings.ActiveWorkspace, vm.Footer, StringComparison.Ordinal);
    }

    [Fact]
    public void SubgroupDoesNotDrawASecondFrameInItsParent()
    {
        var host = new FakePanelHost();
        var admin = GroupManager.Create(host.Settings, "Admin");
        var ad = GroupManager.Create(host.Settings, "AD", admin);
        GroupManager.SetColor(host.Settings, admin, "#2E7D32");
        var vm = host.CreatePanel();
        vm.Rebuild();
        var parent = vm.AllGroups.Single(g => g.GroupPath == admin);
        var child = vm.AllGroups.Single(g => g.GroupPath == ad);

        Assert.NotEqual(ArgbColor.Transparent, parent.CardBorderColor);
        Assert.NotEqual(ArgbColor.Transparent, parent.CardBackgroundColor);
        Assert.True(child.IsNested);
        Assert.Equal(ArgbColor.Transparent, child.CardBorderColor);     // kein Kasten im Kasten
        Assert.Equal(ArgbColor.Transparent, child.CardBackgroundColor); // erbt die Farbe: keine zweite Tönung

        // Eigene, andere Farbe: dezente eigene Tönung, aber weiterhin kein Rahmen
        GroupManager.SetColor(host.Settings, ad, "#1565C0");
        vm.Rebuild();
        child = vm.AllGroups.Single(g => g.GroupPath == ad);
        Assert.NotEqual(ArgbColor.Transparent, child.CardBackgroundColor);
        Assert.Equal(ArgbColor.Transparent, child.CardBorderColor);
    }

    [Fact]
    public void AdjacentSearchHitsAreMergedIntoOnePart()
    {
        var parts = TextPart.Split("Cleanup.cmd", new[] { "clnup" });
        Assert.Equal("Cleanup.cmd", string.Concat(parts.Select(p => p.Text)));
        Assert.Contains(parts, p => p.IsHit && p.Text.Length > 1);       // nicht Buchstabe für Buchstabe
        for (int i = 1; i < parts.Count; i++) Assert.False(parts[i].IsHit && parts[i - 1].IsHit, "zwei Treffer-Abschnitte nebeneinander");
    }

    [Fact]
    public void SearchShowsFlatRankedResultsAndLeavesGroupsUntouched()
    {
        var host = HostWithScripts();
        GroupManager.Create(host.Settings, "Admin");
        var vm = host.CreatePanel();
        vm.Rebuild();
        var groupsBefore = vm.Groups.ToList();
        var itemsBefore = vm.AllGroups.SelectMany(g => g.Items).ToList();

        // Suchen: flache Trefferliste, beste oben, mit der Gruppe als Hinweis – die Gruppen bleiben, wie sie sind
        vm.SearchText = "disk";
        Assert.True(vm.ShowsSearchResults);
        var first = vm.SearchResults[0];
        Assert.Equal("Get-DiskSpace", first.DisplayName);
        Assert.Equal("Scripts", first.GroupHint);
        Assert.Same(first, vm.VisibleItems.First());                   // ↑ ↓ und Enter gehen durch die Treffer
        Assert.Equal(vm.SearchResults, vm.AllItems);
        Assert.Equal(groupsBefore, vm.Groups);
        Assert.Equal(itemsBefore, vm.AllGroups.SelectMany(g => g.Items));

        // Weitertippen: dieselben Treffer-ViewModels
        vm.SearchText = "diskspace";
        Assert.Same(first, vm.SearchResults[0]);

        // Suche leeren: Gruppen und ihre Einträge sind dieselben Objekte – nichts wird neu aufgebaut
        vm.ClearSearchCommand.Execute(null);
        Assert.False(vm.ShowsSearchResults);
        Assert.Empty(vm.SearchResults);
        Assert.Equal(groupsBefore, vm.Groups);
        Assert.Equal(itemsBefore, vm.AllItems);
        Assert.Equal(0, vm.LastSyncStats.Inserted + vm.LastSyncStats.Moved + vm.LastSyncStats.Removed);

        // Ohne Treffer: leer, Hinweis „Keine Treffer“
        vm.SearchText = "zzzz-nichts";
        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.SearchResults);
    }
}
