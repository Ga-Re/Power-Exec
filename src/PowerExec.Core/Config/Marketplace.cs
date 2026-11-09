using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerExec.Core.Config;

/// <summary>Was ein Marktplatz-Element enthält.</summary>
public enum MarketplaceItemType
{
    /// <summary>Ein oder mehrere eigene Einträge (Datei im Format von <see cref="EntryShare"/>).</summary>
    Entry,
    /// <summary>Bündel: Gruppe mit Einträgen, oft mit Variablen (gleiches Format wie <see cref="Entry"/>).</summary>
    Package,
    /// <summary>Designvorlage (<see cref="DesignPreset"/> als JSON).</summary>
    Design,
    /// <summary>PowerShell-Script (.ps1), wird in den Marktplatz-Ordner geladen.</summary>
    Script
}

/// <summary>Variable, die ein Element braucht (z. B. die Adresse des Matrix42-Servers).</summary>
public sealed class MarketplaceVariable
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Example { get; set; } = string.Empty;
}

/// <summary>Ein Element im Index eines Marktplatzes.</summary>
public sealed class MarketplaceItem
{
    public string Id { get; set; } = string.Empty;
    public MarketplaceItemType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Rubrik für die Anzeige, z. B. „Links“, „Security“.</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>Wird bei jeder Änderung erhöht – installierte Elemente zeigen dann „Update verfügbar“.</summary>
    public string Version { get; set; } = "1";
    /// <summary>Pfad der Datei relativ zur Index-Datei (auch ../ erlaubt).</summary>
    public string File { get; set; } = string.Empty;
    public List<MarketplaceVariable> Variables { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

/// <summary>Index-Datei eines Marktplatzes (index.json).</summary>
public sealed class MarketplaceIndex
{
    public string Name { get; set; } = "Marktplatz";
    public string Description { get; set; } = string.Empty;
    public List<MarketplaceItem> Items { get; set; } = new();
}

/// <summary>Eine zusätzliche Marktplatz-Quelle (Repository-URL).</summary>
public sealed class MarketplaceSource
{
    public string Url { get; set; } = string.Empty;
    /// <summary>Anzeigename (aus der Index-Datei, sonst aus der URL).</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Einmal bestätigt: Inhalte dieser Quelle dürfen installiert werden.</summary>
    public bool Trusted { get; set; }
    public DateTime AddedUtc { get; set; }
}

/// <summary>Installiertes Element: Herkunft und Stand (für „Update verfügbar“).</summary>
public sealed class MarketplaceInstall
{
    public string Source { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime InstalledUtc { get; set; }
    /// <summary>Kennungen der angelegten eigenen Einträge.</summary>
    public List<string> EntryIds { get; set; } = new();
    public string DesignKey { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
}

/// <summary>Quellen, Index, Installation und Update-Erkennung des Marktplatzes.</summary>
public static class Marketplace
{
    /// <summary>Standardquelle: Ordner „marketplace“ im Power-Exec-Repository.</summary>
    public const string DefaultSource = "https://github.com/Ga-Re/Power-Exec/tree/main/marketplace";

    /// <summary>Gruppe für Einträge ohne eigene Gruppe.</summary>
    public const string DefaultGroup = "Marktplatz";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Adresse der Index-Datei zu einer Repo-URL: GitHub (github.com/owner/repo[/tree/branch/pfad]), GitLab
    /// (…/-/tree/branch/pfad) oder direkt eine index.json. Ohne Branch wird „main“ angenommen.
    /// </summary>
    public static Uri IndexUri(string source)
    {
        var s = source.Trim().TrimEnd('/');
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new ArgumentException("Bitte die Adresse des Repositorys angeben, z. B. https://github.com/firma/power-exec-katalog.");
        if (uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return uri;
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
        {
            var (branch, path) = parts.Length >= 4 && parts[2] is "tree" or "blob" ? (parts[3], string.Join('/', parts.Skip(4))) : ("main", string.Empty);
            return new Uri($"https://raw.githubusercontent.com/{parts[0]}/{Repo(parts[1])}/{branch}/{Join(path, "index.json")}");
        }
        var dash = Array.IndexOf(parts, "-");
        if (dash >= 2 && parts.Length > dash + 2 && parts[dash + 1] is "tree" or "blob")
        {
            var repoPath = string.Join('/', parts.Take(dash));
            var path = string.Join('/', parts.Skip(dash + 3));
            return new Uri($"{uri.Scheme}://{uri.Authority}/{repoPath}/-/raw/{parts[dash + 2]}/{Join(path, "index.json")}");
        }
        if (parts.Length >= 2 && !uri.Host.Contains("github", StringComparison.OrdinalIgnoreCase))
            return new Uri($"{uri.Scheme}://{uri.Authority}/{string.Join('/', parts.Select(Repo))}/-/raw/main/index.json"); // GitLab ohne Branch
        throw new ArgumentException("Diese Adresse erkennt Power-Exec nicht als Repository (GitHub oder GitLab).");

        static string Repo(string p) => p.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p;
        static string Join(string a, string b) => a.Length == 0 ? b : a.TrimEnd('/') + "/" + b;
    }

    /// <summary>Adresse einer Datei relativ zur Index-Datei.</summary>
    public static Uri FileUri(Uri index, string file) => new(index, file.Replace('\\', '/'));

    public static MarketplaceIndex ParseIndex(string json)
    {
        var index = JsonSerializer.Deserialize<MarketplaceIndex>(json, Json) ?? throw new InvalidDataException("Leere Index-Datei.");
        index.Items = index.Items.Where(i => i.Id.Trim().Length > 0 && i.File.Trim().Length > 0).ToList();
        return index;
    }

    /// <summary>Alle Quellen: die mitgelieferte zuerst (immer vertrauenswürdig), dann die eigenen.</summary>
    public static IReadOnlyList<MarketplaceSource> Sources(AppSettings s) =>
        new[] { new MarketplaceSource { Url = DefaultSource, Name = "Power-Exec", Trusted = true } }
            .Concat(s.MarketplaceSources.Where(x => !SameSource(x.Url, DefaultSource))).ToList();

    public static bool IsDefault(string source) => SameSource(source, DefaultSource);

    public static bool IsTrusted(AppSettings s, string source) =>
        IsDefault(source) || s.MarketplaceSources.Any(x => SameSource(x.Url, source) && x.Trusted);

    /// <summary>Quelle hinzufügen (URL wird geprüft); liefert eine Fehlermeldung oder null.</summary>
    public static string? ValidateSource(AppSettings s, string url)
    {
        try { IndexUri(url); }
        catch (ArgumentException ex) { return ex.Message; }
        if (Sources(s).Any(x => SameSource(x.Url, url))) return "Diese Quelle ist schon eingetragen.";
        return null;
    }

    /// <summary>Name aus der URL (owner/repo), falls die Index-Datei keinen nennt.</summary>
    public static string NameFromUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return url;
        var parts = u.AbsolutePath.Trim('/').Split('/');
        return parts.Length >= 2 ? $"{parts[0]}/{parts[1].Replace(".git", "")}" : u.Host;
    }

    public static MarketplaceInstall? FindInstall(AppSettings s, string source, string itemId) =>
        s.MarketplaceInstalls.FirstOrDefault(i => SameSource(i.Source, source) && i.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));

    public static bool SameSource(string a, string b) => a.Trim().TrimEnd('/').Equals(b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Installiert und (Version unterscheidet sich) neu im Marktplatz?</summary>
    public static bool HasUpdate(AppSettings s, string source, MarketplaceItem item) =>
        FindInstall(s, source, item.Id) is { } i && !i.Version.Equals(item.Version, StringComparison.OrdinalIgnoreCase);

    /// <summary>Variablen des Elements, die es hier noch nicht gibt.</summary>
    public static IReadOnlyList<MarketplaceVariable> MissingVariables(AppSettings s, MarketplaceItem item) =>
        item.Variables.Where(v => Variables.Normalize(v.Name).Length > 0 && !Variables.Exists(s, v.Name)).ToList();

    /// <summary>Kurzbeschreibung des Inhalts für die Vorschau vor der Installation.</summary>
    public static string Preview(MarketplaceItem item, string payload)
    {
        const int max = 1800;
        string text;
        switch (item.Type)
        {
            case MarketplaceItemType.Entry or MarketplaceItemType.Package:
                var file = EntryShare.Parse(payload);
                text = string.Join("\n\n", file.Entries.Select(e =>
                {
                    var c = e.Entry;
                    var what = c.Kind == CustomEntryKind.Snippet ? c.Code : (c.Target + " " + c.Arguments).Trim();
                    return $"• {(e.DisplayName.Length > 0 ? e.DisplayName : c.Name)} ({c.Kind})" + (what.Length > 0 ? "\n  " + what.Replace("\n", "\n  ") : string.Empty);
                }));
                break;
            case MarketplaceItemType.Design:
                var d = ParseDesign(payload, item);
                text = $"Design „{d.Name}“: {d.Theme}, Panelfarbe {(d.PanelColor.Length > 0 ? d.PanelColor : "des Designs")}, {PanelBackdrops.Label(d.Backdrop)}, Deckkraft {d.Opacity} %"
                       + (d.AccentColor.Length > 0 ? $", Akzent {d.AccentColor}" : string.Empty);
                break;
            default:
                text = payload;
                break;
        }
        return text.Length > max ? text[..max] + "\n…" : text;
    }

    public static DesignPreset ParseDesign(string json, MarketplaceItem item)
    {
        var d = JsonSerializer.Deserialize<DesignPreset>(json, Json) ?? throw new InvalidDataException("Leere Design-Datei.");
        return d with { Key = "market:" + item.Id, Name = d.Name.Length > 0 ? d.Name : item.Name, IsCustom = true };
    }

    /// <summary>
    /// Element installieren bzw. aktualisieren. Einträge landen in ihrer Gruppe (sonst „Marktplatz“), Designs bei den
    /// eigenen Designs, Scripte im Ordner <paramref name="scriptFolder"/>, der als Gruppe eingebunden wird.
    /// </summary>
    /// <returns>Kurze Rückmeldung für die Oberfläche.</returns>
    public static string Install(AppSettings s, string source, MarketplaceItem item, string payload, string scriptFolder)
    {
        var record = FindInstall(s, source, item.Id);
        if (record == null)
        {
            record = new MarketplaceInstall { Source = source.Trim(), ItemId = item.Id };
            s.MarketplaceInstalls.Add(record);
        }
        string message;
        switch (item.Type)
        {
            case MarketplaceItemType.Entry or MarketplaceItemType.Package:
            {
                var file = EntryShare.Parse(payload);
                // Gleiche Kennungen wie beim letzten Mal: ein Update ersetzt die Einträge statt sie zu verdoppeln
                foreach (var e in file.Entries)
                    if (e.Group.Trim().Length == 0 && file.Groups.Count == 0) e.Group = DefaultGroup;
                if (file.Entries.All(e => e.Group.Trim().Length > 0) && file.Groups.Count == 0)
                    file.Groups = file.Entries.Select(e => e.Group).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
                var (added, updated) = EntryShare.Import(s, file);
                record.EntryIds = file.Entries.Select(e => s.CustomEntries.FirstOrDefault(c => c.Id == e.Entry.Id)?.Id
                                                           ?? s.CustomEntries.LastOrDefault(c => c.Kind == e.Entry.Kind && c.Target == e.Entry.Target && c.Code == e.Entry.Code)?.Id)
                                              .OfType<string>().Distinct().ToList();
                message = $"„{item.Name}“: {Count(added, "Eintrag", "Einträge")} neu" + (updated > 0 ? $", {updated} aktualisiert." : ".");
                break;
            }
            case MarketplaceItemType.Design:
            {
                var d = ParseDesign(payload, item);
                s.CustomDesigns.RemoveAll(x => x.Key == d.Key);
                s.CustomDesigns.Add(d);
                record.DesignKey = d.Key;
                message = $"Design „{d.Name}“ steht unter Darstellung › Designvorlagen bereit.";
                break;
            }
            default:
            {
                Directory.CreateDirectory(scriptFolder);
                var name = Path.GetFileName(item.File.Replace('\\', '/'));
                if (!name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) name = SafeFileName(item.Name) + ".ps1";
                var path = Path.Combine(scriptFolder, name);
                File.WriteAllText(path, payload, new System.Text.UTF8Encoding(true));
                record.ScriptPath = path;
                if (!s.Folders.Any(f => f.Path.Equals(scriptFolder, StringComparison.OrdinalIgnoreCase)))
                    SmartGroups.Add(s, scriptFolder, "Marktplatz-Scripte");
                message = $"Script „{item.Name}“ liegt in {scriptFolder} (Gruppe „Marktplatz-Scripte“).";
                break;
            }
        }
        record.Version = item.Version;
        record.InstalledUtc = DateTime.UtcNow;
        return message;
    }

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c)).Trim('-');
}
