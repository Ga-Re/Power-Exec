using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Designvorlagen (auch eigene), Vollton und Elementgröße je Gruppe.</summary>
public class AppearanceDesignTests
{
    [Fact]
    public void GlassPresetTurnsOnRealGlassWithBlurAndSaturation()
    {
        var s = new AppSettings { Panels = { new PanelConfig { GlassBlur = 10, GlassSaturation = 80 } } };
        var glass = Appearance.Presets.Single(p => p.Key == "glass");

        Appearance.Apply(s, glass);

        var panel = s.Panels[0];
        Assert.Equal(PanelBackdrop.Glass, panel.Backdrop);
        Assert.Equal(60, panel.GlassBlur);
        Assert.Equal(125, panel.GlassSaturation);
        Assert.Equal("glass", Appearance.Current(s)?.Key);
    }

    [Fact]
    public void NewPresetsCarryTheirColorsAndAccents()
    {
        var s = new AppSettings { Panels = { new PanelConfig() } };
        Appearance.Apply(s, Appearance.Presets.Single(p => p.Key == "oled"));
        Assert.Equal("#000000", s.Panels[0].PanelColor);
        Assert.Equal(PanelBackdrop.Solid, s.Panels[0].Backdrop);
        Assert.Equal(100, s.Panels[0].EffectiveOpacity);

        Assert.Equal("oled", Appearance.Current(s)?.Key);

        // Drei Grund-Designs (je Hintergrund eins); weitere liegen im Marktplatz
        Assert.Equal(new[] { "fluent", "glass", "oled" }, Appearance.Presets.Select(p => p.Key));
        Assert.Equal(string.Empty, Appearance.Presets.Single(p => p.Key == "fluent").AccentColor);
    }

    [Fact]
    public void CustomDesignRoundTripsAndIsRecognized()
    {
        var s = new AppSettings { Theme = ThemeMode.Dark, Panels = { new PanelConfig { PanelColor = "#123456", PanelOpacity = 77, Frost = 12, AccentColor = "#ABCDEF" } } };
        var mine = Appearance.Capture(s, s.Panels[0], "  Mein Look ");
        s.CustomDesigns.Add(mine);
        Assert.True(mine.IsCustom);
        Assert.Equal("Mein Look", mine.Name);
        Assert.Equal(mine.Key, Appearance.Current(s)?.Key);

        // gespeichert und wieder eingelesen
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        var restored = Assert.Single(back.CustomDesigns);
        Assert.Equal(mine, restored);

        // auf ein anderes Panel angewandt
        var other = new AppSettings { Panels = { new PanelConfig() } };
        Appearance.Apply(other, restored);
        Assert.Equal("#123456", other.Panels[0].PanelColor);
        Assert.Equal(77, other.Panels[0].PanelOpacity);
        Assert.Equal("#ABCDEF", other.Panels[0].AccentColor);
    }

    [Fact]
    public void SolidBackdropIsOpaqueWithoutFrostAndKeepsLayeredWindow()
    {
        Assert.False(PanelBackdrops.NeedsSystemWindow(PanelBackdrop.Solid));
        Assert.False(PanelBackdrops.SupportsFrost(PanelBackdrop.Solid));
        Assert.Equal(PanelBackdrop.Solid, PanelBackdrops.Effective(PanelBackdrop.Solid, 19045, highContrast: false));
        Assert.Equal(100, new PanelConfig { Backdrop = PanelBackdrop.Solid, PanelOpacity = 40 }.EffectiveOpacity);
        Assert.Equal(40, new PanelConfig { Backdrop = PanelBackdrop.Clear, PanelOpacity = 40 }.EffectiveOpacity);
        Assert.Equal("Vollton (deckend)", PanelBackdrops.Label(PanelBackdrop.Solid));
    }

    [Fact]
    public void GroupItemSizeIsStoredPerGroupAndFollowsRename()
    {
        var s = new AppSettings();
        Assert.Null(GroupManager.ItemSizeOf(s, "Admin"));
        GroupManager.SetItemSize(s, "Admin", ItemSize.Large);
        Assert.Equal(ItemSize.Large, GroupManager.ItemSizeOf(s, "admin"));
        GroupManager.SetItemSize(s, "Admin", null);
        Assert.Null(GroupManager.ItemSizeOf(s, "Admin"));
        Assert.Empty(s.GroupItemSizes);
    }

    [Fact]
    public void GroupStyleAndAccentAreCloned()
    {
        var c = new PanelConfig { GroupStyle = GroupCardStyle.Accent, AccentColor = "#88C0D0" }.Clone();
        Assert.Equal(GroupCardStyle.Accent, c.GroupStyle);
        Assert.Equal("#88C0D0", c.AccentColor);
    }

    [Fact]
    public void AccentIsReadableInBothThemes()
    {
        var darkBlue = new Rgb(0x00, 0x33, 0x99); // zu dunkel für das dunkle Design
        var dark = Appearance.Derive(darkBlue, dark: true);
        Assert.True(PanelColors.Contrast(dark.Accent, new Rgb(0x20, 0x20, 0x20)) >= 3.0);
        var yellow = new Rgb(0xFF, 0xD7, 0x00); // zu hell für das helle Design
        var light = Appearance.Derive(yellow, dark: false);
        Assert.True(PanelColors.Contrast(light.Accent, new Rgb(0xF3, 0xF3, 0xF3)) >= 3.0);
        Assert.True(PanelColors.Contrast(light.OnAccent, light.Accent) >= 3.0); // Schrift auf Akzent-Knöpfen
        Assert.Equal(new Rgb(0x10, 0x20, 0x30), Appearance.FromAbgr(0xFF302010));
    }

    [Fact]
    public void DesignPresetAppliesAndIsRecognized()
    {
        var s = new AppSettings();
        s.Panels.Add(new PanelConfig());
        var glass = Appearance.Presets.Single(p => p.Key == "glass");
        Appearance.Apply(s, glass);
        Assert.Equal(glass.Frost, s.Panels[0].Frost);
        Assert.Equal(PanelBackdrop.Glass, s.Panels[0].Backdrop); // echtes Glas, nicht Klarglas
        Assert.True(s.UseWindowsAccent);
        Assert.Equal("glass", Appearance.Current(s)?.Key);
        s.Panels[0].PanelOpacity = 60;
        Assert.Null(Appearance.Current(s));
    }
}
