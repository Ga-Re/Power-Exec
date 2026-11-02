namespace PowerExec.Core.Models;

/// <summary>Art eines gefundenen Eintrags, abgeleitet von der Dateiendung.</summary>
public enum ItemType
{
    PowerShell,
    Batch,
    Executable,
    Shortcut,
    ConsoleSnapIn,
    Other
}

/// <summary>Ein Script oder Programm, das in einem konfigurierten Ordner gefunden wurde.</summary>
public sealed class LaunchItem
{
    public required string FullPath { get; init; }

    /// <summary>Der konfigurierte Stammordner, in dem die Datei gefunden wurde.</summary>
    public required string RootFolder { get; init; }

    public required ItemType Type { get; init; }

    public string FileName => Path.GetFileName(FullPath);

    public string Directory => Path.GetDirectoryName(FullPath) ?? string.Empty;

    /// <summary>Anzeigename im Panel (z. B. Dateiname oder Dateibeschreibung einer EXE).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Kurzinfo: .SYNOPSIS, Dateibeschreibung, REM-Kommentar oder Verknüpfungskommentar.</summary>
    public string Synopsis { get; set; } = string.Empty;

    /// <summary>Ausführliche Beschreibung (.DESCRIPTION), optional.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Parameter aus dem param()-Block (nur PowerShell).</summary>
    public IReadOnlyList<ScriptParameter> Parameters { get; set; } = Array.Empty<ScriptParameter>();

    /// <summary>Module aus #Requires -Modules.</summary>
    public IReadOnlyList<string> RequiredModules { get; set; } = Array.Empty<string>();

    /// <summary>Das Script verwendet Read-Host und kann remote hängen bleiben.</summary>
    public bool UsesReadHost { get; set; }

    /// <summary>.NOTES enthält "PowerExec: CopyFolder" → Scriptordner bei Remote standardmäßig mitsenden.</summary>
    public bool CopyFolderByDefault { get; set; }

    /// <summary>Parserfehler, falls das Script nicht gelesen werden konnte.</summary>
    public string? ParseError { get; set; }

    /// <summary>Bei Verknüpfungen: aufgelöstes Ziel (für "als anderer Benutzer").</summary>
    public ShortcutTarget? Shortcut { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public bool HasParameters => Parameters.Count > 0;

    public bool IsPowerShell => Type == ItemType.PowerShell;

    /// <summary>Remote-Ausführung ist nur für PowerShell-Scripte vorgesehen.</summary>
    public bool SupportsRemote => Type == ItemType.PowerShell;

    /// <summary>Text, der von der Suche durchsucht wird.</summary>
    public string SearchText => $"{DisplayName} {FileName} {Synopsis} {Description}";

    public static ItemType TypeFromExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ps1" => ItemType.PowerShell,
            ".cmd" or ".bat" => ItemType.Batch,
            ".exe" => ItemType.Executable,
            ".lnk" => ItemType.Shortcut,
            ".msc" => ItemType.ConsoleSnapIn,
            _ => ItemType.Other
        };
}

public sealed record ShortcutTarget(string TargetPath, string Arguments, string WorkingDirectory);
