using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

/// <summary>Palette, helle und dunkle Variante, Schrift und Gruppenfarben eines Designs.</summary>
public class DesignLookTests
{
    private static DesignPreset Rich() => new("custom:rich", "Reich", "", ThemeMode.Dark, "#2D1B2E", 90, 15, false, AccentColor: "#FF8A65", IsCustom: true,
        Palette: new DesignPalette(Card: "#3A2F44", Text: "f4eef6", Line: "kaputt"),
        Pair: new DesignVariant("#FBF5F2", "#C7472C", new DesignPalette(Card: "#FFFFFF")),
        FontFamily: " Bahnschrift ", MonoFontFamily: "Cascadia Code", GroupColors: "#FF8A70, 7FD1C7; nix #F2C66D", GroupColorsForTags: true);

    [Fact]
    public void ApplyingADesignSetsLookAndFollowsWindowsWhenItHasAPair()
    {
        var s = new AppSettings { Theme = ThemeMode.Light, Panels = { new PanelConfig() } };
        Appearance.Apply(s, Rich());

        Assert.Equal(ThemeMode.System, s.Theme);               // zwei Varianten: wechselt mit Windows mit
        Assert.Equal("Bahnschrift", s.Look.FontFamily);
        Assert.Equal("Cascadia Code", s.Look.MonoFontFamily);
        Assert.Equal(new[] { "#FF8A70", "#7FD1C7", "#F2C66D" }, s.Look.GroupColors); // ungültige Farben fallen weg
        Assert.Equal("#F4EEF6", s.Look.Palette!.Text);         // einheitlich geschrieben
        Assert.Equal(string.Empty, s.Look.Palette.Line);       // ungültig = abgeleitet
        Assert.False(s.Look.PairIsDark);                       // das Paar ist die helle Variante
        Assert.Equal("custom:rich", Appearance.Current(new AppSettings { Theme = s.Theme, Look = s.Look, Panels = s.Panels, CustomDesigns = { Rich() } })?.Key);
    }

    [Fact]
    public void EffectiveColorsPickTheVariantForTheRunningWindowsDesign()
    {
        var s = new AppSettings { Panels = { new PanelConfig() } };
        Appearance.Apply(s, Rich());

        var dark = Appearance.Effective(s, s.Panels[0], dark: true);
        var light = Appearance.Effective(s, s.Panels[0], dark: false);
        Assert.Equal("#2D1B2E", dark.PanelColor);
        Assert.Equal("#FBF5F2", light.PanelColor);
        Assert.Equal("#C7472C", light.AccentColor);
        Assert.Equal("#FFFFFF", light.Palette!.Card);

        // Fest auf dunkel gestellt: die helle Variante bleibt außen vor
        s.Theme = ThemeMode.Dark;
        Assert.Equal("#2D1B2E", Appearance.Effective(s, s.Panels[0], dark: false).PanelColor);
    }

    [Fact]
    public void ResolveDerivesWhatTheDesignLeavesOpenAndKeepsTextReadable()
    {
        var onlySurface = Appearance.Resolve(new DesignColors("#2D1B2E", "#FF8A65", null), dark: true);
        Assert.Equal("#FFFFFF", onlySurface.Text.Hex);
        Assert.True(PanelColors.Contrast(onlySurface.Text, onlySurface.Surface) >= 7);
        Assert.True(PanelColors.Contrast(onlySurface.TextSecondary, onlySurface.Surface) >= 4.5);
        Assert.True(PanelColors.Contrast(onlySurface.Accent, onlySurface.Surface) >= 3);
        Assert.NotEqual(onlySurface.Surface, onlySurface.Card);

        var set = Appearance.Resolve(new DesignColors("#2D1B2E", "", new DesignPalette(Card: "#3A2F44", Text: "#F4EEF6")), dark: true);
        Assert.Equal("#3A2F44", set.Card.Hex);
        Assert.Equal("#F4EEF6", set.Text.Hex);

        // Ohne eigene Fläche: die Fläche des hellen bzw. dunklen Designs
        Assert.Equal(PanelColors.LightSurface, Appearance.Resolve(new DesignColors("", "", null), dark: false).Surface);
    }

    [Fact]
    public void GroupsWithoutOwnColorTakeTheSeriesInOrder()
    {
        var s = new AppSettings { CategoryOrder = { "Admin", "Admin/AD", "Netzwerk", "Werkzeuge", "Links" } };
        Assert.Equal(string.Empty, GroupManager.ColorOf(s, "Netzwerk")); // ohne Reihe keine Farbe

        s.Look.GroupColors = new List<string> { "#111111", "#222222", "#333333" };
        Assert.Equal("#111111", GroupManager.ColorOf(s, "Admin"));
        Assert.Equal("#111111", GroupManager.ColorOf(s, "Admin/AD"));     // Untergruppe wie ihre Gruppe
        Assert.Equal("#222222", GroupManager.ColorOf(s, "Netzwerk"));
        Assert.Equal("#111111", GroupManager.ColorOf(s, "Links"));        // umlaufend
        Assert.Equal(string.Empty, GroupManager.OwnColorOf(s, "Netzwerk")); // die Reihe ist keine eigene Farbe

        GroupManager.SetColor(s, "Netzwerk", "#ABCDEF");
        Assert.Equal("#ABCDEF", GroupManager.ColorOf(s, "Netzwerk"));     // eigene Farbe gewinnt
    }

    [Fact]
    public void TagsUseTheSeriesOnlyWhenTheDesignSaysSo()
    {
        var s = new AppSettings();
        s.Look.GroupColors = new List<string> { "#111111", "#222222" };
        Assert.Equal(string.Empty, Tags.ColorOf(s, "server"));

        s.Look.GroupColorsForTags = true;
        var color = Tags.ColorOf(s, "server");
        Assert.Contains(color, s.Look.GroupColors);
        Assert.Equal(color, Tags.ColorOf(s, "SERVER"));                   // je Name gleich bleibend
        Assert.Equal(string.Empty, Tags.OwnColorOf(s, "server"));
        Tags.SetColor(s, "server", "#ABCDEF");
        Assert.Equal("#ABCDEF", Tags.ColorOf(s, "server"));
    }

    [Fact]
    public void DesignFileRoundTripsThroughTheMarketplaceFormat()
    {
        var json = Appearance.ToFile(Rich());
        var back = Marketplace.ParseDesign(json, new MarketplaceFile("Designs/Reich.design.json", "v1", MarketplaceItemType.Design));
        Assert.Equal("Reich", back.Name);
        Assert.StartsWith("market:", back.Key, StringComparison.Ordinal);
        Assert.Equal(Appearance.LookOf(Rich()).Key, Appearance.LookOf(back).Key);

        // Alte Dateien ohne die neuen Angaben bleiben gültig
        var old = Marketplace.ParseDesign("""{ "Name": "Alt", "Theme": "Dark", "PanelColor": "#0B2233", "Opacity": 90 }""",
            new MarketplaceFile("Designs/Alt.design.json", "v1", MarketplaceItemType.Design));
        Assert.Null(old.Palette);
        Assert.Null(old.Pair);
        Assert.Empty(Appearance.LookOf(old).GroupColors);
    }

    [Fact]
    public void LookIsPartOfWhatCountsAsAnAppearanceChange()
    {
        var before = new AppSettings();
        var after = new AppSettings();
        after.Look.FontFamily = "Bahnschrift";
        Assert.True(SettingsChange.Between(before, after).Appearance);
    }
}
