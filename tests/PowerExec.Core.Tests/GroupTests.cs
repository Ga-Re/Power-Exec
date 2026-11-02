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
