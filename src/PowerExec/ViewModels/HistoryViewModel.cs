using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Infrastructure;

namespace PowerExec.ViewModels;

/// <summary>Eine Zeile im Verlauf.</summary>
public sealed class HistoryRowViewModel
{
    public HistoryRowViewModel(AppController app, HistoryEntry e)
    {
        Entry = e;
        RunAgainCommand = new RelayCommand(() => app.Replay(e));
        RetryFailedCommand = new RelayCommand(() => app.Replay(e, onlyFailed: true), () => e.FailedTargets.Count > 0);
        OpenLogCommand = new RelayCommand(() => Services.Launcher.OpenLocation(e.LogFile!), () => e.LogFile != null && File.Exists(e.LogFile));
    }

    public HistoryEntry Entry { get; }
    public string Time => Entry.StartUtc.ToLocalTime() is var t && t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("dd.MM. HH:mm");
    public string Name => Entry.Name;
    public string Where => Entry.IsRemote
        ? (Entry.Targets.Count == 1 ? Entry.Targets[0] : $"{Entry.Targets.Count} Rechner")
        : Entry.Mode switch { ExecutionMode.Administrator => "lokal · Admin", ExecutionMode.OtherUser => "lokal · anderer Benutzer", _ => "lokal" };
    public string Meta => string.Join(" · ", new[]
    {
        Where,
        Entry.ProfileName is "Standard" or "Verlauf" or "" ? null : Entry.ProfileName,
        Entry.Origin.Length > 0 ? Entry.Origin : null,
        Entry.Result == HistoryResult.Running ? "läuft …" : Entry.Summary.Length > 0 ? Entry.Summary : null
    }.Where(s => !string.IsNullOrEmpty(s)));
    public string Duration => Entry.Duration is { } d ? ActivityViewModel.Format(d) : string.Empty;
    /// <summary>Parameter in einer Zeile, z. B. „-ComputerName SRV01 -Force“.</summary>
    public string ParametersText => string.Join(" ", Entry.Parameters.Select(kv => kv.Value is "true" ? "-" + kv.Key : $"-{kv.Key} {kv.Value}"));
    public bool HasParameters => Entry.Parameters.Count > 0;
    public bool IsSuccess => Entry.Result == HistoryResult.Success;
    public bool IsError => Entry.Result == HistoryResult.Error;
    public bool IsWarning => Entry.Result == HistoryResult.Warning;
    public bool IsRunning => Entry.Result == HistoryResult.Running;
    public bool CanRetryFailed => Entry.FailedTargets.Count > 0;
    public string RetryText => $"Nur {Entry.FailedTargets.Count} fehlgeschlagene";
    public bool HasLog => Entry.LogFile != null;

    public ICommand RunAgainCommand { get; }
    public ICommand RetryFailedCommand { get; }
    public ICommand OpenLogCommand { get; }
}
