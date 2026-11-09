using PowerExec.Core.Config;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

/// <summary>Was der Eintragsdialog (<see cref="CustomEntryViewModel"/>) von der App braucht.</summary>
public interface ICustomEntryHost
{
    IShell Shell { get; }
    IDialogs Dialogs { get; }
    IIconProvider IconProvider { get; }
    IUiDispatcher UiDispatcher { get; }

    /// <summary>Laufwerksbuchstabe eines Netzlaufwerks in UNC-Pfad umwandeln (sonst unverändert).</summary>
    string ToUnc(string path);

    /// <summary>Eigene Variablen ({{$Name}}) – für Einfügen, Vorschläge und die Warnung bei unbekannten Variablen.</summary>
    IReadOnlyList<UserVariable> UserVariables => Array.Empty<UserVariable>();
}

/// <summary>Was der Profil-Dialog (<see cref="ProfilesViewModel"/>) von der App braucht.</summary>
public interface IProfilesHost : ICustomEntryHost
{
    /// <summary>Eigenen Eintrag mit diesem Profil zur Probe starten (ohne Verlauf).</summary>
    void TestRun(CustomEntry entry, LaunchProfile profile);

    /// <summary>Nach dem Speichern: Panels neu aufbauen.</summary>
    void OnProfilesSaved();
}
