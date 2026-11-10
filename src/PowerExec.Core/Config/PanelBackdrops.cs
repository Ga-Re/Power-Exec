using PowerExec.Core.Localization;
namespace PowerExec.Core.Config;

/// <summary>Hintergrund hinter dem Panel.</summary>
public enum PanelBackdrop
{
    /// <summary>Klarglas: Deckkraft der Panelfarbe, dazu Milchglas (Helligkeitsschicht und Tönung).</summary>
    Clear,
    /// <summary>Bis 0.20: Aero-Blur hinter einem Systemfenster – wird beim Einlesen zu <see cref="Clear"/>.</summary>
    Aero,
    /// <summary>Bis 0.20: System-Acrylic von Windows 11 – wird beim Einlesen zu <see cref="Clear"/>.</summary>
    Acrylic,
    /// <summary>Bis 0.20: Mica von Windows 11 – wird beim Einlesen zu <see cref="Clear"/>.</summary>
    Mica,
    /// <summary>
    /// Glas (eigener Weichzeichner, Windows 11): Hintergrund hinter dem Panel über die Windows-Composition im Panelfenster
    /// selbst weichgezeichnet (Weichzeichner, Sättigung, Helligkeit), darüber die Panelfarbe.
    /// </summary>
    Glass,
    /// <summary>Vollton: deckende Panelfarbe – keine Transparenz, kein Milchglas, kein Weichzeichner.</summary>
    Solid
}

/// <summary>Welche Variante tatsächlich läuft und wie das Milchglas aussieht.</summary>
public static class PanelBackdrops
{
    /// <summary>Ab Windows 11 (Build 22000) rundet Windows Fensterecken selbst.</summary>
    public const int RoundedCornersBuild = 22000;

    /// <summary>Eigener Weichzeichner (Composition mit Hintergrund des Hosts) ab Windows 11.</summary>
    public const int GlassBuild = 22000;

    /// <summary>
    /// Aero-Blur, Acrylic und Mica gibt es seit 0.21 nicht mehr: Windows zeichnete sie hinter einem eigenen
    /// Systemfenster, das beim Ein- und Ausfahren sichtbar ruckelte.
    /// </summary>
    public static bool IsRetired(PanelBackdrop b) => b is PanelBackdrop.Aero or PanelBackdrop.Acrylic or PanelBackdrop.Mica;

    /// <summary>
    /// Tatsächlich laufende Variante. Glas fällt auf Klarglas (mit Milchglas) zurück: unter Windows 10, im Kontrastdesign
    /// und wenn die Transparenzeffekte von Windows ausgeschaltet sind.
    /// </summary>
    public static PanelBackdrop Effective(PanelBackdrop wanted, int osBuild, bool highContrast, bool transparencyEffects = true)
    {
        if (highContrast || IsRetired(wanted)) return PanelBackdrop.Clear;
        if (wanted == PanelBackdrop.Glass && (osBuild < GlassBuild || !transparencyEffects)) return PanelBackdrop.Clear;
        return wanted;
    }

    /// <summary>Glas braucht ein Fenster ohne Transparenz-Schicht (Windows zeichnet die Composition dahinter).</summary>
    public static bool NeedsSystemWindow(PanelBackdrop effective) => effective == PanelBackdrop.Glass;

    /// <summary>Milchglas (Helligkeitsschicht) gibt es auf Klarglas und auf Glas.</summary>
    public static bool SupportsFrost(PanelBackdrop effective) => effective is PanelBackdrop.Clear or PanelBackdrop.Glass;

    /// <summary>Weichzeichner-Radius (Standardabweichung in Pixeln) zum Regler 0–100.</summary>
    public static float GlassBlurRadius(int blur) => Math.Clamp(blur, 0, 100) * 0.5f;

    /// <summary>Sättigung als Faktor (1 = unverändert) zum Regler 0–200 %.</summary>
    public static float GlassSaturation(int percent) => Math.Clamp(percent, 0, 200) / 100f;


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

    public static string Label(PanelBackdrop b) => b switch
    {
        PanelBackdrop.Glass => L.T("Glas (Blur)"),
        PanelBackdrop.Solid => L.T("Vollton (deckend)"),
        _ => L.T("Transparent")
    };

    /// <summary>Deckkraft, mit der das Panel tatsächlich gezeichnet wird: Vollton ist immer deckend.</summary>
    public static int EffectiveOpacity(PanelBackdrop setting, int opacity) => setting == PanelBackdrop.Solid ? 100 : opacity;
}
