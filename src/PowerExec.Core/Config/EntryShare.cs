using PowerExec.Core.Localization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerExec.Core.Config;

/// <summary>Eigene Einträge (und ihre Gruppen) als Datei weitergeben – ohne Kennwörter, ohne lokale Einstellungen.</summary>
public static class EntryShare
{
    public const string FileFilter = "Power-Exec-Einträge (*.pxentries.json)|*.pxentries.json|JSON (*.json)|*.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public sealed class SharedEntry
    {
        public CustomEntry Entry { get; set; } = new();
        public string Group { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public List<LaunchProfile> Profiles { get; set; } = new();
    }

    public sealed class SharedFile
    {
        public string Format { get; set; } = L.T("Power-Exec-Einträge");
        public int Version { get; set; } = 1;
        public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;
        public List<string> Groups { get; set; } = new();
        public Dictionary<string, string> GroupIcons { get; set; } = new();
        public List<SharedEntry> Entries { get; set; } = new();
    }

    /// <summary>Exportiert die angegebenen eigenen Einträge samt Gruppe, Notiz, Schlagworten und Profilen.</summary>
    public static string Export(AppSettings s, IEnumerable<CustomEntry> entries, string? rootGroup = null)
    {
        var file = new SharedFile();
        foreach (var e in entries)
        {
            var cfg = s.FindEntry(e.Key);
            var group = GroupManager.Normalize(cfg?.Category);
            file.Entries.Add(new SharedEntry
            {
                Entry = e.Clone(),
                Group = group,
                DisplayName = cfg?.DisplayName ?? string.Empty,
                Notes = cfg?.Notes ?? string.Empty,
                Tags = cfg?.Tags.ToList() ?? new(),
                Profiles = cfg?.Profiles.Select(p => p.Clone()).ToList() ?? new()
            });
        }
        var groups = GroupManager.All(s).Where(g => file.Entries.Any(x => x.Group.Length > 0 && GroupManager.IsSameOrBelow(x.Group, g))
                                                  || (rootGroup != null && GroupManager.IsSameOrBelow(g, rootGroup)));
        file.Groups = groups.ToList();
        foreach (var g in file.Groups)
            if (GroupManager.IconOf(s, g) is { IsEmpty: false } icon) file.GroupIcons[g] = icon.ToString();
        return JsonSerializer.Serialize(file, Options);
    }

    public static SharedFile Parse(string json)
    {
        var f = JsonSerializer.Deserialize<SharedFile>(json, Options);
        if (f == null || f.Entries == null) throw new InvalidDataException(L.T("Die Datei enthält keine Power-Exec-Einträge."));
        return f;
    }

    /// <summary>
    /// Übernimmt Einträge. Gleiche Kennung oder gleiches Ziel derselben Art = vorhandenen Eintrag aktualisieren.
    /// <paramref name="intoGroup"/> setzt alle importierten Gruppen darunter (z. B. „Von Kollegen“).
    /// </summary>
    /// <returns>(neu, aktualisiert)</returns>
    public static (int Added, int Updated) Import(AppSettings s, SharedFile file, string? intoGroup = null)
    {
        int added = 0, updated = 0;
        string Map(string g) => g.Length == 0 ? GroupManager.Normalize(intoGroup) :
            intoGroup is { Length: > 0 } ? GroupManager.Normalize(intoGroup + GroupManager.Separator + g) : g;

        foreach (var g in file.Groups.Select(GroupManager.Normalize).Where(g => g.Length > 0))
        {
            var target = Map(g);
            if (!GroupManager.Exists(s, target)) s.CategoryOrder.Add(target);
            if (file.GroupIcons.TryGetValue(g, out var icon) && GroupManager.IconOf(s, target).IsEmpty)
                GroupManager.SetIcon(s, target, IconRef.Parse(icon));
        }
        if (intoGroup is { Length: > 0 } && !GroupManager.Exists(s, intoGroup)) s.CategoryOrder.Add(GroupManager.Normalize(intoGroup));

        foreach (var shared in file.Entries)
        {
            var e = shared.Entry.Clone();
            var existing = s.CustomEntries.FirstOrDefault(c => c.Id == e.Id)
                           ?? s.CustomEntries.FirstOrDefault(c => c.Kind == e.Kind && c.Target.Trim().Equals(e.Target.Trim(), StringComparison.OrdinalIgnoreCase)
                                                                  && c.Code == e.Code);
            if (existing != null)
            {
                e.Id = existing.Id;
                s.CustomEntries[s.CustomEntries.IndexOf(existing)] = e;
                updated++;
            }
            else
            {
                if (s.CustomEntries.Any(c => c.Id == e.Id)) e.Id = Guid.NewGuid().ToString("N");
                s.CustomEntries.Add(e);
                added++;
            }
            var cfg = s.FindEntry(e.Key);
            if (cfg == null)
            {
                cfg = new EntryConfig { Path = e.Key };
                s.Entries.Add(cfg);
            }
            cfg.Category = Map(GroupManager.Normalize(shared.Group));
            if (shared.DisplayName.Length > 0) cfg.DisplayName = shared.DisplayName;
            if (shared.Notes.Length > 0) cfg.Notes = shared.Notes;
            cfg.Tags = cfg.Tags.Concat(shared.Tags).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            if (shared.Profiles.Count > 0) cfg.Profiles = shared.Profiles.Select(p => p.Clone()).ToList();
        }
        s.CategoryOrder = GroupManager.All(s).ToList();
        return (added, updated);
    }
}
