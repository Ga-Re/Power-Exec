namespace PowerExec.Core.Config;

/// <summary>Hintergrund hinter dem Panel.</summary>
public enum PanelBackdrop
{
    /// <summary>Kein Weichzeichner, nur die Deckkraft.</summary>
    Clear,
    /// <summary>Klassischer Weichzeichner ohne Rauschen.</summary>
    Aero,
    /// <summary>System-Acrylic von Windows 11 (wie Startmenü und Kontextmenüs).</summary>
    Acrylic,
    /// <summary>Farbstimmung des Hintergrundbilds, kein Durchblick (Windows 11).</summary>
    Mica
}

/// <summary>Welche Variante tatsächlich läuft – abhängig von Windows-Version und Kontrastdesign.</summary>
public static class PanelBackdrops
{
    /// <summary>Ab Windows 11 22H2 (Build 22621) setzt Windows Acrylic und Mica als Fensterhintergrund selbst.</summary>
    public const int SystemBackdropBuild = 22621;

    /// <summary>Ab Windows 11 (Build 22000) rundet Windows Fensterecken selbst.</summary>
    public const int RoundedCornersBuild = 22000;

    public static PanelBackdrop Effective(PanelBackdrop wanted, int osBuild, bool highContrast)
    {
        if (highContrast) return PanelBackdrop.Clear;
        if (wanted is PanelBackdrop.Acrylic or PanelBackdrop.Mica && osBuild < SystemBackdropBuild) return PanelBackdrop.Aero;
        return wanted;
    }

    /// <summary>Weichzeichner-Varianten brauchen ein Fenster ohne Transparenz-Schicht (Windows zeichnet dahinter).</summary>
    public static bool NeedsSystemWindow(PanelBackdrop effective) => effective != PanelBackdrop.Clear;

    /// <summary>Milchglas ist nur sinnvoll, wenn weichgezeichnet wird.</summary>
    public static bool SupportsFrost(PanelBackdrop effective) => effective is PanelBackdrop.Aero or PanelBackdrop.Acrylic;

    public static string Label(PanelBackdrop b) => b switch
    {
        PanelBackdrop.Aero => "Aero-Blur",
        PanelBackdrop.Acrylic => "Acrylic",
        PanelBackdrop.Mica => "Mica",
        _ => "Klarglas"
    };
}
