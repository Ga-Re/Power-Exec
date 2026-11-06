using System.Windows;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Services;
using PowerExec.ViewModels;
using PowerExec.Views;

namespace PowerExec;

/// <summary>
/// Brücke zwischen den UI-neutralen ViewModels (PowerExec.UI.Core) und der WPF-App: übersetzt den
/// Fensterbezug (<c>object? owner</c>) in <see cref="Window"/> und reicht WPF-/Win32-Dienste durch.
/// Was schon öffentlich mit passender Signatur existiert, erfüllt die Schnittstellen direkt.
/// </summary>
public sealed partial class AppController : IRunHost, IHistoryHost, IScheduleHost, IWorkflowHost, IWelcomeHost
{
    /// <summary>Explorer, Browser, Zwischenablage.</summary>
    public IShell Shell { get; } = new WpfShell();

    /// <summary>Rückfragen (im Selbsttest über <see cref="AskForTest"/> beantwortet).</summary>
    public IDialogs Dialogs => _uiDialogs ??= new WpfDialogs(() => AskForTest);
    private IDialogs? _uiDialogs;

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

    // ── Einführung ───────────────────────────────────────────────────

    string? IWelcomeHost.PickFolder(string title)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }
}
