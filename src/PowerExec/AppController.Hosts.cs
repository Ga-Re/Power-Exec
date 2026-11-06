using System.Windows;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Updates;
using PowerExec.Infrastructure;
using PowerExec.Services;
using PowerExec.ViewModels;
using PowerExec.Views;

namespace PowerExec;

/// <summary>
/// Brücke zwischen den UI-neutralen ViewModels (PowerExec.UI.Core) und der WPF-App: übersetzt den
/// Fensterbezug (<c>object? owner</c>) in <see cref="Window"/> und reicht WPF-/Win32-Dienste durch.
/// Was schon öffentlich mit passender Signatur existiert, erfüllt die Schnittstellen direkt.
/// </summary>
public sealed partial class AppController : IRunHost, IHistoryHost, IScheduleHost, IWorkflowHost, IWelcomeHost, IPanelHost, IResultsHost, IProfilesHost, ICustomEntryListHost, ISettingsHost, ISettingsWindowHost
{
    /// <summary>Explorer, Browser, Zwischenablage.</summary>
    public IShell Shell { get; } = new WpfShell();

    /// <summary>Rückfragen (im Selbsttest über <see cref="AskForTest"/> beantwortet).</summary>
    public IDialogs Dialogs => _uiDialogs ??= new WpfDialogs(() => AskForTest);
    private IDialogs? _uiDialogs;

    /// <summary>Symbole für die UI-neutralen ViewModels (Griff auf die Bilder des <see cref="IconService"/>).</summary>
    public IIconProvider IconProvider => _iconProvider ??= new WpfIconProvider(Icons);
    private IIconProvider? _iconProvider;

    /// <summary>UI-Thread der App für die UI-neutralen Dienste.</summary>
    public IUiDispatcher UiDispatcher => _uiDispatcher ??= new WpfUiDispatcher(Application.Current.Dispatcher);
    private IUiDispatcher? _uiDispatcher;

    /// <summary>Kontrastdesign, hell/dunkel.</summary>
    public IThemeInfo Theme { get; } = new WpfThemeInfo();

    // ── Ausführen-Dialog ─────────────────────────────────────────────

    bool IRunHost.ConfirmRun(LaunchItem item, LaunchProfile profile, int targetCount, object? owner) =>
        ConfirmRun(item, profile, targetCount, owner as Window);

    bool IRunHost.StartRemote(ExecutionRequest request, object? owner, IReadOnlyDictionary<string, string?>? raw, LaunchProfile? profile) =>
        StartRemote(request, owner as Window, raw, profile);

    bool IRunHost.StartLocalTracked(LaunchItem item, ExecutionRequest request, Func<HistoryEntry> beginHistory, object? owner) =>
        StartLocalTracked(item, request, beginHistory, owner as Window);

    bool IRunHost.IsPwshInstalled => Launcher.IsPwshInstalled;

    string IRunHost.ToUnc(string path) => PathResolver.ToUnc(path);

    RunCredential? IRunHost.PromptCredential(object? owner, string target, bool remote) =>
        CredentialWindow.Prompt(owner as Window, target, remote);

    Task<IReadOnlyList<RemoteTestResult>> IRunHost.TestConnectionsAsync(IReadOnlyList<string> targets, RunCredential? credential) =>
        RemoteTestService.RunAsync(targets, Settings, credential);

    // ── Verlauf ──────────────────────────────────────────────────────

    void IHistoryHost.OpenLocation(string path) => Launcher.OpenLocation(path);

    // ── Ablauf-Editor ────────────────────────────────────────────────

    Task<bool> IWorkflowHost.RunWorkflowAsync(string name, IReadOnlyList<WorkflowStep> steps, bool stopOnError) =>
        RunWorkflowAsync(name, steps, stopOnError);

    // ── Panel, Gruppen, Einträge ─────────────────────────────────────

    IItemSource IPanelHost.Repository => Repository;

    void IPanelHost.OpenInEditor(LaunchItem item) => Launcher.OpenInEditor(item);

    bool IPanelHost.ImportEntries(string? intoGroup) => ImportEntries(intoGroup);

    void IPanelHost.ShowPanelFromHotkey(IPanelContext? panel, bool forcePanel) => ShowPanelFromHotkey(panel as PanelHost, forcePanel);

    void IPanelHost.RememberForUndo(string description) => RememberForUndo(description);

    void IPanelHost.ShowUndoToast(string text) => ShowUndoToast(text);

    // ── Ergebnisliste ────────────────────────────────────────────────

    bool IResultsHost.RetryRemote(ExecutionRequest request) => StartRemote(request, null);

    // ── Eintrags- und Profil-Dialog ──────────────────────────────────

    string ICustomEntryHost.ToUnc(string path) => PathResolver.ToUnc(path);

    object? ISettingsHost.SettingsDialogOwner => Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();

    IItemSource ISettingsHost.Repository => Repository;

    Task<IReadOnlyList<string>?> ISettingsHost.SyncMachineGroupAsync(MachineGroup group) => SyncMachineGroupAsync(group);

    Task<IReadOnlyList<string>> ISettingsHost.FindComputersAsync(string query) => AdSearch.FindComputersAsync(query);

    EntryEditorResult ICustomEntryListHost.EditEntryModal(AppSettings target, CustomEntry? entry, bool isNew, string? group, object? owner) =>
        ProfilesWindow.EditEntryEx(this, target, entry, isNew, saveImmediately: false, group, null, owner as Window);

    void ICustomEntryListHost.ExportEntries(IReadOnlyList<CustomEntry> entries, string? rootGroup, object? owner, AppSettings? source) =>
        ExportEntries(entries, rootGroup, owner as Window, source);

    // ── Einstellungsfenster ──────────────────────────────────────────

    string ISettingsWindowHost.SettingsFilePath => Store.FilePath;
    ReleaseInfo? ISettingsWindowHost.AvailableUpdate => Updates.Available;
    string? ISettingsWindowHost.UpdateError => Updates.LastError;
    DateTime? ISettingsWindowHost.LastUpdateCheck => Updates.LastCheck;
    string ISettingsWindowHost.CurrentVersionText => UpdateService.CurrentVersionText;
    bool ISettingsWindowHost.IsInstalledViaSetup => UpdateService.IsInstalledViaSetup;
    bool ISettingsWindowHost.IsMicaSupported => WindowHelper.IsMicaSupported;

    IReadOnlyList<KeyValuePair<int, string>> ISettingsWindowHost.Monitors =>
        ScreenService.GetMonitors().Select(m => new KeyValuePair<int, string>(m.Index, m.DisplayName)).ToList();

    void ISettingsWindowHost.ShowProfilesDialog(ProfilesViewModel vm, object? owner)
    {
        var w = new ProfilesWindow(vm, this);
        if (owner is Window o) w.Owner = o;
        w.ShowDialog();
    }

    // ── Einführung ───────────────────────────────────────────────────

    string? IWelcomeHost.PickFolder(string title)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }
}
