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
    public required string FullPath { get; set; }

    /// <summary>Der konfigurierte Stammordner, in dem die Datei gefunden wurde.</summary>
    public required string RootFolder { get; set; }

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

    /// <summary>Bei eigenen Einträgen: tatsächlich zu startendes Ziel (URL, Ordner, Programm).</summary>
    public string? LaunchTarget { get; set; }

    /// <summary>Bei eigenen Einträgen: feste Argumente.</summary>
    public string LaunchArguments { get; set; } = string.Empty;

    /// <summary>Art des eigenen Eintrags (null = Datei aus einem Ordner).</summary>
    public Config.CustomEntryKind? CustomKind { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCustom => CustomKind != null;

    /// <summary>Der eigene Eintrag, aus dem dieser Eintrag erzeugt wurde (Snippet-Code, Ein-Klick, Symbol …).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Config.CustomEntry? Source { get; set; }

    /// <summary>Platzhalter wurden für diesen Start bereits abgefragt und eingesetzt.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PlaceholdersResolved { get; set; }

    /// <summary>Kopie mit anderem Pfad (z. B. Snippet: Metadaten aus der .ps1, Schlüssel „custom:Id“).</summary>
    public LaunchItem Rebased(string fullPath, string rootFolder)
    {
        var c = (LaunchItem)MemberwiseClone();
        c.FullPath = fullPath;
        c.RootFolder = rootFolder;
        return c;
    }

    /// <summary>#Requires -RunAsAdministrator</summary>
    public bool RequiresAdmin { get; set; }

    /// <summary>#Requires -Version (z. B. „7.2“), leer = keine Angabe.</summary>
    public string RequiredPsVersion { get; set; } = string.Empty;

    /// <summary>Zonen-Kennung (3 = Internet). Gesetzt, wenn die Datei als heruntergeladen markiert ist.</summary>
    public int? ZoneId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsBlocked => Metadata.ZoneIdentifier.IsBlocked(ZoneId);

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
