using PowerExec.Core.Config;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

public class PanelColorTests
{
    [Theory]
    [InlineData("#1B2A3D", 0x1B, 0x2A, 0x3D)]
    [InlineData("1b2a3d", 0x1B, 0x2A, 0x3D)]
    [InlineData("#fff", 0xFF, 0xFF, 0xFF)]
    public void ParsesHex(string text, byte r, byte g, byte b)
    {
        Assert.True(PanelColors.TryParse(text, out var c));
        Assert.Equal(new Rgb(r, g, b), c);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("blau")]
    public void RejectsInvalid(string text) => Assert.False(PanelColors.TryParse(text, out _));

    [Fact]
    public void DetectsLightAndDark()
    {
        foreach (var p in PanelColors.Presets.Where(p => p.Hex.Length > 0))
        {
            PanelColors.TryParse(p.Hex, out var c);
            var expectLight = p.Name is "Hellgrau" or "Sand" or "Eisblau";
            Assert.Equal(expectLight, PanelColors.IsLight(c));
            // Die passende Schrift (schwarz oder weiß) erreicht immer mindestens 4.5:1
            var text = expectLight ? new Rgb(0x1A, 0x1A, 0x1A) : new Rgb(255, 255, 255);
            Assert.True(PanelColors.Contrast(c, text) >= 4.5, p.Name);
        }
    }

    [Fact]
    public void DerivedColorsMoveAwayFromBase()
    {
        PanelColors.TryParse("#1E1E1E", out var dark);
        var d = PanelColors.Derive(dark);
        Assert.True(d.Hover.R > dark.R && d.Selected.R > d.Hover.R);
        PanelColors.TryParse("#F3F3F3", out var light);
        var l = PanelColors.Derive(light);
        Assert.True(l.Hover.R < light.R);
    }

    [Fact]
    public void NewSettingsHaveDefaultsAndAreNormalized()
    {
        var file = Path.Combine(Path.GetTempPath(), "pe-colors-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, """{"PanelColor":"keine Farbe","PanelOpacity":10,"DefaultFilter":"Favorites"}""");
            var s = new SettingsStore(file).Load();
            Assert.Equal(string.Empty, s.PanelColor);
            Assert.Equal(50, s.PanelOpacity);
            Assert.Equal(TypeFilter.Favorites, s.DefaultFilter);
            Assert.True(new AppSettings().ShowProgramIcons);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
