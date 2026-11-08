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

public class MoveTopLevelTests
{
    [Fact]
    public void FixedAndUserGroupsReorder()
    {
        var s = new AppSettings { CategoryOrder = { "A", "A/x", "B", "C" } };
        Assert.Equal(FixedGroups.Favorites, FixedGroups.MoveTopLevel(s, FixedGroups.Favorites, "B", after: true));
        Assert.Equal(new[] { FixedGroups.Recent, "A", "B", FixedGroups.Favorites, "C", FixedGroups.Custom },
            FixedGroups.Compose(s, new[] { "A", "B", "C" }));

        // Eigene Gruppe vor eine feste Gruppe
        Assert.Equal("C", FixedGroups.MoveTopLevel(s, "C", FixedGroups.Recent, after: false));
        Assert.Equal(new[] { "C", "A", "A/x", "B" }, GroupManager.All(s));
        Assert.Equal(FixedGroups.Recent, FixedGroups.Compose(s, new[] { "C", "A", "B" })[1]);

        // Untergruppe neben eine feste Gruppe: wird zur obersten Ebene
        Assert.Equal("x", FixedGroups.MoveTopLevel(s, "A/x", FixedGroups.Custom, after: false));
        Assert.Contains("x", GroupManager.All(s));
        Assert.DoesNotContain("A/x", GroupManager.All(s));
        var top = GroupManager.All(s).Where(p => GroupManager.ParentOf(p) == null).ToList();
        var full = FixedGroups.Compose(s, top);
        Assert.Equal(full.IndexOf("x") + 1, full.IndexOf(FixedGroups.Custom));
    }
}

public class PanelBackdropTests
{
    [Fact]
    public void RetiredBackdropsBecomeClearGlass()
    {
        foreach (var old in new[] { PanelBackdrop.Aero, PanelBackdrop.Acrylic, PanelBackdrop.Mica })
        {
            var p = new PanelConfig { Backdrop = old, Acrylic = true };
            p.Normalize();
            Assert.Equal(PanelBackdrop.Clear, p.Backdrop);
            Assert.False(p.Acrylic);
        }
        var legacy = new PanelConfig { Acrylic = true };
        legacy.Normalize();
        Assert.Equal(PanelBackdrop.Clear, legacy.Backdrop);
    }

    [Fact]
    public void EffectiveIsClearGlassEverywhere()
    {
        Assert.Equal(PanelBackdrop.Clear, PanelBackdrops.Effective(PanelBackdrop.Acrylic, 22631, false));
        Assert.Equal(PanelBackdrop.Clear, PanelBackdrops.Effective(PanelBackdrop.Mica, 26100, true));
        Assert.False(PanelBackdrops.NeedsSystemWindow(PanelBackdrop.Clear));
        Assert.True(PanelBackdrops.SupportsFrost(PanelBackdrop.Clear));
    }

    [Fact]
    public void GlassFallsBackToClearGlass()
    {
        Assert.Equal(PanelBackdrop.Glass, PanelBackdrops.Effective(PanelBackdrop.Glass, 22631, false, transparencyEffects: true));
        Assert.Equal(PanelBackdrop.Clear, PanelBackdrops.Effective(PanelBackdrop.Glass, 19045, false));                         // Windows 10
        Assert.Equal(PanelBackdrop.Clear, PanelBackdrops.Effective(PanelBackdrop.Glass, 26100, true));                          // Kontrastdesign
        Assert.Equal(PanelBackdrop.Clear, PanelBackdrops.Effective(PanelBackdrop.Glass, 26100, false, transparencyEffects: false)); // Transparenzeffekte aus
        Assert.True(PanelBackdrops.NeedsSystemWindow(PanelBackdrop.Glass));
        var p = new PanelConfig { Backdrop = PanelBackdrop.Glass, GlassBlur = 500, GlassSaturation = -3 };
        p.Normalize();
        Assert.Equal(PanelBackdrop.Glass, p.Backdrop);
        Assert.Equal((100, 0), (p.GlassBlur, p.GlassSaturation));
        Assert.Equal(p.GlassBlur, p.Clone().GlassBlur);
    }

    [Fact]
    public void FrostBrightnessStaysInItsRange()
    {
        Assert.Equal(0, PanelBackdrops.FrostBrightness(0, darkSurface: true));
        Assert.Equal(0.40, PanelBackdrops.FrostBrightness(1, darkSurface: true), 2);
        Assert.Equal(0.60, PanelBackdrops.FrostBrightness(60, darkSurface: true), 3);
        Assert.Equal(0.50, PanelBackdrops.FrostBrightness(1, darkSurface: false), 2);
        Assert.Equal(0.70, PanelBackdrops.FrostBrightness(99, darkSurface: false), 3);
    }
}
