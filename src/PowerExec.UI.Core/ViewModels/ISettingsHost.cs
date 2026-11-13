using PowerExec.Core.Config;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;

/// <summary>
/// Was die Teile des Einstellungsfensters (Schlagworte, Panel-Darstellung, Rechnergruppen …) von der App brauchen.
/// </summary>
public interface ISettingsHost
{
    /// <summary>Gespeicherte Einstellungen (nicht die Arbeitskopie des Einstellungsfensters).</summary>
    AppSettings Settings { get; }
    IDialogs Dialogs { get; }
    IThemeInfo Theme { get; }

    /// <summary>Eingelesene Dateien (für Anzeigenamen).</summary>
    IItemSource Repository { get; }

    /// <summary>Fenster, über dem Rückfragen erscheinen (das Einstellungsfenster), oder null.</summary>
    object? SettingsDialogOwner { get; }

    /// <summary>Rechner einer dynamischen Gruppe aus dem AD lesen; setzt Stand/Fehler an der Gruppe. null = keine AD-Regeln.</summary>
    Task<IReadOnlyList<string>?> SyncMachineGroupAsync(MachineGroup group);

    /// <summary>Rechner im Active Directory suchen (Name mit * als Platzhalter).</summary>
    Task<IReadOnlyList<string>> FindComputersAsync(string query);
    /// <summary>Distinguished Names von Domäne, OUs und dem Container „Computers“ (für die OU-Auswahl).</summary>
    Task<IReadOnlyList<string>> FindOrganizationalUnitsAsync() => AdSearch.FindOrganizationalUnitsAsync();

    /// <summary>PowerShell-Befehl einer Variable ausführen (Ausgabe als Text; wirft bei Fehler).</summary>
    string RunVariableCommand(string command) => VariableCommandRunner.Run(command, Settings.DefaultEngine);

    /// <summary>Marktplatz-Fenster öffnen (bzw. nach vorn holen), optional mit einem Bereich („Entry“, „Script“, „Design“ …).</summary>
    void ShowMarketplace(string? tab = null) { }

    /// <summary>Text-Datei aus dem Netz laden (Marktplatz).</summary>
    Task<string> DownloadTextAsync(Uri uri) => MarketplaceClient.DownloadTextAsync(uri);

    /// <summary>Ordner für Scripte aus dem Marktplatz.</summary>
    string MarketplaceScriptFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Power-Exec", "Marktplatz");

    /// <summary>Power-Exec beenden und neu starten (z. B. nach dem Wechsel der Sprache).</summary>
    void RestartApp() { }
}
