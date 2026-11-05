using System.Drawing;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;

namespace PowerExec.Core.Tests;

/// <summary>Ein-/Ausfahren mit Weichzeichner: Fenster schieben (flüssig) oder wachsen lassen.</summary>
public class RevealTests
{
    private static readonly Rectangle Full = new(1512, 8, 400, 1000); // rechts an einem 1920er-Bildschirm

    [Fact]
    public void MoveKeepsSizeAndSlidesFromEdge()
    {
        // eingefahren: nur ein Pixel ragt herein
        Assert.Equal(new Rectangle(1911, 8, 400, 1000), PanelLayout.Reveal(Full, ScreenEdge.Right, 0, move: true));
        Assert.Equal(Full, PanelLayout.Reveal(Full, ScreenEdge.Right, 1, move: true));
        var half = PanelLayout.Reveal(Full, ScreenEdge.Right, 0.5, move: true);
        Assert.Equal(new Rectangle(1712, 8, 400, 1000), half);
        var left = PanelLayout.Reveal(new Rectangle(8, 8, 400, 1000), ScreenEdge.Left, 0.25, move: true);
        Assert.Equal(new Rectangle(-292, 8, 400, 1000), left);
    }

    [Fact]
    public void GrowAnchorsAtEdge()
    {
        Assert.Equal(new Rectangle(1712, 8, 200, 1000), PanelLayout.Reveal(Full, ScreenEdge.Right, 0.5, move: false));
        Assert.Equal(new Rectangle(1512, 8, 400, 500), PanelLayout.Reveal(Full, ScreenEdge.Top, 0.5, move: false));
        Assert.Equal(1, PanelLayout.Reveal(Full, ScreenEdge.Right, 0, move: false).Width);
    }

    [Fact]
    public void NeighborMonitorBlocksMoving()
    {
        var right = new Rectangle(1920, 0, 1920, 1080);
        var left = new Rectangle(-1920, 0, 1920, 1080);
        Assert.True(PanelLayout.HasMonitorBeyond(Full, ScreenEdge.Right, new[] { right }));
        Assert.False(PanelLayout.HasMonitorBeyond(Full, ScreenEdge.Right, new[] { left }));
        Assert.False(PanelLayout.HasMonitorBeyond(Full, ScreenEdge.Right, Array.Empty<Rectangle>()));
    }
}
