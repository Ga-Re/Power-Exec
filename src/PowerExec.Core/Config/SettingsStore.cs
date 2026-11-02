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

    internal static AppSettings Normalize(AppSettings s)
    {
        if (s.DefaultExtensions.Count == 0) s.DefaultExtensions = new(AppSettings.StandardExtensions);
        s.HoverDelayMs = Math.Clamp(s.HoverDelayMs, 0, 3000);
        s.CollapseDelayMs = Math.Clamp(s.CollapseDelayMs, 0, 5000);
        s.PanelWidth = Math.Clamp(s.PanelWidth, 280, 900);
        s.PanelOpacity = Math.Clamp(s.PanelOpacity, 50, 100);
        s.PanelHeightPercent = Math.Clamp(s.PanelHeightPercent, 30, 100);
        s.HotZoneThickness = Math.Clamp(s.HotZoneThickness, 1, 40);
        s.HotZoneLength = Math.Clamp(s.HotZoneLength, 40, 4000);
        if (!PanelColors.TryParse(s.PanelColor, out _)) s.PanelColor = string.Empty;
        s.ThrottleLimit = Math.Clamp(s.ThrottleLimit, 1, 128);
        s.RemoteTimeoutSeconds = Math.Clamp(s.RemoteTimeoutSeconds, 5, 600);
        s.MaxRecentMachines = Math.Clamp(s.MaxRecentMachines, 1, 200);
        s.RecentRunsCount = Math.Clamp(s.RecentRunsCount, 0, 20);
        s.ScanTimeoutSeconds = Math.Clamp(s.ScanTimeoutSeconds, 2, 120);
        foreach (var f in s.Folders) FolderVisibility.MigrateLegacyExcludes(f);
        if (s.Panels.Count == 0) s.Panels.Add(PanelConfig.FromLegacy(s));
        foreach (var p in s.Panels) p.Normalize();
        if (s.Panels.Select(p => p.Id).Distinct().Count() != s.Panels.Count)
            foreach (var p in s.Panels) p.Id = Guid.NewGuid().ToString("N");
        WorkspaceManager.EnsureInitialized(s);
        s.MachineGroups.RemoveAll(g => string.IsNullOrWhiteSpace(g.Name));
        s.Entries.RemoveAll(e => string.IsNullOrWhiteSpace(e.Path));
        foreach (var e in s.Entries)
        {
            e.Profiles.RemoveAll(p => p == null);
            if (e.Profiles.Count > 0 && e.Profiles.Count(p => p.IsDefault) != 1)
            {
                var first = e.Profiles.FirstOrDefault(p => p.IsDefault) ?? e.Profiles[0];
                foreach (var p in e.Profiles) p.IsDefault = ReferenceEquals(p, first);
            }
        }
        return s;
    }
}
