using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerExec.Core.Config;

/// <summary>Lädt und speichert die Einstellungen als JSON. Enthält niemals Anmeldedaten.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string FilePath { get; }

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Power-Exec", "settings.json");
    }

    public bool Exists => File.Exists(FilePath);

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (s != null) return Normalize(s);
            }
        }
        catch
        {
            // Beschädigte Datei sichern, damit nichts stillschweigend verloren geht.
            try { File.Copy(FilePath, FilePath + ".defekt", overwrite: true); } catch { }
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public static AppSettings Clone(AppSettings s) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, Options), Options)!;

    private static AppSettings Normalize(AppSettings s)
    {
        if (s.DefaultExtensions.Count == 0) s.DefaultExtensions = new(AppSettings.StandardExtensions);
        s.HoverDelayMs = Math.Clamp(s.HoverDelayMs, 0, 3000);
        s.CollapseDelayMs = Math.Clamp(s.CollapseDelayMs, 0, 5000);
        s.PanelWidth = Math.Clamp(s.PanelWidth, 280, 900);
        s.PanelOpacity = Math.Clamp(s.PanelOpacity, 50, 100);
        if (!PanelColors.TryParse(s.PanelColor, out _)) s.PanelColor = string.Empty;
        s.ThrottleLimit = Math.Clamp(s.ThrottleLimit, 1, 128);
        s.RemoteTimeoutSeconds = Math.Clamp(s.RemoteTimeoutSeconds, 5, 600);
        s.MaxRecentMachines = Math.Clamp(s.MaxRecentMachines, 1, 200);
        return s;
    }
}
