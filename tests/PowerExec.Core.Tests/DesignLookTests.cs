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
    }

    [Fact]
    public void VariantChoiceAppliesOnlyToDesignsWithBothVariants()
    {
        var s = new AppSettings { Variant = ThemeMode.Light, Panels = { new PanelConfig() } };
        Appearance.Apply(s, Rich());
        Assert.Equal(ThemeMode.Light, s.Theme);                  // Paar: die gewählte Variante
        Assert.Equal("custom:rich", s.DesignKey);

        var onlyDark = new DesignPreset("custom:dark", "Dunkel", "", ThemeMode.Dark, "#101010", 100, 0, false);
        Appearance.Apply(s, onlyDark);
        Assert.Equal(ThemeMode.Dark, s.Theme);                   // nur eine Variante: die des Designs
        Assert.False(onlyDark.HasBothVariants);
        Assert.True(Appearance.Presets[0].HasBothVariants);     // „wie Windows“ ohne eigene Fläche
    }

    [Fact]
    public void GroupPaletteFollowsTheRunningVariant()
    {
        var design = Rich() with { Pair = Rich().Pair! with { GroupColors = "#0A0A0A, #0B0B0B" } };
        var s = new AppSettings { CategoryOrder = { "Admin", "Netzwerk" }, Panels = { new PanelConfig() } };
        Appearance.Apply(s, design);
        Assert.Equal(new[] { "#0A0A0A", "#0B0B0B" }, s.Look.PairGroupColors);
        try
        {
            Appearance.IsDarkNow = true;
            Assert.Equal("#FF8A70", GroupManager.ColorOf(s, "Admin"));
            Appearance.IsDarkNow = false;                       // das Paar ist hell: dessen Reihe
            Assert.Equal("#0A0A0A", GroupManager.ColorOf(s, "Admin"));
            Assert.Equal("#0B0B0B", GroupManager.ColorOf(s, "Netzwerk"));
        }
        finally { Appearance.IsDarkNow = true; }
    }

    [Fact]
    public void GroupColorReferencesPickPositionNoneOrFixedColor()
    {
        var s = new AppSettings { CategoryOrder = { "Admin", "Admin/AD", "Netzwerk" } };
        s.Look.GroupColors = new List<string> { "#111111", "#222222", "#333333" };

        GroupManager.SetColor(s, "Admin", Appearance.DesignColor(2));
        Assert.Equal("design:2", GroupManager.OwnColorOf(s, "Admin"));
        Assert.Equal("#333333", GroupManager.ColorOf(s, "Admin"));
        Assert.Equal("#333333", GroupManager.ColorOf(s, "Admin/AD"));    // Untergruppe wie ihre Gruppe

        s.Look.GroupColors = new List<string> { "#AAAAAA", "#BBBBBB", "#CCCCCC" }; // anderes Design: gleiche Stelle
        Assert.Equal("#CCCCCC", GroupManager.ColorOf(s, "Admin"));

        GroupManager.SetColor(s, "Netzwerk", Appearance.NoColor);
        Assert.Equal(string.Empty, GroupManager.ColorOf(s, "Netzwerk")); // ausdrücklich keine Farbe

        GroupManager.SetColor(s, "Netzwerk", "kaputt");
        Assert.Equal(string.Empty, GroupManager.OwnColorOf(s, "Netzwerk")); // ungültig = automatisch
        Assert.Equal("#BBBBBB", GroupManager.ColorOf(s, "Netzwerk"));

        Tags.SetColor(s, "server", Appearance.DesignColor(0));
        Assert.Equal("#AAAAAA", Tags.ColorOf(s, "server"));               // Schlagworte: dieselben Angaben
        Tags.SetColor(s, "server", Appearance.NoColor);
        s.Look.GroupColorsForTags = true;
        Assert.Equal(string.Empty, Tags.ColorOf(s, "server"));
    }

    [Fact]
    public void TranslationsShowInTheLanguageOfTheUserInterface()
    {
        var d = new DesignPreset("market:x", "Forest", "Dark moss green", ThemeMode.Dark, "#1B2A1F", 95, 0, false, Category: "Nature",
            Translations: new Dictionary<string, DesignText> { ["de"] = new("Wald", "Dunkles Moosgrün", "Natur") });
        var before = PowerExec.Core.Localization.L.Language;
        try
        {
            PowerExec.Core.Localization.L.Use("de");
            Assert.Equal("Wald", d.DisplayName);
            Assert.Equal("Natur", d.DisplayCategory);
            PowerExec.Core.Localization.L.Use("en");
            Assert.Equal("Forest", d.DisplayName);
            Assert.Equal("Dark moss green", d.DisplayDescription);
            Assert.True(d.IsDownloaded);
            Assert.False(d.IsOwn);
        }
        finally { PowerExec.Core.Localization.L.Use(before); }
    }

    [Fact]
    public void MigrationKeepsTheLookAsAnActiveDesign()
    {
        // Eigene Einstellungen, die zu keinem Design passen: werden zu „My settings“
        var s = new AppSettings { Theme = ThemeMode.Dark, Panels = { new PanelConfig { PanelColor = "#123456", PanelOpacity = 77 } } };
        Appearance.MigrateToActiveDesign(s);
        var active = Appearance.Active(s);
        Assert.NotNull(active);
        Assert.True(active!.IsOwn);
        Assert.Equal("#123456", active.PanelColor);
        Assert.Equal(ThemeMode.Dark, s.Variant);

        // Passt der Stand zu einem Design, wird dieses das aktive
        var fluent = new AppSettings { Panels = { new PanelConfig() } };
        Appearance.Apply(fluent, Appearance.Presets[0]);
        fluent.DesignKey = string.Empty;
        Appearance.MigrateToActiveDesign(fluent);
        Assert.Equal("fluent", fluent.DesignKey);
        Assert.Empty(fluent.CustomDesigns);
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

    [Theory]
    [InlineData("#2D1B2E", "#FF8A65", true)]
    [InlineData("#0B2233", "#00B7C3", true)]
    [InlineData("#F2EBDD", "#8A5A2B", false)]
    [InlineData("#E3F2EA", "#1B8A5A", false)]
    public void CounterpartKeepsHueAndAccentAndStaysReadable(string panel, string accent, bool dark)
    {
        var palette = Appearance.PaletteFor(Rgb(panel), Rgb(accent));
        var other = Appearance.Counterpart(new DesignColors(panel, accent, palette), dark);
        Assert.Equal(accent, other.AccentColor);
        Assert.Equal(dark, PanelColors.IsLight(Rgb(other.PanelColor)));            // Helligkeit umgekehrt
        Assert.NotEqual("#FFFFFF", other.PanelColor);                                // nicht einfach weiß
        foreach (var (colors, isDark) in new[] { (new DesignColors(panel, accent, palette), dark), (new DesignColors(other.PanelColor, accent, other.Palette), !dark) })
        {
            var r = Appearance.Resolve(colors, isDark);
            Assert.True(PanelColors.Contrast(r.Text, r.Surface) >= 7);
            Assert.True(PanelColors.Contrast(r.Text, r.Card) >= 7);
            Assert.True(PanelColors.Contrast(r.TextSecondary, r.Surface) >= 4.5);
            Assert.True(PanelColors.Contrast(r.Accent, r.Surface) >= 3);
        }
        // Ohne eigene Fläche folgt auch das Gegenstück dem Design
        Assert.Equal(string.Empty, Appearance.Counterpart(new DesignColors("", accent, null), dark).PanelColor);
    }

    [Theory]
    [InlineData("#1B2A1F", "#8BC34A")]   // Forest
    [InlineData("#0F2A3D", "#4FC3F7")]   // Ocean
    [InlineData("#0D0D12", "#FCEE0A")]   // fast neutral, kräftiger Akzent: der Akzent gibt den Farbton
    public void LightCounterpartCarriesTheDesignColor(string panel, string accent)
    {
        var light = Appearance.Counterpart(new DesignColors(panel, accent, Appearance.PaletteFor(Rgb(panel), Rgb(accent))), fromDark: true);
        var surface = Rgb(light.PanelColor);
        Assert.True(PanelColors.IsLight(surface));
        Assert.True(Chroma(surface) >= 12, $"helle Fläche {light.PanelColor} kaum getönt");          // nicht fast weiß
        Assert.True(Chroma(Rgb(light.Palette!.Selected)) > Chroma(surface), "Auswahl trägt den Akzent"); // Auswahl kräftiger als die Fläche
        Assert.True(Chroma(Rgb(light.Palette.Line)) > Chroma(surface));
    }

    [Theory]
    [InlineData("#1C1C1C")]   // Graphite
    [InlineData("#202020")]   // Fluent-Clean (Akzent von Windows)
    public void LightCounterpartOfNeutralDesignStaysNeutral(string panel)
    {
        var light = Appearance.Counterpart(new DesignColors(panel, string.Empty, null), fromDark: true);
        Assert.True(Chroma(Rgb(light.PanelColor)) <= 2, light.PanelColor);
        Assert.True(Chroma(Rgb(light.Palette!.Selected)) <= 2);
        var r = Appearance.Resolve(new DesignColors(light.PanelColor, string.Empty, light.Palette), dark: false);
        Assert.True(PanelColors.Contrast(r.Text, r.Surface) >= 7);
    }

    /// <summary>Alle Designs im Marktplatz: beide Varianten lesbar, helle Varianten farbiger Designs erkennbar getönt.</summary>
    [Fact]
    public void MarketplaceDesignsAreReadableAndLightVariantsAreTinted()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "marketplace", "Designs");
        foreach (var file in Directory.GetFiles(folder, "*.design.json"))
        {
            var d = Marketplace.ParseDesign(File.ReadAllText(file), new MarketplaceFile("Designs/" + Path.GetFileName(file), "v", MarketplaceItemType.Design));
            var variants = new List<(DesignColors Colors, bool Dark)> { (new DesignColors(d.PanelColor, d.AccentColor, d.Palette), d.PrimaryIsDark) };
            if (d.Pair is { } p) variants.Add((new DesignColors(p.PanelColor, p.AccentColor, p.Palette), !d.PrimaryIsDark));
            foreach (var (colors, dark) in variants)
            {
                var r = Appearance.Resolve(colors, dark);
                var name = $"{Path.GetFileName(file)} ({(dark ? "dunkel" : "hell")})";
                Assert.True(PanelColors.Contrast(r.Text, r.Surface) >= 7, name);
                Assert.True(PanelColors.Contrast(r.Text, r.Card) >= 7, name);
                Assert.True(PanelColors.Contrast(r.TextSecondary, r.Surface) >= 4.5, name);
                if (colors.AccentColor.Length > 0) Assert.True(PanelColors.Contrast(r.Accent, r.Surface) >= 3, name);
                // Helle Variante eines farbigen Designs (eigener Akzent, nicht „Paper“): Fläche getönt, nicht fast weiß
                if (!dark && colors.AccentColor.Length > 0 && !d.Name.Equals("Paper", StringComparison.Ordinal))
                    Assert.True(Chroma(r.Surface) >= 8, $"{name}: Fläche {r.Surface.Hex} kaum getönt");
            }
        }
    }

    /// <summary>Wie bunt eine Farbe ist: Abstand zwischen größtem und kleinstem Kanal (0 = grau).</summary>
    private static int Chroma(Rgb c) => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));

    private static Rgb Rgb(string hex) => PanelColors.TryParse(hex, out var c) ? c : throw new ArgumentException(hex);
}
