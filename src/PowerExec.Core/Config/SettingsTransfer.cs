using PowerExec.Core.Localization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerExec.Core.Config;

/// <summary>Einstellungen sichern und auf einem anderen PC wieder einlesen. Anmeldedaten gibt es darin nicht.</summary>
public static class SettingsTransfer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Export(AppSettings settings)
    {
        var copy = SettingsStore.Clone(settings);
        copy.Fingerprints.Clear(); // rechnerbezogen
        var envelope = new ExportFile { Version = 1, ExportedUtc = DateTime.UtcNow, Settings = copy };
        return JsonSerializer.Serialize(envelope, Options);
    }

    public static AppSettings Parse(string json)
    {
        var env = JsonSerializer.Deserialize<ExportFile>(json, Options);
        if (env?.Settings != null) return SettingsStore.Normalize(env.Settings);
        // Auch eine nackte settings.json akzeptieren
        var s = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? throw new InvalidDataException(L.T("The file contains no Power-Exec settings."));
        return SettingsStore.Normalize(s);
    }

    /// <summary>
    /// Führt importierte Einstellungen mit den vorhandenen zusammen: Ordner, Einträge, Favoriten, Rechner und Kategorien
    /// werden ergänzt; bei gleichen Ordnern bzw. Einträgen gewinnt der Import. Allgemeine Einstellungen bleiben unverändert.
    /// </summary>
    public static AppSettings Merge(AppSettings current, AppSettings imported)
    {
        var result = SettingsStore.Clone(current);
        foreach (var f in imported.Folders)
        {
            result.Folders.RemoveAll(x => x.Path.Equals(f.Path, StringComparison.OrdinalIgnoreCase));
            result.Folders.Add(f.Clone());
        }
        foreach (var e in imported.Entries)
        {
            result.Entries.RemoveAll(x => x.Path.Equals(e.Path, StringComparison.OrdinalIgnoreCase));
            result.Entries.Add(e.Clone());
        }
        foreach (var fav in imported.Favorites)
            if (!result.Favorites.Contains(fav, StringComparer.OrdinalIgnoreCase)) result.Favorites.Add(fav);
        foreach (var m in imported.Machines)
        {
            var existing = result.Machines.FirstOrDefault(x => x.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
            if (existing == null) result.Machines.Add(new MachineEntry { Name = m.Name, Favorite = m.Favorite, LastUsedUtc = m.LastUsedUtc });
            else existing.Favorite |= m.Favorite;
        }
        foreach (var c in imported.CategoryOrder)
            if (!result.CategoryOrder.Contains(c, StringComparer.CurrentCultureIgnoreCase)) result.CategoryOrder.Add(c);
        foreach (var g in imported.GlobalExcludes)
            if (!result.GlobalExcludes.Contains(g, StringComparer.OrdinalIgnoreCase)) result.GlobalExcludes.Add(g);
        foreach (var c in imported.CustomEntries)
        {
            result.CustomEntries.RemoveAll(x => x.Id == c.Id);
            result.CustomEntries.Add(c.Clone());
        }
        foreach (var g in imported.MachineGroups)
        {
            result.MachineGroups.RemoveAll(x => x.Name.Equals(g.Name, StringComparison.OrdinalIgnoreCase));
            result.MachineGroups.Add(g.Clone());
        }
        // Arbeitsbereiche: nur neue übernehmen (der aktive Stand steht bereits oben in Ordnern, Einträgen …)
        WorkspaceManager.EnsureInitialized(result);
        foreach (var w in imported.Workspaces.Where(w => !w.Name.Equals(imported.ActiveWorkspace, StringComparison.OrdinalIgnoreCase)))
            if (WorkspaceManager.Find(result, w.Name) == null) result.Workspaces.Add(w);
        // Panels bleiben wie sie sind (Lage und Monitore sind rechnerbezogen)
        return result;
    }

    private sealed class ExportFile
    {
        public string Format { get; set; } = L.T("Power-Exec settings");
        public int Version { get; set; }
        public DateTime ExportedUtc { get; set; }
        public AppSettings? Settings { get; set; }
    }
}
