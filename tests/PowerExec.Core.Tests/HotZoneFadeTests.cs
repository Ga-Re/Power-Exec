using PowerExec.Core.Layout;

namespace PowerExec.Core.Tests;

/// <summary>Hover-Zone „erst beim Annähern zeigen“: stufenlos nach Abstand statt auf einen Schlag.</summary>
public class HotZoneFadeTests
{
    [Fact]
    public void VisibilityGrowsSmoothlyAsTheCursorComesCloser()
    {
        Assert.Equal(1, HotZoneFade.Visibility(0));                        // in bzw. an der Zone: voll da
        Assert.Equal(0, HotZoneFade.Visibility(HotZoneFade.Range));        // am Rand des Bereichs: unsichtbar
        Assert.Equal(0, HotZoneFade.Visibility(HotZoneFade.Range * 3));
        Assert.Equal(0.5, HotZoneFade.Visibility(HotZoneFade.Range / 2), 6);

        // Je näher, desto sichtbarer – in vielen kleinen Schritten, ohne Sprung
        double previous = -1;
        for (var d = HotZoneFade.Range; d >= 0; d -= HotZoneFade.Range / 40)
        {
            var v = HotZoneFade.Visibility(d);
            Assert.True(v >= previous);
            Assert.True(previous < 0 || v - previous < 0.06, $"Sprung bei {d}: {previous} → {v}");
            previous = v;
        }
        // Weicher Anfang: weit weg kaum merklich
        Assert.True(HotZoneFade.Visibility(HotZoneFade.Range * 0.9) < 0.05);
        Assert.Equal(0, HotZoneFade.Visibility(double.NaN));
    }

    [Theory]
    [InlineData(5, 5, 0)]        // in der Zone
    [InlineData(-30, 5, 30)]     // links daneben
    [InlineData(5, 50, 30)]      // darunter
    [InlineData(-3, -4, 5)]      // schräg vor der Ecke
    public void DistanceToZone(double x, double y, double expected) =>
        Assert.Equal(expected, HotZoneFade.Distance(x, y, 0, 0, 10, 20), 6);
}
