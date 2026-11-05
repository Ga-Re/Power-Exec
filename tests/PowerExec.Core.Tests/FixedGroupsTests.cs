using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

/// <summary>Feste Gruppen: Reihenfolge unter den eigenen Gruppen und Sichtbarkeit je Arbeitsbereich.</summary>
public class FixedGroupsTests
{
    [Fact]
    public void DefaultOrderRecentFavoritesTopCustomBottom()
    {
        var s = new AppSettings();
        Assert.Equal(new[] { FixedGroups.Recent, FixedGroups.Favorites, "A", "B", FixedGroups.Custom },
            FixedGroups.Compose(s, new[] { "A", "B" }));
        Assert.Equal(new[] { FixedGroups.Recent, FixedGroups.Favorites, FixedGroups.Custom }, FixedGroups.Compose(s, Array.Empty<string>()));
    }

    [Fact]
    public void PositionsRoundTrip()
    {
        var s = new AppSettings();
        var order = new[] { "A", FixedGroups.Favorites, "B", FixedGroups.Custom, "C", FixedGroups.Recent };
        FixedGroups.SetPositions(s, order);
        Assert.Equal(order, FixedGroups.Compose(s, new[] { "A", "B", "C" }));
        // Neue Gruppe unten: „Zuletzt ausgeführt“ bleibt ganz unten
        Assert.Equal(FixedGroups.Recent, FixedGroups.Compose(s, new[] { "A", "B", "C", "D" }).Last());
        // Gruppe gelöscht: Position wird begrenzt, nichts geht verloren
        Assert.Equal(6 - 1, FixedGroups.Compose(s, new[] { "A", "B" }).Count);
    }

    [Fact]
    public void OldTogglesBecomeHiddenGroups()
    {
        var s = new AppSettings();
        WorkspaceManager.EnsureInitialized(s);
        s.Panels.Add(new PanelConfig { ShowFavorites = false });
        FixedGroups.MigrateToggles(s);
        Assert.False(WorkspaceManager.ShowsGroup(s, FixedGroups.Favorites));
        Assert.True(WorkspaceManager.ShowsGroup(s, FixedGroups.Recent));
        Assert.True(s.Panels[0].ShowFavorites);
    }
}

public class AskProfileTests
{
    [Fact]
    public void OnlyWithSeveralProfilesEntryOverridesGlobal()
    {
        var item = new Models.LaunchItem { FullPath = @"C:\S\a.ps1", RootFolder = @"C:\S", Type = Models.ItemType.PowerShell };
        var s = new AppSettings { AskProfile = true };
        Assert.False(Profiles.ProfileResolver.AsksProfile(s, item)); // nur das automatische Profil
        var e = new EntryConfig { Path = item.FullPath, Profiles = { new LaunchProfile { Name = "A", IsDefault = true }, new LaunchProfile { Name = "B" } } };
        s.Entries.Add(e);
        Assert.True(Profiles.ProfileResolver.AsksProfile(s, item));
        e.AskProfile = false;
        Assert.False(Profiles.ProfileResolver.AsksProfile(s, item));
        s.AskProfile = false;
        e.AskProfile = true;
        Assert.True(Profiles.ProfileResolver.AsksProfile(s, item));
        Assert.False(new EntryConfig { Path = "x", AskProfile = true }.IsEmpty);
    }
}
