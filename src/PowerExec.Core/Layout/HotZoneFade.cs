namespace PowerExec.Core.Layout;

/// <summary>
/// Hover-Zone „erst beim Annähern zeigen“: je näher der Mauszeiger kommt, desto deutlicher erscheint der Streifen –
/// stufenlos statt auf einen Schlag. Weit weg ist er unsichtbar, an bzw. in der Zone voll da.
/// </summary>
public static class HotZoneFade
{
    /// <summary>Ab diesem Abstand (DIP) beginnt der Streifen zu erscheinen.</summary>
    public const double Range = 160;

    /// <summary>
    /// Sichtbarkeit 0 … 1 für einen Abstand zur Zone (0 = in der Zone). Weicher Verlauf (Smoothstep): am Rand des
    /// Bereichs kaum merklich, nahe der Zone zügig voll.
    /// </summary>
    public static double Visibility(double distance, double range = Range)
    {
        if (double.IsNaN(distance) || range <= 0) return 0;
        var t = 1 - Math.Clamp(distance / range, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Abstand eines Punkts zu einem Rechteck (0, wenn er darin liegt); Einheiten wie die Eingaben.</summary>
    public static double Distance(double x, double y, double left, double top, double right, double bottom)
    {
        var dx = x < left ? left - x : x > right ? x - right : 0;
        var dy = y < top ? top - y : y > bottom ? y - bottom : 0;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
