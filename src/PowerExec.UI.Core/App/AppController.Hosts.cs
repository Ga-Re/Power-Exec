using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Updates;
using PowerExec.Infrastructure;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec;

/// <summary>
/// Brücke zwischen den UI-neutralen ViewModels (PowerExec.UI.Core) und der WPF-App: übersetzt den
/// Fensterbezug (<c>object? owner</c>) in <see cref="Window"/> und reicht WPF-/Win32-Dienste durch.
/// Was schon öffentlich mit passender Signatur existiert, erfüllt die Schnittstellen direkt.
/// </summary>
public partial class AppController : IRunHost, IHistoryHost, IScheduleHost, IWorkflowHost, IWelcomeHost, IPanelHost, IResultsHost, IProfilesHost, ICustomEntryListHost, ISettingsHost, ISettingsWindowHost
{
    /// <summary>Explorer, Browser, Zwischenablage.</summary>
    public IShell Shell => Ui.Shell;

    /// <summary>Rückfragen (im Selbsttest über <see cref="AskForTest"/> beantwortet).</summary>
    public IDialogs Dialogs => Ui.Dialogs;

    /// <summary>Symbole für die UI-neutralen ViewModels.</summary>
    public IIconProvider IconProvider => Ui.Icons;

    /// <summary>UI-Thread der App für die UI-neutralen Dienste.</summary>
    public IUiDispatcher UiDispatcher => Ui.Dispatcher;

    /// <summary>Kontrastdesign, hell/dunkel.</summary>
    public IThemeInfo Theme => Ui.Theme;

    // ── Ausführen-Dialog ─────────────────────────────────────────────

    bool IRunHost.ConfirmRun(LaunchItem item, LaunchProfile profile, int targetCount, object? owner) =>
        ConfirmRun(item, profile, targetCount, owner);

    bool IRunHost.StartRemote(ExecutionRequest request, object? owner, IReadOnlyDictionary<string, string?>? raw, LaunchProfile? profile) =>
        StartRemote(request, owner, raw, profile);

    bool IRunHost.StartLocalTracked(LaunchItem item, ExecutionRequest request, Func<HistoryEntry> beginHistory, object? owner) =>
        StartLocalTracked(item, request, beginHistory, owner);

    bool IRunHost.IsPwshInstalled => Launcher.IsPwshInstalled;

    string IRunHost.ToUnc(string path) => PathResolver.ToUnc(path);

    RunCredential? IRunHost.PromptCredential(object? owner, string target, bool remote) =>
        Ui.AskCredential(owner, target, remote);

    Task<IReadOnlyList<RemoteTestResult>> IRunHost.TestConnectionsAsync(IReadOnlyList<string> targets, RunCredential? credential) =>
        RemoteTestService.RunAsync(targets, Settings, credential);

    // ── Verlauf ──────────────────────────────────────────────────────

    void IHistoryHost.OpenLocation(string path) => Shell.OpenLocation(path);

    // ── Ablauf-Editor ────────────────────────────────────────────────

    Task<bool> IWorkflowHost.RunWorkflowAsync(string name, IReadOnlyList<WorkflowStep> steps, bool stopOnError) =>
        RunWorkflowAsync(name, steps, stopOnError);

    // ── Panel, Gruppen, Einträge ─────────────────────────────────────

    /// <summary>„WPF 0.19.5 · installiert“ bzw. „… · portabel (installiert: 0.19.4)“.</summary>
    public string VersionText => _versionText ??= BuildVersionText();
    private string? _versionText;

    private static string BuildVersionText()
    {
        var running = $"Version {UpdateService.CurrentVersionText}";
        if (UpdateService.IsInstalledViaSetup) return running + " · installiert";
        return InstalledVersion.Read() is { } v ? $"{running} · portabel (installiert: {v})" : running + " · portabel";
    }

    IItemSource IPanelHost.Repository => Repository;

    void IPanelHost.OpenInEditor(LaunchItem item) => Launcher.OpenInEditor(item);

    bool IPanelHost.ImportEntries(string? intoGroup) => ImportEntries(intoGroup);

    void IPanelHost.ShowPanelFromHotkey(IPanelContext? panel, bool forcePanel) => ShowPanelFromHotkey(panel as AppPanel, forcePanel);

    void IPanelHost.RememberForUndo(string description) => RememberForUndo(description);

    void IPanelHost.ShowUndoToast(string text) => ShowUndoToast(text);

    // ── Ergebnisliste ────────────────────────────────────────────────

    bool IResultsHost.RetryRemote(ExecutionRequest request) => StartRemote(request, null);

    // ── Eintrags- und Profil-Dialog ──────────────────────────────────

    string ICustomEntryHost.ToUnc(string path) => PathResolver.ToUnc(path);

    object? ISettingsHost.SettingsDialogOwner => _settingsWindow?.Owner;

    IItemSource ISettingsHost.Repository => Repository;

    Task<IReadOnlyList<string>?> ISettingsHost.SyncMachineGroupAsync(MachineGroup group) => SyncMachineGroupAsync(group);

    Task<IReadOnlyList<string>> ISettingsHost.FindComputersAsync(string query) => AdSearch.FindComputersAsync(query);

    EntryEditorResult ICustomEntryListHost.EditEntryModal(AppSettings target, CustomEntry? entry, bool isNew, string? group, object? owner) =>
        Ui.EditEntry(target, entry, isNew, group, null, owner);

    void ICustomEntryListHost.ExportEntries(IReadOnlyList<CustomEntry> entries, string? rootGroup, object? owner, AppSettings? source) =>
        ExportEntries(entries, rootGroup, owner, source);

    // ── Einstellungsfenster ──────────────────────────────────────────

    string ISettingsWindowHost.SettingsFilePath => Store.FilePath;
    ReleaseInfo? ISettingsWindowHost.AvailableUpdate => Updates.Available;
    string? ISettingsWindowHost.UpdateError => Updates.LastError;
    DateTime? ISettingsWindowHost.LastUpdateCheck => Updates.LastCheck;
    string ISettingsWindowHost.CurrentVersionText => UpdateService.CurrentVersionText;
    bool ISettingsWindowHost.IsInstalledViaSetup => UpdateService.IsInstalledViaSetup;
    bool ISettingsWindowHost.IsMicaSupported => Ui.IsMicaSupported;

    IReadOnlyList<KeyValuePair<int, string>> ISettingsWindowHost.Monitors => Ui.Monitors;

    void ISettingsWindowHost.ShowProfilesDialog(ProfilesViewModel vm, object? owner) => Ui.ShowProfilesModal(vm, owner);

    // ── Einführung ───────────────────────────────────────────────────

    string? IWelcomeHost.PickFolder(string title) => Dialogs.PickFolder(title);
}
