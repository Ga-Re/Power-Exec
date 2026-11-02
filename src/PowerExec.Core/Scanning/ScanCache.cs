using System.Text.Json;
using System.Text.Json.Serialization;
using PowerExec.Core.Models;

namespace PowerExec.Core.Scanning;

public sealed class CachedFolder
{
    public string Path { get; set; } = string.Empty;
    public DateTime ScannedUtc { get; set; }
    public List<LaunchItem> Items { get; set; } = new();
    public List<ExcludedFile> Excluded { get; set; } = new();
}

/// <summary>Zwischenspeicher des letzten erfolgreichen Scans je Ordner (für schnellen Start und nicht erreichbare Freigaben).</summary>
public sealed class ScanCache
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string FilePath { get; }

    public ScanCache(string? filePath = null)
    {
        FilePath = filePath ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec", "scan-cache.json");
    }

    public Dictionary<string, CachedFolder> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<CachedFolder>>(File.ReadAllText(FilePath), Options);
                if (list != null)
                    return list.Where(f => !string.IsNullOrEmpty(f.Path))
                               .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                               .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Defekter Zwischenspeicher: einfach neu aufbauen
        }
        return new Dictionary<string, CachedFolder>(StringComparer.OrdinalIgnoreCase);
    }

    public void Save(IEnumerable<CachedFolder> folders)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(folders.ToList(), Options));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // Zwischenspeicher ist optional
        }
    }
}
