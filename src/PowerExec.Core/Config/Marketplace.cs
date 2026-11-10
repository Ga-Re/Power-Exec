using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PowerExec.Core.Config;

/// <summary>Was eine Datei im Marktplatz enthält (aus der Dateiendung).</summary>
public enum MarketplaceItemType
{
    /// <summary><c>*.pxentry.json</c>: ein eigener Eintrag (Link, Snippet, Befehl …).</summary>
    Entry,
    /// <summary><c>*.ps1</c>: PowerShell-Script.</summary>
    Script,
    /// <summary><c>*.design.json</c>: Designvorlage.</summary>
    Design
}

/// <summary>Variable, die ein Eintrag braucht (z. B. die Adresse des Matrix42-Servers).</summary>
public sealed class MarketplaceVariable
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Example { get; set; } = string.Empty;
}

/// <summary>Inhalt einer <c>*.pxentry.json</c>: der Eintrag und die Variablen, die er braucht.</summary>
public sealed class MarketplaceEntryFile
{
    public CustomEntry Entry { get; set; } = new();
    public List<MarketplaceVariable> Variables { get; set; } = new();
}

/// <summary>Eine Datei im Repository einer Quelle (Pfad relativ zum Marktplatz-Ordner, Git-Stand für Updates).</summary>
public sealed record MarketplaceFile(string Path, string Sha, MarketplaceItemType Type)
{
    /// <summary>Ordner der Datei („Links/Security“), leer = oberste Ebene.</summary>
    public string Folder => Path.Contains('/') ? Path[..Path.LastIndexOf('/')] : string.Empty;
    public string FileName => Path[(Path.LastIndexOf('/') + 1)..];

    /// <summary>Name aus dem Dateinamen (ohne Endung, Bindestriche als Leerzeichen) – bis die Datei gelesen ist.</summary>
    public string DefaultName
    {
        get
        {
            var n = FileName;
            foreach (var ext in new[] { ".pxentry.json", ".design.json", ".ps1" })
                if (n.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { n = n[..^ext.Length]; break; }
            return n.Replace('_', ' ');
        }
    }
}

/// <summary>Was die Oberfläche über eine Datei zeigt (nach dem Lesen der Datei).</summary>
public sealed record MarketplaceDetails(string Name, string Description, IReadOnlyList<MarketplaceVariable> Variables, string Preview);

/// <summary>Eine zusätzliche Marktplatz-Quelle (Repository-URL).</summary>
public sealed class MarketplaceSource
{
    public string Url { get; set; } = string.Empty;
    /// <summary>Anzeigename (aus der URL).</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Einmal bestätigt: Inhalte dieser Quelle dürfen installiert werden.</summary>
    public bool Trusted { get; set; }
    public DateTime AddedUtc { get; set; }
}

/// <summary>Installierte Datei: Herkunft, Git-Stand (für „Update verfügbar“) und was dafür angelegt wurde.</summary>
public sealed class MarketplaceInstall
{
    public string Source { get; set; } = string.Empty;
    /// <summary>Pfad der Datei in der Quelle.</summary>
    public string ItemId { get; set; } = string.Empty;
    /// <summary>Git-Stand (Blob-SHA) beim Installieren.</summary>
    public string Version { get; set; } = string.Empty;
    public DateTime InstalledUtc { get; set; }
    /// <summary>Kennungen der angelegten eigenen Einträge.</summary>
    public List<string> EntryIds { get; set; } = new();
    public string DesignKey { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    /// <summary>Stand des Eintrags beim Installieren (zum Erkennen eigener Änderungen vor dem Deinstallieren).</summary>
    public string Fingerprint { get; set; } = string.Empty;
}

/// <summary>Wo eine Quelle liegt: GitHub oder GitLab, Repository, Branch und Unterordner.</summary>
public sealed record MarketplaceRepo(bool IsGitHub, string Authority, string Project, string Branch, string Folder)
{
    /// <summary>Liste aller Dateien (rekursiv) über die Schnittstelle des Anbieters.</summary>
    public Uri TreeUri(int page = 1) => IsGitHub
        ? new Uri($"https://api.github.com/repos/{Project}/git/trees/{Uri.EscapeDataString(Branch)}?recursive=1")
        : new Uri($"https://{Authority}/api/v4/projects/{Uri.EscapeDataString(Project)}/repository/tree?ref={Uri.EscapeDataString(Branch)}&recursive=true&per_page=100&page={page}"
                  + (Folder.Length > 0 ? "&path=" + Uri.EscapeDataString(Folder) : string.Empty));

