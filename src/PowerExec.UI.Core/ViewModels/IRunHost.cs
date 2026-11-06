using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Services;

namespace PowerExec.ViewModels;

/// <summary>
/// Was der Ausführen-Dialog (<see cref="RunViewModel"/>) von der App braucht – ohne UI-Framework.
/// <c>owner</c> ist jeweils das Fenster der Oberfläche (WPF: <c>System.Windows.Window</c>) oder null.
/// </summary>
public interface IRunHost
{
    AppSettings Settings { get; }
    void SaveSettings();

    /// <summary>Rückfrage vor dem Start (z. B. bei vielen Rechnern); false = abgebrochen.</summary>
    bool ConfirmRun(LaunchItem item, LaunchProfile profile, int targetCount, object? owner);

    /// <summary>Startet die Remote-/Massenausführung; false = nicht gestartet (z. B. Anmeldung abgebrochen).</summary>
    bool StartRemote(ExecutionRequest request, object? owner, IReadOnlyDictionary<string, string?>? raw, LaunchProfile? profile);

    /// <summary>Startet lokal und verfolgt den Lauf in der Aktivitäten-Leiste; false = nicht gestartet.</summary>
    bool StartLocalTracked(LaunchItem item, ExecutionRequest request, Func<HistoryEntry> beginHistory, object? owner);

    HistoryEntry BeginHistory(LaunchItem item, LaunchProfile? profile, ExecutionMode mode, PsEngine engine,
        IEnumerable<string>? targets, IReadOnlyDictionary<string, string?>? raw, string origin = "");

    void RecordParameters(LaunchItem item, IReadOnlyDictionary<string, string?> raw);
    void RecordRun(LaunchItem item, LaunchProfile profile);

    /// <summary>Ist PowerShell 7 (pwsh.exe) auf diesem PC installiert?</summary>
    bool IsPwshInstalled { get; }

    /// <summary>Laufwerksbuchstabe eines Netzlaufwerks in UNC-Pfad umwandeln (sonst unverändert).</summary>
    string ToUnc(string path);

    /// <summary>Fragt Anmeldedaten ab; null = abgebrochen.</summary>
    RunCredential? PromptCredential(object? owner, string target, bool remote);

    /// <summary>Verbindungstest (WinRM, Anmeldung, PowerShell 7) für mehrere Rechner.</summary>
    Task<IReadOnlyList<RemoteTestResult>> TestConnectionsAsync(IReadOnlyList<string> targets, RunCredential? credential);
}
