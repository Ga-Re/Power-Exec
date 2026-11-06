using PowerExec.Core.Models;
using PowerExec.Core.Scanning;

namespace PowerExec.Services;

/// <summary>Ergebnis des Einlesens eines Ordners.</summary>
public sealed record FolderStatus(string Path, int Count, IReadOnlyList<ExcludedFile> ExcludedFiles, string? Error)
{
    public int Excluded => ExcludedFiles.Count;

    /// <summary>Dauer des Einlesens.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Angezeigt wird der zwischengespeicherte Stand (Ordner nicht erreichbar oder Start).</summary>
    public bool FromCache { get; init; }
    public DateTime? CachedAtUtc { get; init; }
}

/// <summary>Die gefundenen Einträge aller Ordner (WPF: <c>ItemRepository</c> mit Dateiwächtern).</summary>
public interface IItemSource
{
    IReadOnlyList<LaunchItem> Items { get; }
    IReadOnlyList<FolderStatus> FolderStatuses { get; }
    DateTime LastScan { get; }
    bool IsScanning { get; }

    /// <summary>Alle Ordner neu einlesen.</summary>
    Task ScanAsync();
}
