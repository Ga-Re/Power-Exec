using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Arbeitsbereiche: Auswahl der Gruppen, Wechsel ohne Datenverlust.</summary>
public class WorkspaceTests
{
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
    public void WorkspacesSwitchKeepData()
    {
        // Ab 0.13: Ordner/Gruppen global; Favoriten, Profile, Verlauf und Gruppenauswahl je Arbeitsbereich
        var s = new AppSettings();
        WorkspaceManager.EnsureInitialized(s);
        s.Folders.Add(new FolderConfig { Path = @"C:\KundeA", Group = "A" });
        s.Favorites.Add(@"C:\KundeA\x.ps1");
        s.Entries.Add(new EntryConfig { Path = @"C:\KundeA\x.ps1", Notes = "global", Profiles = [new LaunchProfile { Name = "P1", IsDefault = true }] });

        var b = WorkspaceManager.Create(s, "Kunde B", copyCurrent: false);
        Assert.True(WorkspaceManager.Switch(s, b.Name));
        Assert.Single(s.Folders);                          // global
        Assert.Empty(s.Favorites);                         // je Arbeitsbereich
        Assert.Empty(s.FindEntry(@"C:\KundeA\x.ps1")!.Profiles);
        Assert.Equal("global", s.FindEntry(@"C:\KundeA\x.ps1")!.Notes);
        WorkspaceManager.SetGroupHidden(WorkspaceManager.Active(s), "A", true);
        Assert.False(WorkspaceManager.ShowsGroup(s, "A/Unter"));

        Assert.True(WorkspaceManager.Switch(s, "standard"));
        Assert.Single(s.Favorites);
        Assert.Equal("P1", Assert.Single(s.FindEntry(@"C:\KundeA\x.ps1")!.Profiles).Name);
        Assert.True(WorkspaceManager.ShowsGroup(s, "A"));

        Assert.True(WorkspaceManager.Switch(s, "Kunde B"));
        Assert.False(WorkspaceManager.Delete(s, "Kunde B")); // aktiv
        Assert.True(WorkspaceManager.Rename(s, "Kunde B", "Kunde Beta"));
        Assert.Equal("Kunde Beta", s.ActiveWorkspace);
        var copy = WorkspaceManager.Create(s, "Kunde Beta", copyCurrent: true);
        Assert.Equal("Kunde Beta 2", copy.Name);
        Assert.Single(copy.HiddenGroups);
        Assert.True(WorkspaceManager.Delete(s, "Standard"));
    }
}
