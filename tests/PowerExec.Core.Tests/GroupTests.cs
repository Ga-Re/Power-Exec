using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

public class GroupTests
{
    [Fact]
    public void CreateMakesUniqueNames()
    {
        var s = new AppSettings();
        Assert.Equal("Tools", GroupManager.Create(s, "Tools"));
        Assert.Equal("tools 2", GroupManager.Create(s, "tools"));
        Assert.Equal(new[] { "Tools", "tools 2" }, GroupManager.All(s));
    }

    [Fact]
    public void AssignAndRemoveKeepsOtherEntrySettings()
    {
        var s = new AppSettings();
        GroupManager.Assign(s, @"C:\a.ps1", "Admin");
        Assert.Equal("Admin", GroupManager.GroupOf(s, @"C:\A.PS1"));
        Assert.Contains("Admin", s.CategoryOrder);

        // Leere Konfiguration verschwindet beim Herausnehmen
        GroupManager.Assign(s, @"C:\a.ps1", null);
        Assert.Empty(s.Entries);

        // Mit Notiz bleibt der Eintrag erhalten
        s.Entries.Add(new EntryConfig { Path = @"C:\b.ps1", Notes = "Achtung", Category = "Admin" });
        GroupManager.Assign(s, @"C:\b.ps1", "");
        Assert.Equal("Achtung", Assert.Single(s.Entries).Notes);
    }

    [Fact]
    public void RenameUpdatesEntriesOrderAndPanels()
    {
        var s = new AppSettings();
        GroupManager.Assign(s, @"C:\a.ps1", "Alt");
        s.CustomOrder["cat:Alt"] = [@"C:\a.ps1"];
        s.CollapsedGroups.Add("cat:Alt");
        s.Panels.Add(new PanelConfig { Categories = ["Alt"] });

        Assert.True(GroupManager.Rename(s, "Alt", "Neu"));
        Assert.Equal("Neu", GroupManager.GroupOf(s, @"C:\a.ps1"));
        Assert.True(s.CustomOrder.ContainsKey("cat:Neu"));
        Assert.Contains("cat:Neu", s.CollapsedGroups);
        Assert.Equal("Neu", s.Panels[0].Categories[0]);

        GroupManager.Create(s, "Andere");
        Assert.False(GroupManager.Rename(s, "Neu", "andere")); // Name schon vergeben
    }

    [Fact]
    public void DeleteReturnsItemsToTheirFolder()
    {
        var s = new AppSettings();
        GroupManager.Assign(s, @"C:\a.ps1", "X");
        GroupManager.Delete(s, "X");
        Assert.Empty(GroupManager.All(s));
        Assert.Equal(string.Empty, GroupManager.GroupOf(s, @"C:\a.ps1"));
    }

    [Fact]
    public void MoveChangesOrder()
    {
        var s = new AppSettings();
        GroupManager.Create(s, "A");
        GroupManager.Create(s, "B");
        GroupManager.Create(s, "C");
        GroupManager.Move(s, "C", -1);
        Assert.Equal(new[] { "A", "C", "B" }, GroupManager.All(s));
        GroupManager.Move(s, "A", -1); // schon oben
        Assert.Equal(new[] { "A", "C", "B" }, GroupManager.All(s));
    }

    [Fact]
    public void ClassifyDroppedPaths()
    {
        var folder = DropClassifier.Classify(@"\\srv\share\Tools\", p => p == @"\\srv\share\Tools", _ => null);
        Assert.Equal(CustomEntryKind.Folder, folder.Kind);
        Assert.Equal("Tools", folder.Name);

        var url = DropClassifier.Classify(@"C:\Users\x\Desktop\Wiki.url", _ => false, _ => "[InternetShortcut]\r\nURL=https://wiki.local/start\r\n");
        Assert.Equal(CustomEntryKind.Url, url.Kind);
        Assert.Equal("https://wiki.local/start", url.Target);
        Assert.Equal("Wiki", url.Name);

        var file = DropClassifier.Classify(@"C:\Tools\Reset.ps1", _ => false, _ => null);
        Assert.Equal(CustomEntryKind.File, file.Kind);

        Assert.Null(DropClassifier.FromUrl("kein link"));
        Assert.Equal("intranet.local", DropClassifier.FromUrl("https://intranet.local/a")!.Name);
        Assert.Equal("Startseite", DropClassifier.FromUrl("https://intranet.local/a\n", "Startseite")!.Name);
    }

    [Fact]
    public void AddDroppedReusesEntriesAndAssignsGroup()
    {
        var s = new AppSettings();
        var targets = new[]
        {
            new DroppedTarget(CustomEntryKind.File, @"C:\Tools\Reset.ps1", "Reset.ps1"),
            new DroppedTarget(CustomEntryKind.File, @"C:\Scripts\Scanned.ps1", "Scanned.ps1"),
            new DroppedTarget(CustomEntryKind.Url, "https://wiki", "wiki")
        };
        var keys = DropClassifier.AddToSettings(s, targets, "Tools", p => p.StartsWith(@"C:\Scripts"));
        Assert.Equal(2, s.CustomEntries.Count); // die gescannte Datei wird nur zugeordnet
        Assert.Equal(@"C:\Tools\Reset.ps1", keys[0]); // Datei-Eintrag: Schlüssel ist der Pfad
        Assert.StartsWith("custom:", keys[2]);
        Assert.All(keys, k => Assert.Equal("Tools", GroupManager.GroupOf(s, k)));

        DropClassifier.AddToSettings(s, targets.Take(1), "Tools", _ => false);
        Assert.Equal(2, s.CustomEntries.Count); // kein Duplikat
    }

    [Fact]
    public void FileEntryBehavesLikeScannedFile()
    {
        var item = new CustomEntry { Kind = CustomEntryKind.File, Target = @"C:\Tools\Reset.ps1" }.ToLaunchItem();
        Assert.Equal(@"C:\Tools\Reset.ps1", item.FullPath);
        Assert.True(item.IsPowerShell);
        Assert.True(item.SupportsRemote);
        Assert.Null(item.LaunchTarget);
        var spec = Execution.CommandBuilder.BuildLocal(new Execution.ExecutionRequest { Item = item });
        Assert.Equal(Path.GetDirectoryName(@"C:\Tools\Reset.ps1"), spec.WorkingDirectory);
    }
}

public class SubgroupTests
{
    private static AppSettings Sample()
    {
        var s = new AppSettings();
        GroupManager.Create(s, "Server");
        GroupManager.Create(s, "Exchange", "Server");
        GroupManager.Create(s, "Clients");
        GroupManager.Assign(s, @"C:\a.ps1", "Server/Exchange");
        return s;
    }

