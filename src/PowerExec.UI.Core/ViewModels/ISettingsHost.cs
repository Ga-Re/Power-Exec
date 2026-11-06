using PowerExec.Core.Config;
using PowerExec.Services;

namespace PowerExec.ViewModels;

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
}
