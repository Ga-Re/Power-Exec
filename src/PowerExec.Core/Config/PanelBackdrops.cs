namespace PowerExec.Core.Config;

/// <summary>Hintergrund hinter dem Panel.</summary>
public enum PanelBackdrop
{
    /// <summary>Klarglas: Deckkraft der Panelfarbe, dazu Milchglas (Helligkeitsschicht, Tönung, Körnung).</summary>
    Clear,
    /// <summary>Bis 0.20: Aero-Blur hinter einem Systemfenster – wird beim Einlesen zu <see cref="Clear"/>.</summary>
    Aero,
    /// <summary>Bis 0.20: System-Acrylic von Windows 11 – wird beim Einlesen zu <see cref="Clear"/>.</summary>
    Acrylic,
    /// <summary>Bis 0.20: Mica von Windows 11 – wird beim Einlesen zu <see cref="Clear"/>.</summary>
    Mica
}

/// <summary>Welche Variante tatsächlich läuft und wie das Milchglas aussieht.</summary>
public static class PanelBackdrops
{
    /// <summary>Ab Windows 11 (Build 22000) rundet Windows Fensterecken selbst.</summary>
    public const int RoundedCornersBuild = 22000;

    /// <summary>Deckkraft der Rauschtextur („Körnung“) über dem Milchglas.</summary>
    public const double NoiseOpacity = 0.03;

    /// <summary>
    /// Aero-Blur, Acrylic und Mica gibt es seit 0.21 nicht mehr: Windows zeichnete sie hinter einem eigenen
    /// Systemfenster, das beim Ein- und Ausfahren sichtbar ruckelte.
    /// </summary>
    public static bool IsRetired(PanelBackdrop b) => b is PanelBackdrop.Aero or PanelBackdrop.Acrylic or PanelBackdrop.Mica;

    public static PanelBackdrop Effective(PanelBackdrop wanted, int osBuild, bool highContrast) =>
        highContrast || IsRetired(wanted) ? PanelBackdrop.Clear : wanted;

    /// <summary>Varianten mit Weichzeichner von Windows brauchen ein Fenster ohne Transparenz-Schicht (derzeit keine).</summary>
    public static bool NeedsSystemWindow(PanelBackdrop effective) => effective != PanelBackdrop.Clear;

    /// <summary>Milchglas gibt es auf Klarglas.</summary>
    public static bool SupportsFrost(PanelBackdrop effective) => effective == PanelBackdrop.Clear;

    /// <summary>
    /// Deckkraft der Helligkeitsschicht unter der Panelfarbe: dunkle Fläche Schwarz 40–60 %, helle Fläche Weiß 50–70 %;
    /// der Milchglas-Regler (0–60) wählt den Wert in diesem Bereich, 0 = aus.
    /// </summary>
    public static double FrostBrightness(int frost, bool darkSurface)
    {
        if (frost <= 0) return 0;
        var t = Math.Clamp(frost, 0, 60) / 60.0;
        return (darkSurface ? 0.40 : 0.50) + 0.20 * t;
    }

    public static string Label(PanelBackdrop b) => "Klarglas";
}
