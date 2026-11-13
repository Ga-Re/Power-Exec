using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.UI.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI.Core.Tests;

/// <summary>App-Ersatz für den Ausführen-Dialog: merkt sich alle Aufrufe, startet nichts.</summary>
internal sealed class FakeRunHost : IRunHost
{
    public AppSettings Settings { get; } = new();
    public bool IsPwshInstalled { get; set; } = true;
    public bool ConfirmResult { get; set; } = true;
    public bool StartResult { get; set; } = true;
    public RunCredential? Credential { get; set; }

    public int SaveCount { get; private set; }
    public List<ExecutionRequest> LocalStarts { get; } = new();
    public List<ExecutionRequest> RemoteStarts { get; } = new();
    public List<HistoryEntry> HistoryEntries { get; } = new();
    public List<IReadOnlyDictionary<string, string?>> RecordedParameters { get; } = new();
    public List<LaunchProfile> RecordedRuns { get; } = new();
    public List<IReadOnlyList<string>> ConnectionTests { get; } = new();
    public int CredentialPrompts { get; private set; }

    public void SaveSettings() => SaveCount++;

    public bool ConfirmRun(LaunchItem item, LaunchProfile profile, int targetCount, object? owner) => ConfirmResult;

    public bool StartRemote(ExecutionRequest request, object? owner, IReadOnlyDictionary<string, string?>? raw, LaunchProfile? profile)
    {
        RemoteStarts.Add(request);
        return StartResult;
    }

    public bool StartLocalTracked(LaunchItem item, ExecutionRequest request, Func<HistoryEntry> beginHistory, object? owner)
    {
        LocalStarts.Add(request);
        HistoryEntries.Add(beginHistory());
        return StartResult;
    }

    public HistoryEntry BeginHistory(LaunchItem item, LaunchProfile? profile, ExecutionMode mode, PsEngine engine,
        IEnumerable<string>? targets, IReadOnlyDictionary<string, string?>? raw, string origin = "") =>
        RunHistory.Create(item, item.DisplayName, profile?.Name ?? "Standard", mode, engine, targets, raw);

    public void RecordParameters(LaunchItem item, IReadOnlyDictionary<string, string?> raw) => RecordedParameters.Add(raw);

    public void RecordRun(LaunchItem item, LaunchProfile profile) => RecordedRuns.Add(profile);

    public string ToUnc(string path) => path;

    public RunCredential? PromptCredential(object? owner, string target, bool remote)
    {
        CredentialPrompts++;
        return Credential;
    }

    public Task<IReadOnlyList<RemoteTestResult>> TestConnectionsAsync(IReadOnlyList<string> targets, RunCredential? credential)
    {
        ConnectionTests.Add(targets);
        IReadOnlyList<RemoteTestResult> results = targets
            .Select(t => new RemoteTestResult { Computer = t, WsMan = true, Auth = !t.Equals("kaputt", StringComparison.OrdinalIgnoreCase), Version = "5.1" })
            .ToList();
        return Task.FromResult(results);
    }
}
