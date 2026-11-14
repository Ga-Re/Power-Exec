using System.Drawing;
using PowerExec.Core.Config;

namespace PowerExec.Core.Layout;

/// <summary>
/// Ein- und Ausfahren der Weichzeichner-Varianten: Das Fenster behält seine Größe und gleitet als Ganzes vom Rand
/// des Arbeitsbereichs herein. Windows verschiebt Inhalt und Hintergrund dabei gemeinsam, sie bleiben deckungsgleich.
/// Alle Werte in physischen Pixeln.
/// </summary>
public static class PanelSlide
{
    /// <summary>Eingefahren: das Panel liegt direkt hinter dem Rand des Arbeitsbereichs (nicht mehr sichtbar).</summary>
    public static Rectangle Hidden(Rectangle panel, Rectangle workArea, ScreenEdge edge) => edge switch
    {
        ScreenEdge.Left => panel with { X = workArea.Left - panel.Width },
        ScreenEdge.Top => panel with { Y = workArea.Top - panel.Height },
        ScreenEdge.Bottom => panel with { Y = workArea.Bottom },
        _ => panel with { X = workArea.Right }
    };

    /// <summary>Fensterlage bei <paramref name="progress"/> (0 = eingefahren, 1 = ganz ausgefahren); die Größe bleibt gleich.</summary>
    public static Rectangle At(Rectangle panel, Rectangle hidden, double progress)
    {
        var f = Math.Clamp(progress, 0, 1);
        return panel with
        {
            X = (int)Math.Round(hidden.X + (panel.X - hidden.X) * f),
            Y = (int)Math.Round(hidden.Y + (panel.Y - hidden.Y) * f)
        };
    }

    /// <summary>
    /// Muss das Fenster auf dem Weg auf den Arbeitsbereich zugeschnitten werden? Nur wenn außerhalb davon etwas
    /// Sichtbares liegt: die Taskleiste an dieser Kante oder ein angrenzender Monitor. Sonst gleitet das Fenster
    /// ohne Zuschnitt (Windows rundet die Ecken selbst, kein Fensterbereich pro Bild).
    /// </summary>
    /// <param name="monitors">Bildschirmbereiche aller Monitore, auch der eigene.</param>
    public static bool NeedsClip(Rectangle panel, Rectangle workArea, ScreenEdge edge, IEnumerable<Rectangle> monitors)
    {
        var path = Rectangle.Union(Hidden(panel, workArea, edge), panel);
        var outside = edge switch
        {
            ScreenEdge.Left => Rectangle.FromLTRB(path.Left, path.Top, workArea.Left, path.Bottom),
            ScreenEdge.Top => Rectangle.FromLTRB(path.Left, path.Top, path.Right, workArea.Top),
            ScreenEdge.Bottom => Rectangle.FromLTRB(path.Left, workArea.Bottom, path.Right, path.Bottom),
            _ => Rectangle.FromLTRB(workArea.Right, path.Top, path.Right, path.Bottom)
        };
        if (outside.Width <= 0 || outside.Height <= 0) return false;
        return monitors.Any(m => m.IntersectsWith(outside));
    }

    /// <summary>So weit reichen Schatten und Rand, die Windows um ein Fenster zeichnet (Pixel, großzügig bemessen).</summary>
    public const int ShadowReach = 32;

    /// <summary>
    /// Läge der Schatten des ausgefahrenen Panels auf einem anderen Monitor? Das ist der Fall, wenn ein Monitor, auf dem
    /// das Panel nicht steht, näher als <see cref="ShadowReach"/> an dessen Rand liegt – etwa wenn das Panel an der
    /// Kante zum Nachbarmonitor andockt. Dann bekommt das Fenster einen Fensterbereich: Windows zeichnet dafür keinen Schatten.
    /// </summary>
    public static bool ShadowReachesOtherMonitor(Rectangle panel, IEnumerable<Rectangle> monitors)
    {
        var center = new Point(panel.X + panel.Width / 2, panel.Y + panel.Height / 2);
        var reach = Rectangle.Inflate(panel, ShadowReach, ShadowReach);
        return monitors.Any(m => !m.Contains(center) && m.IntersectsWith(reach));
    }

    /// <summary>
    /// Steht das ausgefahrene Fenster an seiner Ziellage? Je Kante sind <paramref name="tolerance"/> Pixel Abweichung
    /// erlaubt (Rundung, wenn WPF Lage und Größe über DIP setzt); 0 verlangt die genaue Lage.
    /// </summary>
    public static bool IsSettled(Rectangle actual, Rectangle expected, int tolerance) =>
        Math.Abs(actual.X - expected.X) <= tolerance && Math.Abs(actual.Y - expected.Y) <= tolerance &&
        Math.Abs(actual.Width - expected.Width) <= tolerance && Math.Abs(actual.Height - expected.Height) <= tolerance;

    /// <summary>Sichtbarer Teil des Fensters (relativ zum Fenster), wenn auf den Arbeitsbereich zugeschnitten wird; leer = nichts sichtbar.</summary>
    public static Rectangle Visible(Rectangle window, Rectangle workArea)
    {
        var v = Rectangle.Intersect(window, workArea);
        if (v.Width <= 0 || v.Height <= 0) return Rectangle.Empty;
        v.Offset(-window.X, -window.Y);
        return v;
    }
}
