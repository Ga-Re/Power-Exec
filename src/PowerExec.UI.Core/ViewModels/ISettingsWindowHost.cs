using PowerExec.Core.Config;
using PowerExec.Core.Updates;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;

/// <summary>Was das Einstellungsfenster (<see cref="SettingsViewModel"/>) als Ganzes von der App braucht.</summary>
public interface ISettingsWindowHost : ICustomEntryListHost, IProfilesHost
{
    // In mehreren Basis-Schnittstellen deklariert – hier eindeutig gemacht.
    new IDialogs Dialogs { get; }
    new IIconProvider IconProvider { get; }

    UpdateState Update { get; }
    void SaveSettings();

    /// <summary>Geänderte Einstellungen übernehmen (Panels und Ordner neu aufbauen); Fehlermeldung oder null.</summary>
    string? ApplySettings(AppSettings updated);

    /// <summary>Pfad der Einstellungsdatei.</summary>
    string SettingsFilePath { get; }

    // ── Team-Kataloge ────────────────────────────────────────────────
    Task CheckCatalogsAsync(bool force = false);
    /// <summary>Katalog abonnieren; Fehlermeldung oder null.</summary>
    Task<string?> SubscribeCatalogAsync(string path, string? name = null);
    void UnsubscribeCatalog(CatalogSubscription subscription);
    Task ApplyPendingCatalogsAsync();

    // ── Updates ──────────────────────────────────────────────────────
    Task<ReleaseInfo?> CheckUpdatesAsync(bool manual, bool includePrereleases);
    Task InstallUpdateAsync();
    ReleaseInfo? AvailableUpdate { get; }
    string? UpdateError { get; }
    DateTime? LastUpdateCheck { get; }
    string CurrentVersionText { get; }
    bool IsInstalledViaSetup { get; }

    // ── Oberfläche ───────────────────────────────────────────────────
    void ShowWelcome();
    /// <summary>Monitore: Index → Anzeigename.</summary>
    IReadOnlyList<KeyValuePair<int, string>> Monitors { get; }
    /// <summary>Profil-Dialog modal zeigen (arbeitet auf der Arbeitskopie des Einstellungsfensters).</summary>
    void ShowProfilesDialog(ProfilesViewModel vm, object? owner);
}
