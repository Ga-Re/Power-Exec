using System.Drawing;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;

namespace PowerExec.Core.Tests;

public class PanelSlideTests
{
    // 1920×1080, Taskleiste unten (48 px), Panel rechts mit 8 px Abstand
    private static readonly Rectangle Bounds = new(0, 0, 1920, 1080);
    private static readonly Rectangle Work = new(0, 0, 1920, 1032);
    private static readonly Rectangle Panel = new(1512, 8, 400, 1016);

    [Theory]
    [InlineData(ScreenEdge.Right, 1920, 8)]
    [InlineData(ScreenEdge.Left, -400, 8)]
    [InlineData(ScreenEdge.Top, 1512, -1016)]
    [InlineData(ScreenEdge.Bottom, 1512, 1032)]
    public void HiddenSitsJustBeyondTheWorkArea(ScreenEdge edge, int x, int y)
    {
        var hidden = PanelSlide.Hidden(Panel, Work, edge);
        Assert.Equal(new Rectangle(x, y, 400, 1016), hidden);
        Assert.Equal(Rectangle.Empty, PanelSlide.Visible(hidden, Work));
    }

    [Fact]
    public void SizeStaysTheSameWhileSliding()
    {
        var hidden = PanelSlide.Hidden(Panel, Work, ScreenEdge.Right);
        Assert.Equal(hidden, PanelSlide.At(Panel, hidden, 0));
        Assert.Equal(Panel, PanelSlide.At(Panel, hidden, 1));
        var mid = PanelSlide.At(Panel, hidden, 0.5);
        Assert.Equal(Panel.Size, mid.Size);
        Assert.Equal(1716, mid.X);
        Assert.Equal(Panel.Y, mid.Y);
        Assert.Equal(Panel, PanelSlide.At(Panel, hidden, 1.7)); // über das Ziel hinaus wird begrenzt
    }

    [Fact]
    public void NoClipOnTheOuterEdgeOfASingleMonitor()
    {
        Assert.False(PanelSlide.NeedsClip(Panel, Work, ScreenEdge.Right, [Bounds]));
    }

    [Fact]
    public void ClipWhenAMonitorAdjoinsTheEdge()
    {
        var right = new Rectangle(1920, 0, 2560, 1440);
        Assert.True(PanelSlide.NeedsClip(Panel, Work, ScreenEdge.Right, [Bounds, right]));
        // Monitor links daneben stört rechts nicht
        var left = new Rectangle(-1920, 0, 1920, 1080);
        Assert.False(PanelSlide.NeedsClip(Panel, Work, ScreenEdge.Right, [Bounds, left]));
    }

    [Fact]
    public void ClipWhenTheTaskbarIsOnThatEdge()
    {
        var bottomPanel = new Rectangle(760, 616, 400, 408);
        Assert.True(PanelSlide.NeedsClip(bottomPanel, Work, ScreenEdge.Bottom, [Bounds]));
        var workRightTaskbar = new Rectangle(0, 0, 1872, 1080);
        var rightPanel = new Rectangle(1464, 8, 400, 1064);
        Assert.True(PanelSlide.NeedsClip(rightPanel, workRightTaskbar, ScreenEdge.Right, [Bounds]));
    }

    [Fact]
    public void StaleTargetIsClippedAgainstANewWorkArea()
    {
        // Taskleiste wandert nach rechts: der Arbeitsbereich wird schmaler. Eine Ziellage vom alten Stand ragt dann
        // hinaus und würde am Ende des Ausfahrens zugeschnitten bleiben – deshalb wird die Geometrie vor jedem
        // Ausfahren neu bestimmt.
        var workRightTaskbar = new Rectangle(0, 0, 1872, 1080);
        var visible = PanelSlide.Visible(Panel, workRightTaskbar);
        Assert.NotEqual(Panel.Size, visible.Size);
        Assert.Equal(new Rectangle(0, 0, 360, 1016), visible);
    }

    [Fact]
    public void SettledAllowsOnlyRoundingDifferences()
    {
        Assert.True(PanelSlide.IsSettled(Panel, Panel, 0));
        Assert.False(PanelSlide.IsSettled(Panel with { X = Panel.X + 60 }, Panel, 2));  // nicht ganz ausgefahren
        Assert.False(PanelSlide.IsSettled(Panel with { Width = 500 }, Panel, 0));       // fremd bemessen
        Assert.True(PanelSlide.IsSettled(Panel with { Width = 401, Height = 1015 }, Panel, 2));
        Assert.True(PanelSlide.IsSettled(Panel with { X = Panel.X - 1 }, Panel, 2));
        Assert.False(PanelSlide.IsSettled(Panel with { Width = 401 }, Panel, 0));
        Assert.False(PanelSlide.IsSettled(Panel with { X = Panel.X - 1 }, Panel, 0));
    }

    [Fact]
    public void VisiblePartIsRelativeToTheWindow()
    {
        Assert.Equal(new Rectangle(0, 0, 400, 1016), PanelSlide.Visible(Panel, Work));
        Assert.Equal(new Rectangle(0, 0, 120, 1016), PanelSlide.Visible(Panel with { X = 1800 }, Work));
        Assert.Equal(Rectangle.Empty, PanelSlide.Visible(Panel with { X = 1920 }, Work));
        var bottom = new Rectangle(760, 900, 400, 408);
        Assert.Equal(new Rectangle(0, 0, 400, 132), PanelSlide.Visible(bottom, Work));
    }

    [Fact]
    public void ShadowGuardOnlyWhenAnotherMonitorIsWithinReach()
    {
        var home = new Rectangle(0, 0, 1920, 1080);
        var right = new Rectangle(1920, 0, 1920, 1080);
        var docked = new Rectangle(1920 - 420, 0, 420, 1040);   // am rechten Rand, direkt an der Kante zum Nachbarn
        var inner = new Rectangle(0, 100, 420, 800);           // am linken Rand, weit weg vom Nachbarn

        Assert.False(PanelSlide.ShadowReachesOtherMonitor(docked, new[] { home }));
        Assert.True(PanelSlide.ShadowReachesOtherMonitor(docked, new[] { home, right }));
        Assert.False(PanelSlide.ShadowReachesOtherMonitor(inner, new[] { home, right }));
        // Monitor darüber: ein Panel über die volle Höhe reicht mit dem Schatten hinein
        var above = new Rectangle(0, -1080, 1920, 1080);
        Assert.True(PanelSlide.ShadowReachesOtherMonitor(new Rectangle(0, 0, 420, 1040), new[] { home, above }));
        // Lücke größer als die Reichweite des Schattens: kein Schutz nötig
        var far = new Rectangle(1920 + PanelSlide.ShadowReach + 1, 0, 1920, 1080);
        Assert.False(PanelSlide.ShadowReachesOtherMonitor(docked, new[] { home, far }));
    }
}