    /// <summary>Rohe Datei (nicht vom Abruflimit der Schnittstelle betroffen).</summary>
    public Uri RawUri(string relativePath)
    {
        var full = string.Join('/', (Folder.Length > 0 ? Folder + "/" + relativePath : relativePath).Split('/').Select(Uri.EscapeDataString));
        return IsGitHub
            ? new Uri($"https://raw.githubusercontent.com/{Project}/{Uri.EscapeDataString(Branch)}/{full}")
            : new Uri($"https://{Authority}/{Project}/-/raw/{Uri.EscapeDataString(Branch)}/{full}");
    }
}

/// <summary>Marktplatz: Quellen (Git-Repositorys), Ordner lesen, installieren, deinstallieren, Updates erkennen.</summary>
public static class Marketplace
{
    /// <summary>Standardquelle: Ordner „marketplace“ im Power-Exec-Repository.</summary>
    public const string DefaultSource = "https://github.com/Ga-Re/Power-Exec/tree/main/marketplace";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // ---- Quellen -----------------------------------------------------------------

    /// <summary>
    /// Repository-URL zerlegen: GitHub (github.com/owner/repo[/tree/branch/ordner]) oder GitLab
    /// (host/gruppe/projekt[/-/tree/branch/ordner]). Ohne Branch gilt „main“.
    /// </summary>
    public static MarketplaceRepo ParseSource(string source)
    {
        var s = source.Trim().TrimEnd('/');
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new ArgumentException("Bitte die Adresse des Repositorys angeben, z. B. https://github.com/firma/power-exec-katalog.");
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        static string Repo(string p) => p.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p;
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 2) throw new ArgumentException("GitHub-Adresse ohne Repository – z. B. https://github.com/firma/katalog.");
            var (branch, folder) = parts.Length >= 4 && parts[2] is "tree" or "blob" ? (parts[3], string.Join('/', parts.Skip(4))) : ("main", string.Empty);
            return new MarketplaceRepo(true, "github.com", $"{parts[0]}/{Repo(parts[1])}", branch, folder);
        }
        var dash = Array.IndexOf(parts, "-");
        if (dash >= 2 && parts.Length > dash + 2 && parts[dash + 1] == "tree")
            return new MarketplaceRepo(false, uri.Authority, string.Join('/', parts.Take(dash)), parts[dash + 2], string.Join('/', parts.Skip(dash + 3)));
        if (dash < 0 && parts.Length >= 2)
            return new MarketplaceRepo(false, uri.Authority, string.Join('/', parts.Select(Repo)), "main", string.Empty);
        throw new ArgumentException("Diese Adresse erkennt Power-Exec nicht als Repository (GitHub oder GitLab).");
    }

    /// <summary>Alle Quellen: die mitgelieferte zuerst (immer vertrauenswürdig), dann die eigenen.</summary>
    public static IReadOnlyList<MarketplaceSource> Sources(AppSettings s) =>
        new[] { new MarketplaceSource { Url = DefaultSource, Name = "Power-Exec", Trusted = true } }
            .Concat(s.MarketplaceSources.Where(x => !SameSource(x.Url, DefaultSource))).ToList();

    public static bool IsDefault(string source) => SameSource(source, DefaultSource);

