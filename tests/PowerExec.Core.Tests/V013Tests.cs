using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

public class V013Tests
{
    private static AppSettings Load(string json)
    {
        var file = Path.Combine(Path.GetTempPath(), "pe-v013-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, json);
            return new SettingsStore(file).Load();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void FoldersBecomeSmartGroups()
    {
        var s = Load("""
        {"Folders":[{"Path":"C:\\Scripts\\AD"},{"Path":"\\\\srv\\tools\\AD"},{"Path":"C:\\Hidden","ShowGroup":false}],
         "CategoryOrder":["Favoriten-Ersatz"],
         "Panels":[{"Name":"A","Compact":true},{"Name":"B"}],
         "CustomEntries":[{"Id":"x","Kind":"Url","Target":"https://a","SingleClick":true}]}
        """);
        Assert.Equal(14, s.DataVersion);
        Assert.Equal(new[] { "AD", "AD 2", "Hidden" }, s.Folders.Select(f => f.Group));
        Assert.Contains("AD 2", GroupManager.All(s));
        Assert.Single(s.Panels);                                  // nur noch ein Panel
        Assert.Equal(ItemSize.Small, s.Panels[0].ItemSize);
        Assert.True(s.FindEntry("custom:x")!.SingleClick);        // Ein-Klick wandert in die Eintragseinstellungen
        Assert.False(WorkspaceManager.ShowsGroup(s, "Hidden"));   // ausgeblendeter Ordner → Gruppe ausgeblendet
        Assert.True(WorkspaceManager.ShowsGroup(s, "AD"));
    }

    [Fact]
    public void LegacyWorkspacesAreMerged()
    {
        var s = Load("""
        {"ActiveWorkspace":"A",
         "Folders":[{"Path":"C:\\A"}],
         "Entries":[{"Path":"C:\\A\\x.ps1","Notes":"n","Profiles":[{"Name":"PA","IsDefault":true}]}],
         "Favorites":["C:\\A\\x.ps1"],
         "Workspaces":[
           {"Name":"A"},
           {"Name":"B","Folders":[{"Path":"C:\\B"}],"Favorites":["C:\\B\\y.ps1"],
            "Entries":[{"Path":"C:\\B\\y.ps1","Profiles":[{"Name":"PB","IsDefault":true}]}],
            "CustomEntries":[{"Id":"b1","Kind":"Url","Target":"https://b"}]}]}
        """);
        Assert.Equal(2, s.Folders.Count);                         // global zusammengeführt
        Assert.Single(s.CustomEntries);
        Assert.Equal("PA", s.FindEntry("C:\\A\\x.ps1")!.Profiles.Single().Name);
        Assert.Empty(s.FindEntry("C:\\B\\y.ps1")!.Profiles);      // Profil gehört zu B
        Assert.False(WorkspaceManager.ShowsGroup(s, "B"));        // in A ist B ausgeblendet
        Assert.True(WorkspaceManager.ShowsGroup(s, "A"));

        WorkspaceManager.Switch(s, "B");
        Assert.Equal("PB", s.FindEntry("C:\\B\\y.ps1")!.Profiles.Single().Name);
        Assert.Equal(new[] { "C:\\B\\y.ps1" }, s.Favorites);
        Assert.False(WorkspaceManager.ShowsGroup(s, "A"));
        Assert.True(WorkspaceManager.ShowsGroup(s, "B"));
        Assert.All(s.Workspaces, w => Assert.Empty(w.Folders)); // Altdaten entfernt
    }

    [Fact]
    public void EffectiveGroupWithSubfolders()
    {
        var s = new AppSettings();
        var f = SmartGroups.Add(s, @"C:\Tools");
        Assert.Equal("Tools", f.Group);
        Assert.Equal("Tools", SmartGroups.EffectiveGroup(s, @"C:\Tools\AD\Reset.ps1", @"C:\Tools"));
        f.SubfoldersAsGroups = true;
        Assert.Equal("Tools/AD", SmartGroups.EffectiveGroup(s, @"C:\Tools\AD\Reset.ps1", @"C:\Tools"));
        Assert.Equal("Tools/AD/Sub", SmartGroups.EffectiveGroup(s, @"C:\Tools\AD\Sub\x.ps1", @"C:\Tools"));
        Assert.Equal("Tools", SmartGroups.EffectiveGroup(s, @"C:\Tools\x.ps1", @"C:\Tools"));
        // ausdrücklich verschoben → nur noch in der Zielgruppe
        GroupManager.Assign(s, @"C:\Tools\AD\Reset.ps1", "Favorit-Gruppe");
        Assert.Equal("Favorit-Gruppe", SmartGroups.EffectiveGroup(s, @"C:\Tools\AD\Reset.ps1", @"C:\Tools"));
        Assert.Equal(string.Empty, SmartGroups.EffectiveGroup(s, "custom:1", "custom:"));
    }

    [Fact]
    public void RenameAndDeleteSmartGroup()
    {
        var s = new AppSettings();
        WorkspaceManager.EnsureInitialized(s);
        SmartGroups.Add(s, @"C:\Tools", "Werkzeuge");
        WorkspaceManager.SetGroupHidden(WorkspaceManager.Active(s), "Werkzeuge", true);
        Assert.True(GroupManager.Rename(s, "Werkzeuge", "Tools"));
        Assert.Equal("Tools", s.Folders[0].Group);
        Assert.Equal("Tools", WorkspaceManager.Active(s).HiddenGroups.Single());
        GroupManager.Delete(s, "Tools");
        Assert.Empty(s.Folders);
        Assert.Empty(WorkspaceManager.Active(s).HiddenGroups);
    }

    [Fact]
    public void HiddenParentHidesSubgroupsPerWorkspace()
    {
        var s = new AppSettings();
        WorkspaceManager.EnsureInitialized(s);
        GroupManager.Create(s, "Admin");
        GroupManager.Create(s, "Admin/AD");
        var other = WorkspaceManager.Create(s, "Kunde A", copyCurrent: false);
        WorkspaceManager.SetGroupHidden(WorkspaceManager.Active(s), "Admin", true);
        Assert.False(WorkspaceManager.ShowsGroup(s, "Admin/AD"));
        Assert.True(WorkspaceManager.ShowsGroupIn(other, "Admin/AD"));
        Assert.True(WorkspaceManager.ShowsGroup(s, string.Empty));
    }

    [Fact]
    public void PanelContentFilterIsDropped()
    {
        var p = new PanelConfig { Compact = true, Folders = { @"C:\x" }, Categories = { "A" } };
        p.Normalize();
        Assert.Empty(p.Folders);
        Assert.Empty(p.Categories);
        Assert.Equal(ItemSize.Small, p.ItemSize);
        Assert.True(p.ShowsFolder(PanelConfig.CustomEntriesKey));
    }

    [Fact]
    public void UngroupingKeepsSingleClickAndTags()
    {
        var s = new AppSettings();
        s.Entries.Add(new EntryConfig { Path = "custom:ad", SingleClick = true });
        s.Entries.Add(new EntryConfig { Path = "custom:x", Tags = { "netz" } });
        s.Entries.Add(new EntryConfig { Path = "custom:leer", Category = "A" });
        GroupManager.Assign(s, "custom:ad", null);
        GroupManager.Assign(s, "custom:x", "");
        GroupManager.Assign(s, "custom:leer", null);
        Assert.True(s.FindEntry("custom:ad")!.SingleClick);
        Assert.Equal("netz", s.FindEntry("custom:x")!.Tags.Single());
        Assert.Null(s.FindEntry("custom:leer"));
    }
}
