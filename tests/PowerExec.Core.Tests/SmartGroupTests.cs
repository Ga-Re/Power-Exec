using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

/// <summary>Smarte Gruppen: Ordner als Gruppe, Unterordner als Untergruppen.</summary>
public class SmartGroupTests
{
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