    public static bool SameSource(string a, string b) => a.Trim().TrimEnd('/').Equals(b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public static bool IsTrusted(AppSettings s, string source) =>
        IsDefault(source) || s.MarketplaceSources.Any(x => SameSource(x.Url, source) && x.Trusted);

    /// <summary>Fehlermeldung für eine neue Quelle oder null.</summary>
    public static string? ValidateSource(AppSettings s, string url)
    {
        try { ParseSource(url); }
        catch (ArgumentException ex) { return ex.Message; }
        if (Sources(s).Any(x => SameSource(x.Url, url))) return "Diese Quelle ist schon eingetragen.";
        return null;
    }

    /// <summary>Name aus der URL (owner/repo).</summary>
    public static string NameFromUrl(string url)
    {
        try
        {
            var r = ParseSource(url);
            return r.Folder.Length > 0 ? $"{r.Project} › {r.Folder}" : r.Project;
        }
        catch (ArgumentException) { return url; }
    }

    // ---- Ordner lesen ------------------------------------------------------------

    public static MarketplaceItemType? TypeOf(string path) =>
        path.EndsWith(".pxentry.json", StringComparison.OrdinalIgnoreCase) ? MarketplaceItemType.Entry
        : path.EndsWith(".design.json", StringComparison.OrdinalIgnoreCase) ? MarketplaceItemType.Design
        : path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ? MarketplaceItemType.Script
        : null;

    /// <summary>
    /// Dateiliste aus der Antwort der Schnittstelle (GitHub: <c>{"tree":[…]}</c>, GitLab: <c>[…]</c>): nur Einträge, Scripte und
    /// Designs unterhalb des Marktplatz-Ordners, Pfade relativ dazu.
    /// </summary>
    public static IReadOnlyList<MarketplaceFile> ParseTree(string json, MarketplaceRepo repo)
    {
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
            : doc.RootElement.TryGetProperty("tree", out var t) ? t
            : throw new InvalidDataException("Unerwartete Antwort des Repositorys.");
        var prefix = repo.Folder.Length > 0 ? repo.Folder.TrimEnd('/') + "/" : string.Empty;
        var result = new List<MarketplaceFile>();
        foreach (var e in items.EnumerateArray())
        {
            if (!e.TryGetProperty("type", out var type) || type.GetString() != "blob") continue;
            var path = e.GetProperty("path").GetString() ?? string.Empty;
            if (prefix.Length > 0 && !path.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var relative = path[prefix.Length..];
            if (TypeOf(relative) is not { } kind) continue;
            var sha = e.TryGetProperty("sha", out var sh) ? sh.GetString() : e.TryGetProperty("id", out var id) ? id.GetString() : null;
            result.Add(new MarketplaceFile(relative, sha ?? string.Empty, kind));
        }
        return result.OrderBy(f => f.Folder, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.FileName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Name, Beschreibung, Variablen und Vorschau einer Datei.</summary>
    public static MarketplaceDetails Describe(MarketplaceFile file, string payload)
    {
        switch (file.Type)
        {
            case MarketplaceItemType.Entry:
            {
                var f = ParseEntry(payload);
                var e = f.Entry;
                var what = e.Kind == CustomEntryKind.Snippet ? e.Code : (e.Target + " " + e.Arguments).Trim();
                return new MarketplaceDetails(e.Name.Length > 0 ? e.Name : file.DefaultName, e.Description, f.Variables, Trim(what));
            }
            case MarketplaceItemType.Design:
            {
                var d = ParseDesign(payload, file);
                var text = $"{d.Theme}, Panelfarbe {(d.PanelColor.Length > 0 ? d.PanelColor : "des Designs")}, {PanelBackdrops.Label(d.Backdrop)}, Deckkraft {d.Opacity} %"
                           + (d.AccentColor.Length > 0 ? $", Akzent {d.AccentColor}" : string.Empty);
                return new MarketplaceDetails(d.Name, d.Description, Array.Empty<MarketplaceVariable>(), text);
            }
            default:
            {
                var synopsis = Regex.Match(payload, @"\.SYNOPSIS\s*\r?\n\s*(.+)");
                return new MarketplaceDetails(file.DefaultName, synopsis.Success ? synopsis.Groups[1].Value.Trim() : string.Empty,
                    Array.Empty<MarketplaceVariable>(), Trim(payload));
            }
        }
    }

    private static string Trim(string text) => text.Length > 1500 ? text[..1500] + "\n…" : text;

    public static MarketplaceEntryFile ParseEntry(string json)
    {
        var f = JsonSerializer.Deserialize<MarketplaceEntryFile>(json, Json) ?? throw new InvalidDataException("Leere Eintragsdatei.");
        if (f.Entry == null) throw new InvalidDataException("Die Datei enthält keinen Eintrag.");
        return f;
    }

    public static DesignPreset ParseDesign(string json, MarketplaceFile file)
    {
        var d = JsonSerializer.Deserialize<DesignPreset>(json, Json) ?? throw new InvalidDataException("Leere Design-Datei.");
        return d with { Key = "market:" + StableId(file.Path), Name = d.Name.Length > 0 ? d.Name : file.DefaultName, IsCustom = true };
    }

    // ---- Installieren, deinstallieren, Updates -----------------------------------------

    public static MarketplaceInstall? FindInstall(AppSettings s, string source, string path) =>
        s.MarketplaceInstalls.FirstOrDefault(i => SameSource(i.Source, source) && i.ItemId.Equals(path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Installiert, aber im Repository geändert (anderer Git-Stand)?</summary>
    public static bool HasUpdate(AppSettings s, string source, MarketplaceFile file) =>
        FindInstall(s, source, file.Path) is { } i && file.Sha.Length > 0 && !i.Version.Equals(file.Sha, StringComparison.OrdinalIgnoreCase);

    /// <summary>Variablen, die es hier noch nicht gibt.</summary>
    public static IReadOnlyList<MarketplaceVariable> MissingVariables(AppSettings s, IEnumerable<MarketplaceVariable> needed) =>
        needed.Where(v => Variables.Normalize(v.Name).Length > 0 && !Variables.Exists(s, v.Name))
              .GroupBy(v => Variables.Normalize(v.Name), StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).ToList();

    /// <summary>Gleiche Datei = gleiche Kennung: ein Update ersetzt den Eintrag statt ihn zu verdoppeln.</summary>
    public static string StableId(string key) =>
        "mk" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key.ToLowerInvariant())))[..14].ToLowerInvariant();

    private static string Fingerprint(CustomEntry e) => JsonSerializer.Serialize(e, Json);

    /// <summary>
    /// Datei installieren bzw. aktualisieren. Einträge und Scripte landen in den eigenen Einträgen der Gruppe
    /// <paramref name="group"/> (Scripte als Datei im Ordner <paramref name="scriptFolder"/>), Designs bei den Designvorlagen.
    /// </summary>
    public static MarketplaceInstall Install(AppSettings s, string source, MarketplaceFile file, string payload, string group, string scriptFolder)
    {
        var record = FindInstall(s, source, file.Path);
        if (record == null)
        {
            record = new MarketplaceInstall { Source = source.Trim(), ItemId = file.Path };
            s.MarketplaceInstalls.Add(record);
        }
        group = GroupManager.Normalize(group);
        switch (file.Type)
        {
            case MarketplaceItemType.Entry:
            {
                var entry = ParseEntry(payload).Entry.Clone();
                entry.Id = StableId(source.Trim().TrimEnd('/') + "|" + file.Path);
                AddOrReplace(s, entry, group);
                record.EntryIds = new List<string> { entry.Id };
                record.Fingerprint = Fingerprint(entry);
                break;
            }
            case MarketplaceItemType.Design:
            {
                var d = ParseDesign(payload, file);
                s.CustomDesigns.RemoveAll(x => x.Key == d.Key);
                s.CustomDesigns.Add(d);
                record.DesignKey = d.Key;
                break;
            }
            default:
            {
                var folder = file.Folder.Length > 0 ? Path.Combine(new[] { scriptFolder }.Concat(file.Folder.Split('/').Select(SafeName)).ToArray()) : scriptFolder;
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, SafeName(file.FileName));
                File.WriteAllText(path, payload, new UTF8Encoding(true));
                var entry = new CustomEntry
                {
                    Id = StableId(source.Trim().TrimEnd('/') + "|" + file.Path), Kind = CustomEntryKind.File, Target = path, Name = file.DefaultName,
                    Description = Describe(file, payload).Description
                };
                AddOrReplace(s, entry, group);
                record.EntryIds = new List<string> { entry.Id };
                record.ScriptPath = path;
                record.Fingerprint = Fingerprint(entry);
                break;
            }
        }
        record.Version = file.Sha;
        record.InstalledUtc = DateTime.UtcNow;
        return record;
    }

    private static void AddOrReplace(AppSettings s, CustomEntry entry, string group)
    {
        var index = s.CustomEntries.FindIndex(c => c.Id == entry.Id);
        if (index >= 0) s.CustomEntries[index] = entry;
        else s.CustomEntries.Add(entry);
        if (group.Length > 0 && !GroupManager.Exists(s, group)) s.CategoryOrder.Add(group);
        var cfg = s.FindEntry(entry.Key);
        if (cfg == null)
        {
            cfg = new EntryConfig { Path = entry.Key };
            s.Entries.Add(cfg);
        }
        if (group.Length > 0) cfg.Category = group;
        s.CategoryOrder = GroupManager.All(s).ToList();
    }

    /// <summary>Hat der Benutzer einen installierten Eintrag seither geändert?</summary>
    public static bool IsModified(AppSettings s, MarketplaceInstall record) =>
        record.Fingerprint.Length > 0 && record.EntryIds.Any(id => s.CustomEntries.FirstOrDefault(c => c.Id == id) is { } e && Fingerprint(e) != record.Fingerprint);

    /// <summary>Gruppe, in der die installierten Einträge stehen (für Updates: dieselbe Gruppe wieder nehmen).</summary>
    public static string? InstalledGroup(AppSettings s, MarketplaceInstall record) =>
        record.EntryIds.Select(id => s.CustomEntries.FirstOrDefault(c => c.Id == id)).OfType<CustomEntry>()
            .Select(e => s.FindEntry(e.Key)?.Category).FirstOrDefault(g => !string.IsNullOrEmpty(g));

    /// <summary>Deinstallieren: angelegte Einträge, Design bzw. Script-Datei entfernen.</summary>
    public static void Uninstall(AppSettings s, MarketplaceInstall record)
    {
        foreach (var id in record.EntryIds)
            if (s.CustomEntries.FirstOrDefault(c => c.Id == id) is { } e)
            {
                s.CustomEntries.Remove(e);
                s.Entries.RemoveAll(x => x.Path.Equals(e.Key, StringComparison.OrdinalIgnoreCase));
                s.Favorites.RemoveAll(f => f.Equals(e.Key, StringComparison.OrdinalIgnoreCase));
            }
        if (record.DesignKey.Length > 0) s.CustomDesigns.RemoveAll(d => d.Key == record.DesignKey);
        if (record.ScriptPath.Length > 0)
            try { if (File.Exists(record.ScriptPath)) File.Delete(record.ScriptPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        s.MarketplaceInstalls.Remove(record);
    }

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
}
