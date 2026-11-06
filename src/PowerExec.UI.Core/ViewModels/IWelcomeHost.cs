using PowerExec.Core.Config;

namespace PowerExec.ViewModels;

/// <summary>Was die Einführung (<see cref="WelcomeViewModel"/>) von der App braucht.</summary>
public interface IWelcomeHost
{
    AppSettings Settings { get; }

    /// <summary>Geänderte Einstellungen übernehmen (Panels und Ordner neu aufbauen); Fehlermeldung oder null.</summary>
    string? ApplySettings(AppSettings updated);

    /// <summary>Ordnerauswahl der Oberfläche; null = abgebrochen.</summary>
    string? PickFolder(string title);
}