    [Fact]
    public void AllListsParentsBeforeChildren()
    {
        var s = new AppSettings();
        s.Entries.Add(new EntryConfig { Path = "x", Category = " A // B " });
        Assert.Equal(new[] { "A", "A/B" }, GroupManager.All(s));
        Assert.Equal("A/B", GroupManager.GroupOf(s, "x"));
    }

    [Fact]
    public void CreateSubgroupPlacesItAfterSiblings()
    {
        var s = Sample();
        Assert.Equal("Server/Mail", GroupManager.Create(s, "Mail", "Server"));
        Assert.Equal(new[] { "Server", "Server/Exchange", "Server/Mail", "Clients" }, GroupManager.All(s));
        Assert.Equal(new[] { "Server/Exchange", "Server/Mail" }, GroupManager.ChildrenOf(s, "Server"));
        Assert.Equal("a-b", GroupManager.Create(s, "a/b")); // kein Trenner im Namen
    }

    [Fact]
    public void RenameParentMovesChildren()
    {
        var s = Sample();
        s.CollapsedGroups.Add("cat:Server/Exchange");
        Assert.True(GroupManager.Rename(s, "Server", "Srv"));
        Assert.Equal("Srv/Exchange", GroupManager.GroupOf(s, @"C:\a.ps1"));
        Assert.Contains("cat:Srv/Exchange", s.CollapsedGroups);
        Assert.Equal(new[] { "Srv", "Srv/Exchange", "Clients" }, GroupManager.All(s));
    }

    [Fact]
    public void DeleteRemovesSubtree()
    {
        var s = Sample();
        Assert.Equal(1, GroupManager.DescendantCount(s, "Server"));
        GroupManager.Delete(s, "Server");
        Assert.Equal(new[] { "Clients" }, GroupManager.All(s));
        Assert.Equal(string.Empty, GroupManager.GroupOf(s, @"C:\a.ps1"));
    }

    [Fact]
    public void MoveGroupIntoBeforeAfter()
    {
        var s = Sample();
        // Clients in Server hinein
        Assert.Equal("Server/Clients", GroupManager.MoveGroup(s, "Clients", "Server", GroupDropPosition.Into));
        Assert.Equal(new[] { "Server", "Server/Exchange", "Server/Clients" }, GroupManager.All(s));

        // Exchange (mit Eintrag) vor Server auf die oberste Ebene
        Assert.Equal("Exchange", GroupManager.MoveGroup(s, "Server/Exchange", "Server", GroupDropPosition.Before));
        Assert.Equal(new[] { "Exchange", "Server", "Server/Clients" }, GroupManager.All(s));
        Assert.Equal("Exchange", GroupManager.GroupOf(s, @"C:\a.ps1"));

        // Nicht in die eigene Untergruppe
        Assert.Null(GroupManager.MoveGroup(s, "Server", "Server/Clients", GroupDropPosition.Into));

        // Reihenfolge unter Geschwistern
        GroupManager.Move(s, "Exchange", 1);
        Assert.Equal(new[] { "Server", "Server/Clients", "Exchange" }, GroupManager.All(s));
    }

    [Fact]
    public void LayoutMigratesFromCompact()
    {
        var p = new PanelConfig { Compact = true };
        p.Normalize();
        Assert.Equal(ItemLayout.List, p.Layout);
        Assert.Equal(ItemSize.Small, p.ItemSize);
        Assert.False(p.Compact);
        Assert.True(p.ShowRecent && p.ShowFavorites);
    }
}
