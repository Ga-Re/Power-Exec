using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Farbumrechnung und Liste der Dateiendungen.</summary>
public class ColorMathTests
{
    [Fact]
    public void ColorMathRoundTripsAndExtensionsNeedSemicolons()
    {
        foreach (var hex in new[] { "#3B82F6", "#EF4444", "#000000", "#FFFFFF", "#22C55E" })
        {
            Assert.True(PanelColors.TryParse(hex, out var c));
            var (h, sat, v) = ColorMath.ToHsv(c);
            Assert.Equal(hex, ColorMath.FromHsv(h, sat, v).Hex);
        }
        Assert.Equal(new[] { ".ps1", ".cmd", ".exe" }, ExtensionList.Parse(".ps1; cmd;.EXE"));
        Assert.Null(ExtensionList.Error(".ps1; .cmd"));
        Assert.NotNull(ExtensionList.Error(".ps1 .cmd")); // mit Leerzeichen statt Semikolon
        Assert.Equal(".ps1; .cmd", ExtensionList.Format(new[] { ".ps1", ".cmd" }));
    }
}
