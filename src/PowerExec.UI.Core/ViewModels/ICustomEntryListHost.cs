using PowerExec.Core.Config;
using PowerExec.Services;

namespace PowerExec.ViewModels;

/// <summary>Ergebnis des Eintragsdialogs.</summary>
/// <param name="SavedKey">Schlüssel des gespeicherten Eintrags; null = abgebrochen.</param>
/// <param name="Alternative">Statt eines Eintrags gewählt: „catalog“ (aus Vorlagen) oder „files“ (Dateien); sonst null.</param>
/// <param name="SmartGroupPath">Ordner, für den statt eines Eintrags eine smarte Gruppe angelegt werden soll; sonst null.</param>
public sealed record EntryEditorResult(string? SavedKey, string? Alternative, string? SmartGroupPath);

/// <summary>Was die Liste „Eigene Einträge“ in den Einstellungen (<see cref="CustomEntryListViewModel"/>) von der App braucht.</summary>
public interface ICustomEntryListHost
{
    /// <summary>Gespeicherte Einstellungen (nicht die Arbeitskopie des Einstellungsfensters).</summary>
    AppSettings Settings { get; }
    IDialogs Dialogs { get; }
    IIconProvider IconProvider { get; }
    HealthService Health { get; }

    /// <summary>Fenster, über dem Rückfragen erscheinen (das Einstellungsfenster), oder null.</summary>
    object? SettingsDialogOwner { get; }

    /// <summary>Eintragsdialog modal öffnen; arbeitet auf <paramref name="target"/> (Arbeitskopie), speichert nicht.</summary>
    EntryEditorResult EditEntryModal(AppSettings target, CustomEntry? entry, bool isNew, string? group, object? owner);

    /// <summary>Einträge in eine Datei exportieren (fragt nach Ziel und Umfang).</summary>
    void ExportEntries(IReadOnlyList<CustomEntry> entries, string? rootGroup, object? owner, AppSettings? source);

    /// <summary>Smarte Gruppe für einen Ordner anlegen.</summary>
    void CreateSmartGroupFor(string folderPath, string? parent);
}
