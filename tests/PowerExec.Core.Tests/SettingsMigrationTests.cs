using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Einstellungen älterer Versionen werden beim Einlesen übernommen.</summary>
public class SettingsMigrationTests
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
    public void LayoutMigration09()
    {
        var p = new PanelConfig { Layout = ItemLayout.Compact };
        p.Normalize();
        Assert.Equal(ItemLayout.List, p.Layout);
        Assert.Equal(ItemSize.Small, p.ItemSize);
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
    public void LegacyPanelSettingsAreMigrated()
    {
        var file = Path.Combine(Path.GetTempPath(), "pe-v07-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, """{"Edge":"Left","PanelWidth":520,"PanelColor":"#1B2A3D","Hotkey":"Ctrl+Alt+P","DefaultFilter":"Favorites"}""");
            var s = new SettingsStore(file).Load();
            var p = Assert.Single(s.Panels);
            Assert.Equal("Hauptpanel", p.Name);
            Assert.Equal(ScreenEdge.Left, p.Edge);
            Assert.Equal(520, p.PanelWidth);
            Assert.Equal("#1B2A3D", p.PanelColor);
            Assert.Equal("Ctrl+Alt+P", p.Hotkey);
            Assert.Equal(TypeFilter.Favorites, p.DefaultFilter);
            Assert.Equal("Standard", s.ActiveWorkspace);
            Assert.Single(s.Workspaces);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
