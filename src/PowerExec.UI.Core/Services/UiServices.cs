using PowerExec.Core.Config;

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

/// <summary>
/// Rückfragen, Hinweise und Auswahldialoge der Oberfläche. <c>owner</c> ist jeweils das Fenster der Oberfläche
/// (WPF: <c>Window</c>), über dem der Dialog erscheint, oder null.
/// </summary>
public interface IDialogs
{
    /// <summary>
    /// Fragt einen Text ab; null = abgebrochen. <paramref name="validate"/> liefert eine Fehlermeldung
    /// (Eingabe wird nicht übernommen) oder null.
    /// </summary>
    string? Ask(string heading, string prompt, string initial = "", string okText = "OK", Func<string, string?>? validate = null);

    /// <summary>Hinweis mit „OK“.</summary>
    void Inform(string message, string title, object? owner = null);

    /// <summary>Warnung mit „OK“.</summary>
    void Warn(string message, string title, object? owner = null);

    /// <summary>
    /// Ja/Nein-Frage; true = Ja. <paramref name="destructive"/>: Warnsymbol und „Nein“ als Vorgabe
    /// (Löschen, Entfernen), sonst Fragezeichen und „Ja“ als Vorgabe.
    /// </summary>
    bool Confirm(string message, string title, bool destructive = false, object? owner = null);

    /// <summary>Ja/Nein/Abbrechen; true = Ja, false = Nein, null = Abbrechen (Vorgabe).</summary>
    bool? ConfirmOrCancel(string message, string title, object? owner = null);

    /// <summary>Datei öffnen; <paramref name="filter"/> wie „Text (*.txt)|*.txt“. null = abgebrochen.</summary>
    string? OpenFile(string filter, string? title = null, object? owner = null);

    /// <summary>Mehrere Dateien öffnen; leer = abgebrochen.</summary>
    IReadOnlyList<string> OpenFiles(string filter, string? title = null, object? owner = null);

    /// <summary>Speichern unter; null = abgebrochen.</summary>
    string? SaveFile(string filter, string fileName, string? title = null, object? owner = null);

    /// <summary>Ordner wählen; null = abgebrochen.</summary>
    string? PickFolder(string? title = null, object? owner = null);

    /// <summary>Farbe wählen (#RRGGBB); null = abgebrochen.</summary>
    string? PickColor(string title, string? initial, object? owner = null);

    /// <summary>Symbol wählen; null = abgebrochen.</summary>
    IconRef? PickIcon(IconRef current, string heading);

    /// <summary>Neue Gruppe (Name, optional Ordner und Symbol); null = abgebrochen.</summary>
    NewGroupSpec? AskNewGroup(string heading, string initialName, Func<string, string?> validateName, Func<string?, string?>? validateFolder);

    /// <summary>Vorlagen aus dem Werkzeug-Katalog wählen; null = abgebrochen.</summary>
    IReadOnlyList<CatalogTemplate>? PickCatalogTemplates();
}

/// <summary>Aktueller Zustand des Designs (Windows-Kontrastdesign, helles/dunkles Design der App).</summary>
public interface IThemeInfo
{
    bool IsHighContrast { get; }
    bool IsDark { get; }
}
