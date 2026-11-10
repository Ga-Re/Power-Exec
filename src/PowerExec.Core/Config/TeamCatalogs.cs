using PowerExec.Core.Localization;
using System.Security.Cryptography;
using System.Text;

namespace PowerExec.Core.Config;

/// <summary>Abonnierter Team-Katalog (D3): eine Exportdatei (*.pxentries.json), meist auf einer Freigabe.</summary>
public sealed class CatalogSubscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Path { get; set; } = string.Empty;
    /// <summary>Name = Gruppe im Panel, unter der die Einträge erscheinen.</summary>
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    /// <summary>Änderungen sofort übernehmen; sonst erst nach Klick auf „Übernehmen“.</summary>
    public bool AutoApply { get; set; } = true;
    public string LastHash { get; set; } = string.Empty;
    public DateTime? LastCheckedUtc { get; set; }
    public DateTime? LastAppliedUtc { get; set; }
    public string LastError { get; set; } = string.Empty;
    /// <summary>Gefundene, noch nicht übernommene Änderung („3 neu, 1 geändert“).</summary>
    public string PendingSummary { get; set; } = string.Empty;
}

/// <summary>Unterschied zwischen Katalogdatei und dem übernommenen Stand.</summary>
public sealed record CatalogDiff(IReadOnlyList<string> Added, IReadOnlyList<string> Changed, IReadOnlyList<string> Removed)
{
    public bool IsEmpty => Added.Count + Changed.Count + Removed.Count == 0;
    public string Summary => IsEmpty ? L.T("no changes") : string.Join(", ", new[]
    {
        Added.Count > 0 ? L.F("{0} new", Added.Count) : null,
        Changed.Count > 0 ? L.F("{0} changed", Changed.Count) : null,
        Removed.Count > 0 ? L.F("{0} removed", Removed.Count) : null
    }.Where(x => x != null));
}

public static class TeamCatalogs
{
    public static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>Kennung eines Katalogeintrags in den lokalen Einstellungen (Katalog + Kennung beim Herausgeber).</summary>
    public static string LocalId(CatalogSubscription sub, CustomEntry e) => $"cat-{sub.Id}-{e.Id}";

    public static IEnumerable<CustomEntry> EntriesOf(AppSettings s, CatalogSubscription sub) =>
        s.CustomEntries.Where(c => c.CatalogId == sub.Id);

    private static string Fingerprint(CustomEntry e, EntryShare.SharedEntry? shared = null) =>
        System.Text.Json.JsonSerializer.Serialize(new { e.Kind, e.Name, e.Target, e.Arguments, e.Code, e.Description, e.Icon, Steps = e.Steps.Select(x => x.Path),
            Group = shared?.Group, Tags = shared?.Tags, Notes = shared?.Notes, Profiles = shared?.Profiles.Count });

    public static CatalogDiff Diff(AppSettings s, CatalogSubscription sub, EntryShare.SharedFile file)
    {
        var current = EntriesOf(s, sub).ToDictionary(c => c.Id);
        var added = new List<string>();
        var changed = new List<string>();
        foreach (var shared in file.Entries)
        {
            var id = LocalId(sub, shared.Entry);
            var name = shared.DisplayName.Length > 0 ? shared.DisplayName : shared.Entry.ToLaunchItem().DisplayName;
            if (!current.TryGetValue(id, out var existing)) added.Add(name);
            else if (existing.CatalogFingerprint != Fingerprint(shared.Entry, shared)) changed.Add(name);
            current.Remove(id);
        }
        return new CatalogDiff(added, changed, current.Values.Select(c => c.ToLaunchItem().DisplayName).ToList());
    }

    /// <summary>
    /// Katalog übernehmen: Einträge unter der Gruppe <see cref="CatalogSubscription.Name"/> anlegen bzw. aktualisieren,
    /// nicht mehr enthaltene entfernen. Eigene Ergänzungen (Favoriten, eigene Einträge in der Gruppe) bleiben.
    /// </summary>
    public static CatalogDiff Apply(AppSettings s, CatalogSubscription sub, EntryShare.SharedFile file, DateTime? nowUtc = null)
    {
        var diff = Diff(s, sub, file);
        var root = GroupManager.Normalize(sub.Name);
        string Map(string g) => g.Length == 0 ? root : GroupManager.Normalize(root + GroupManager.Separator + g);

        if (root.Length > 0 && !GroupManager.Exists(s, root)) s.CategoryOrder.Add(root);
        foreach (var g in file.Groups.Select(GroupManager.Normalize).Where(g => g.Length > 0))
        {
            var target = Map(g);
            if (!GroupManager.Exists(s, target)) s.CategoryOrder.Add(target);
            if (file.GroupIcons.TryGetValue(g, out var icon) && GroupManager.IconOf(s, target).IsEmpty)
                GroupManager.SetIcon(s, target, IconRef.Parse(icon));
        }

        var keep = new HashSet<string>();
        foreach (var shared in file.Entries)
        {
            var e = shared.Entry.Clone();
            e.Id = LocalId(sub, shared.Entry);
            e.CatalogId = sub.Id;
            e.CatalogFingerprint = Fingerprint(shared.Entry, shared);
            // Abläufe im Katalog verweisen auf Katalogeinträge: Kennungen umschreiben
            foreach (var step in e.Steps.Where(x => x.Path.StartsWith(PanelConfig.CustomEntriesKey, StringComparison.OrdinalIgnoreCase)))
                step.Path = PanelConfig.CustomEntriesKey + $"cat-{sub.Id}-" + step.Path[PanelConfig.CustomEntriesKey.Length..];
            keep.Add(e.Id);
            var i = s.CustomEntries.FindIndex(c => c.Id == e.Id);
            if (i >= 0) s.CustomEntries[i] = e; else s.CustomEntries.Add(e);

            var cfg = s.FindEntry(e.Key);
            if (cfg == null) { cfg = new EntryConfig { Path = e.Key }; s.Entries.Add(cfg); }
            cfg.Category = Map(GroupManager.Normalize(shared.Group));
            cfg.DisplayName = shared.DisplayName;
            cfg.Notes = shared.Notes;
            cfg.Tags = shared.Tags.ToList();
            cfg.Profiles = shared.Profiles.Select(p => p.Clone()).ToList();
        }
        foreach (var gone in EntriesOf(s, sub).Where(c => !keep.Contains(c.Id)).ToList())
        {
            s.CustomEntries.Remove(gone);
            s.Entries.RemoveAll(x => x.Path.Equals(gone.Key, StringComparison.OrdinalIgnoreCase));
        }
        s.CategoryOrder = GroupManager.All(s).ToList();
        sub.LastAppliedUtc = nowUtc ?? DateTime.UtcNow;
        sub.PendingSummary = string.Empty;
        return diff;
    }

    /// <summary>Abo beenden: Katalogeinträge entfernen (die Gruppe bleibt, falls eigene Einträge darin liegen).</summary>
    public static void Unsubscribe(AppSettings s, CatalogSubscription sub)
    {
        foreach (var e in EntriesOf(s, sub).ToList())
        {
            s.CustomEntries.Remove(e);
            s.Entries.RemoveAll(x => x.Path.Equals(e.Key, StringComparison.OrdinalIgnoreCase));
        }
        s.Catalogs.RemoveAll(c => c.Id == sub.Id);
    }
}
