using System.Drawing;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;

namespace PowerExec.Core.Tests;

public class PanelLayoutTests
{
    // 1920×1080, Taskleiste unten (48 px)
    private static readonly Rectangle Bounds = new(0, 0, 1920, 1080);
    private static readonly Rectangle Work = new(0, 0, 1920, 1032);

    [Fact]
    public void HotZoneUsesThicknessAndLength()
    {
        var s = new PanelConfig { Edge = ScreenEdge.Right, HotZoneThickness = 10, HotZoneLength = 300 };
        var z = PanelLayout.HotZone(Bounds, Work, s, 1.0);
        Assert.Equal(new Rectangle(1910, 366, 10, 300), z);

        var scaled = PanelLayout.HotZone(Bounds, Work, s, 1.5);
        Assert.Equal(15, scaled.Width);
        Assert.Equal(450, scaled.Height);
        Assert.Equal(1905, scaled.Left);

        s.HotZone = HotZoneMode.Full;
        Assert.Equal(1032, PanelLayout.HotZone(Bounds, Work, s, 1.0).Height);
    }

    [Fact]
    public void FullHeightPanel()
    {
        var s = new PanelConfig { Edge = ScreenEdge.Right, PanelWidth = 400 };
        Assert.Equal(new Rectangle(1512, 8, 400, 1016), PanelLayout.Panel(Bounds, Work, s, 1.0));
    }

    [Theory]
    [InlineData(PanelAlignment.Start, 8)]
    [InlineData(PanelAlignment.Center, 262)]   // 8 + (1016 - 508) / 2
    [InlineData(PanelAlignment.End, 516)]      // 8 + 1016 - 508
    [InlineData(PanelAlignment.HotZone, 262)]  // Hover-Zone in der Mitte
    public void PartialHeightIsAligned(PanelAlignment alignment, int expectedTop)
    {
        var s = new PanelConfig { Edge = ScreenEdge.Left, PanelHeightPercent = 50, PanelAlignment = alignment };
        var r = PanelLayout.Panel(Bounds, Work, s, 1.0);
        Assert.Equal(508, r.Height);
        Assert.Equal(expectedTop, r.Top);
        Assert.Equal(8, r.Left);
    }

    [Fact]
    public void PanelFollowsHotZoneButStaysOnScreen()
    {
        var s = new PanelConfig
        {
            Edge = ScreenEdge.Right,
            HotZone = HotZoneMode.FirstHalf,
            PanelHeightPercent = 40,
            PanelAlignment = PanelAlignment.HotZone
        };
        var r = PanelLayout.Panel(Bounds, Work, s, 1.0);
        var zone = PanelLayout.HotZone(Bounds, Work, s, 1.0);
        Assert.Equal(zone.Top + zone.Height / 2, r.Top + r.Height / 2);

        s.HotZone = HotZoneMode.Middle;
        s.HotZoneLength = 40;
        s.PanelHeightPercent = 100;
        r = PanelLayout.Panel(Bounds, Work, s, 1.0);
        Assert.Equal(8, r.Top); // volle Höhe wird nie verschoben
    }

    [Fact]
    public void TopEdgeUsesHeightPercentAndHorizontalAlignment()
    {
        var s = new PanelConfig { Edge = ScreenEdge.Top, PanelHeightPercent = 60, PanelWidth = 500, PanelAlignment = PanelAlignment.End };
        var r = PanelLayout.Panel(Bounds, Work, s, 1.0);
        Assert.Equal(8, r.Top);
        Assert.Equal((int)Math.Round(1016 * 0.6), r.Height);
        Assert.Equal(1920 - 8 - 500, r.Left);
    }
}
