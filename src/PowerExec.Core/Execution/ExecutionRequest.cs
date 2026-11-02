using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Execution;

public enum ExecutionMode { CurrentUser, Administrator, OtherUser }

/// <summary>Alles, was für einen Start nötig ist – außer Anmeldedaten, die nie hier landen.</summary>
public sealed class ExecutionRequest
{
    public required LaunchItem Item { get; init; }
    public ExecutionMode Mode { get; init; } = ExecutionMode.CurrentUser;
    public PsEngine Engine { get; init; } = PsEngine.WindowsPowerShell;
    public bool NoExit { get; init; } = true;
    public bool NoProfile { get; init; } = true;

    /// <summary>"Bypass", "RemoteSigned" … oder leer für den Systemstandard.</summary>
    public string ExecutionPolicy { get; init; } = "Bypass";

    /// <summary>Parameterwerte, bereits typisiert (siehe <see cref="ParameterValueConverter"/>).</summary>
    public IReadOnlyDictionary<string, object> Parameters { get; init; } = new Dictionary<string, object>();

    /// <summary>Leer = lokal ausführen.</summary>
    public IReadOnlyList<string> Targets { get; init; } = Array.Empty<string>();

    public bool CopyFolder { get; init; }

    /// <summary>Zusätzliche Argumente für EXE, Verknüpfungen, Batch und Snap-ins (aus dem Startprofil).</summary>
    public string ExtraArguments { get; init; } = string.Empty;

    /// <summary>Pfad, der tatsächlich gestartet wird (bei Netzlaufwerken bereits als UNC-Pfad).</summary>
    public string? ResolvedPath { get; init; }

    public bool IsRemote => Targets.Count > 0;

    public string EffectivePath => ResolvedPath ?? Item.FullPath;
}
