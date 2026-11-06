namespace PowerExec.Services;

/// <summary>Betriebssystem-Aufrufe der Oberfläche: Explorer, Browser, Zwischenablage, installierte Werkzeuge.</summary>
public interface IShell
{
    /// <summary>Im Explorer zeigen: Datei markieren bzw. Ordner öffnen (<paramref name="isFolder"/>).</summary>
    void OpenLocation(string path, bool isFolder = false);

    /// <summary>Adresse im Standardbrowser öffnen; Fehler werden still ignoriert.</summary>
    void OpenUrl(string url);

    /// <summary>Text in die Zwischenablage legen; Fehler (Zwischenablage belegt) werden still ignoriert.</summary>
    void SetClipboardText(string text);

    /// <summary>Ist PowerShell 7 (pwsh.exe) installiert?</summary>
    bool IsPwshInstalled { get; }
}

/// <summary>Einfache Rückfragen der Oberfläche.</summary>
public interface IDialogs
{
    /// <summary>
    /// Fragt einen Text ab; null = abgebrochen. <paramref name="validate"/> liefert eine Fehlermeldung
    /// (Eingabe wird nicht übernommen) oder null.
    /// </summary>
    string? Ask(string heading, string prompt, string initial = "", string okText = "OK", Func<string, string?>? validate = null);
}

/// <summary>Aktueller Zustand des Designs (Windows-Kontrastdesign, helles/dunkles Design der App).</summary>
public interface IThemeInfo
{
    bool IsHighContrast { get; }
    bool IsDark { get; }
}
