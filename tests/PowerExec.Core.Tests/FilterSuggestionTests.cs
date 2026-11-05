using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Filter über das Suchfeld: erst die Filterart, dann ihre Werte.</summary>
public class FilterSuggestionTests
{
    private static SuggestionSource Source()
    {
        var src = new SuggestionSource { Favorites = 3 };
        src.Tags["Exchange"] = 4; src.Tags["AD"] = 7;
        src.Kinds[SearchKind.PowerShell] = 23; src.Kinds[SearchKind.Batch] = 2; src.Kinds[SearchKind.Rdp] = 1;
        src.Groups["Kunde A"] = 5;
        return src;
    }

    [Fact]
    public void CategoriesOnlyForWhatExists()
    {
        var cats = SearchSuggestions.Categories(Source());
        Assert.Equal(new[] { "#", "typ:", "gruppe:", "ist:" }, cats.Select(c => c.Insert));
        Assert.All(cats, c => Assert.True(c.IsCategory));
        Assert.Equal("Schlagwort", cats[0].Label);
        Assert.StartsWith("#AD, #Exchange", cats[0].Detail);

        var empty = SearchSuggestions.Categories(new SuggestionSource());
        Assert.Empty(empty);
    }

    [Fact]
    public void ChoosingCategoryLeadsToValues()
    {
        var hash = SearchSuggestions.Categories(Source())[0];
        var (text, caret) = SearchSuggestions.Apply("disk ", 5, hash);
        Assert.Equal("disk #", text); // kein Leerzeichen: es geht mit dem Wert weiter
        var values = SearchSuggestions.For(text, caret, Source());
        Assert.Equal(new[] { "#AD", "#Exchange" }, values.Select(v => v.Insert));
    }

    [Fact]
    public void TypeValuesOfferCombinedKindsLikeFormerButtons()
    {
        var kinds = SearchSuggestions.For("typ:", 4, Source()).Select(k => k.Insert).ToList();
        Assert.Equal("typ:script", kinds[0]); // PowerShell und Batch vorhanden → zusammengefasst
        Assert.DoesNotContain("typ:programm", kinds); // keine Programme vorhanden
        Assert.Contains("typ:ps1", kinds);
        Assert.True(SearchQuery.Parse("typ:script").Kinds.SetEquals(new[] { SearchKind.PowerShell, SearchKind.Batch }));
    }

    [Fact]
    public void BrowsingShowsMoreThanTyping()
    {
        var src = new SuggestionSource();
        for (int i = 0; i < 30; i++) src.Tags["tag" + i] = 1;
        Assert.Equal(30, SearchSuggestions.For("#", 1, src).Count);
        Assert.Equal(SearchSuggestions.MaxSuggestions, SearchSuggestions.For("#tag", 4, src).Count);
    }
}

/// <summary>Gruppenfarben auf hellem bzw. dunklem Panel lesbar halten.</summary>
public class ReadableColorTests
{
    [Theory]
    [InlineData("#EAB308")] // Gelb
    [InlineData("#14B8A6")] // Türkis
    [InlineData("#84CC16")] // Limette
    public void LightColorsAreDarkenedOnLightSurface(string hex)
    {
        Assert.True(Config.PanelColors.TryParse(hex, out var c));
        Assert.True(Config.PanelColors.Contrast(c, Config.PanelColors.LightSurface) < 3.0);
        var r = Config.PanelColors.Readable(c, darkSurface: false);
        Assert.True(Config.PanelColors.Contrast(r, Config.PanelColors.LightSurface) >= 3.0);
        Assert.NotEqual(c, r);
    }

    [Fact]
    public void StrongColorsStayUnchanged()
    {
        Assert.True(Config.PanelColors.TryParse("#6366F1", out var indigo));
        Assert.Equal(indigo, Config.PanelColors.Readable(indigo, darkSurface: false));
        Assert.True(Config.PanelColors.TryParse("#EAB308", out var yellow));
        Assert.Equal(yellow, Config.PanelColors.Readable(yellow, darkSurface: true));
    }

    [Fact]
    public void AllPresetsReadableInBothDesigns()
    {
        foreach (var p in Config.PanelColors.AccentPresets)
        {
            Assert.True(Config.PanelColors.TryParse(p.Hex, out var c));
            foreach (var dark in new[] { false, true })
            {
                var surface = dark ? Config.PanelColors.DarkSurface : Config.PanelColors.LightSurface;
                Assert.True(Config.PanelColors.Contrast(Config.PanelColors.Readable(c, dark), surface) >= 3.0, $"{p.Name} dark={dark}");
            }
        }
    }
}
