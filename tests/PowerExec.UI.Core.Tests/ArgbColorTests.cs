using PowerExec.Core.Config;
using PowerExec.Infrastructure;

namespace PowerExec.UI.Core.Tests;

public class ArgbColorTests
{
    [Fact]
    public void FromHexKeepsChannelsAndAppliesAlpha()
    {
        Assert.Equal(new ArgbColor(0x24, 0x12, 0xAB, 0xEF), ArgbColor.FromHex("#12ABEF", 0x24));
        Assert.Equal(new ArgbColor(255, 0x12, 0xAB, 0xEF), ArgbColor.FromHex("#12abef"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rot")]
    public void InvalidHexGivesNull(string? hex) => Assert.Null(ArgbColor.FromHex(hex));

    [Fact]
    public void FromRgbAndToString()
    {
        var c = ArgbColor.From(new Rgb(1, 2, 3), 0x73);
        Assert.Equal("#73010203", c.ToString());
        Assert.Equal(0, ArgbColor.Transparent.A);
    }
}
