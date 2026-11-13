namespace PowerExec.UI.Services;

/// <summary>
/// Kurze Rückmeldungen („3 Einträge installiert“), die das offene Fenster unten einblendet und nach einigen Sekunden
/// wieder ausblendet – statt einer dauerhaften Zeile. Fehler bleiben als InfoBar stehen.
/// </summary>
public static class Snackbar
{
    public static event Action<string>? Shown;

    /// <summary>Zuletzt gezeigte Meldung (für Tests).</summary>
    public static string? Last { get; private set; }

    public static void Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Last = message;
        Shown?.Invoke(message);
    }
}
