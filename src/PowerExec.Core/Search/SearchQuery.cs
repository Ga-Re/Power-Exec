using PowerExec.Core.Localization;
using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Search;

/// <summary>Art eines Eintrags aus Sicht der Suche (Dateitypen und Arten eigener Einträge).</summary>
public enum SearchKind { PowerShell, Batch, Executable, Shortcut, SnapIn, Web, Folder, Rdp, Ssh, Snippet, Command, Tool, File }

public enum SearchTokenKind { Text, Tag, Type, Group, Favorite, Admin, Remote, Problem, Invalid }

/// <summary>Ein erkannter Teil der Suche, z. B. „typ:ps1“ (für die Chips unter dem Suchfeld).</summary>
public sealed record SearchToken(SearchTokenKind Kind, string Raw, int Start, string Label);

/// <summary>
/// Zerlegt die Eingabe im Suchfeld in Suchbegriffe und Filter:
/// <c>#schlagwort</c>, <c>typ:ps1</c>, <c>gruppe:"Kunde A"</c>, <c>ist:fav</c>, <c>ist:admin</c>, <c>ist:remote</c>, <c>ist:problem</c>.
/// Englische Schlüssel (<c>type:</c>, <c>group:</c>, <c>is:</c>) gehen auch.
/// </summary>
public sealed class SearchQuery
{
    public string Raw { get; private init; } = string.Empty;
    public List<string> Terms { get; } = new();
    public List<string> Tags { get; } = new();
    public HashSet<SearchKind> Kinds { get; } = new();
    public List<string> Groups { get; } = new();
    public bool Favorites { get; private set; }
    public bool Admin { get; private set; }
    public bool Remote { get; private set; }
    public bool Problems { get; private set; }
    public List<SearchToken> Tokens { get; } = new();

    public bool HasTerms => Terms.Count > 0;
    public bool HasFilters => Tags.Count > 0 || Kinds.Count > 0 || Groups.Count > 0 || Favorites || Admin || Remote || Problems;
    public bool IsEmpty => !HasTerms && !HasFilters;

    /// <summary>Erkannte Operatoren (ohne normale Suchbegriffe).</summary>
    public IEnumerable<SearchToken> Operators => Tokens.Where(t => t.Kind != SearchTokenKind.Text);

    public static string Help => L.T("Suche: Name, Synopsis, Beschreibung – Tippfehler und Anfangsbuchstaben („gds“) gehen auch.\nFilter: #schlagwort · typ:ps1|exe|web|rdp|ssh|ordner|snippet … · gruppe:Name · ist:fav · ist:admin · ist:remote · ist:problem");

    private static readonly (string[] Aliases, SearchKind[] Kinds, string Label)[] TypeAliases =
    {
        (new[] { "ps1", "ps", "powershell" }, new[] { SearchKind.PowerShell }, "PowerShell"),
        (new[] { "script", "scripte", "scripts", "skript", "skripte" }, new[] { SearchKind.PowerShell, SearchKind.Batch }, L.T("Scripte")),
        (new[] { "cmd", "bat", "batch" }, new[] { SearchKind.Batch }, "Batch"),
        (new[] { "exe" }, new[] { SearchKind.Executable }, "EXE"),
        (new[] { "programm", "programme", "program", "programs", "app" }, new[] { SearchKind.Executable, SearchKind.Shortcut, SearchKind.SnapIn }, L.T("Programme")),
        (new[] { "lnk", "verknüpfung", "shortcut" }, new[] { SearchKind.Shortcut }, L.T("Verknüpfung")),
        (new[] { "msc", "snapin", "snap-in", "konsole" }, new[] { SearchKind.SnapIn }, "Snap-in"),
        (new[] { "web", "url", "webseite", "link", "website" }, new[] { SearchKind.Web }, L.T("Webseite")),
        (new[] { "ordner", "folder", "freigabe", "share" }, new[] { SearchKind.Folder }, L.T("Ordner")),
        (new[] { "rdp", "remotedesktop" }, new[] { SearchKind.Rdp }, L.T("Remotedesktop")),
        (new[] { "ssh" }, new[] { SearchKind.Ssh }, "SSH"),
        (new[] { "snippet", "snippets", "text" }, new[] { SearchKind.Snippet }, "Snippet"),
        (new[] { "befehl", "command" }, new[] { SearchKind.Command }, L.T("Befehl")),
        (new[] { "werkzeug", "tool", "tools" }, new[] { SearchKind.Tool }, L.T("Werkzeug")),
        (new[] { "datei", "file" }, new[] { SearchKind.File }, L.T("Datei")),
    };

    private static readonly (string[] Aliases, SearchTokenKind Kind, string Label)[] FlagAliases =
    {
        (new[] { "fav", "favorit", "favoriten", "favorite", "favorites", "star" }, SearchTokenKind.Favorite, L.T("Favoriten")),
        (new[] { "admin", "administrator", "elevated" }, SearchTokenKind.Admin, L.T("Benötigt Admin")),
        (new[] { "remote" }, SearchTokenKind.Remote, L.T("Remote-fähig")),
        (new[] { "problem", "probleme", "fehler", "defekt", "error", "broken" }, SearchTokenKind.Problem, L.T("Mit Problemen")),
    };

    private static readonly string[] TypeKeys = { "typ", "type", "t", "art" };
    private static readonly string[] GroupKeys = { "gruppe", "group", "g", "in" };
    private static readonly string[] FlagKeys = { "ist", "is" };

    public static SearchQuery Parse(string? text)
    {
        var q = new SearchQuery { Raw = text ?? string.Empty };
        foreach (var (word, start) in Split(q.Raw))
        {
            if (word.Length > 1 && word[0] == '#')
            {
                var tag = Unquote(word[1..]);
                q.Tags.Add(tag);
                q.Tokens.Add(new SearchToken(SearchTokenKind.Tag, word, start, "#" + tag));
                continue;
            }
            var colon = word.IndexOf(':');
            if (colon > 0)
            {
                var key = word[..colon].ToLowerInvariant();
                var value = Unquote(word[(colon + 1)..]);
                if (TypeKeys.Contains(key)) { q.AddType(word, start, value); continue; }
                if (GroupKeys.Contains(key))
                {
                    if (value.Length == 0) q.Tokens.Add(new SearchToken(SearchTokenKind.Invalid, word, start, L.T("Gruppe: …")));
                    else { q.Groups.Add(value); q.Tokens.Add(new SearchToken(SearchTokenKind.Group, word, start, L.T("Gruppe: ") + value)); }
                    continue;
                }
                if (FlagKeys.Contains(key)) { q.AddFlag(word, start, value); continue; }
                // Unbekannter Schlüssel (z. B. „C:\Tools“ oder „http:“): normaler Suchbegriff
            }
            var term = Unquote(word);
            if (term.Length == 0) continue;
            q.Terms.Add(term);
            q.Tokens.Add(new SearchToken(SearchTokenKind.Text, word, start, term));
        }
        return q;
    }

    private void AddType(string raw, int start, string value)
    {
        var v = value.ToLowerInvariant();
        // Exakter Treffer, sonst Präfix (während des Tippens: „typ:po“ → PowerShell)
        var hits = TypeAliases.Where(a => a.Aliases.Contains(v)).ToList();
        if (hits.Count == 0 && v.Length > 0) hits = TypeAliases.Where(a => a.Aliases.Any(x => x.StartsWith(v, StringComparison.Ordinal))).ToList();
        if (hits.Count == 0)
        {
            Tokens.Add(new SearchToken(SearchTokenKind.Invalid, raw, start, value.Length == 0 ? L.T("Typ: …") : L.F("Typ „{0}“ unbekannt", value)));
            return;
        }
        foreach (var k in hits.SelectMany(h => h.Kinds)) Kinds.Add(k);
        Tokens.Add(new SearchToken(SearchTokenKind.Type, raw, start, L.T("Typ: ") + string.Join(", ", hits.Select(h => h.Label))));
    }

    private void AddFlag(string raw, int start, string value)
    {
        var v = value.ToLowerInvariant();
        var hit = FlagAliases.FirstOrDefault(a => a.Aliases.Contains(v));
        if (hit.Aliases == null && v.Length > 0) hit = FlagAliases.FirstOrDefault(a => a.Aliases.Any(x => x.StartsWith(v, StringComparison.Ordinal)));
        if (hit.Aliases == null)
        {
            Tokens.Add(new SearchToken(SearchTokenKind.Invalid, raw, start, value.Length == 0 ? L.T("ist: …") : L.F("„ist:{0}“ unbekannt", value)));
            return;
        }
        switch (hit.Kind)
        {
            case SearchTokenKind.Favorite: Favorites = true; break;
            case SearchTokenKind.Admin: Admin = true; break;
            case SearchTokenKind.Remote: Remote = true; break;
            case SearchTokenKind.Problem: Problems = true; break;
        }
        Tokens.Add(new SearchToken(hit.Kind, raw, start, hit.Label));
    }

    /// <summary>Teilt an Leerzeichen; Anführungszeichen halten zusammen („gruppe:"Kunde A"“).</summary>
    private static IEnumerable<(string Word, int Start)> Split(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) yield break;
            int start = i;
            bool quoted = false;
            while (i < text.Length && (quoted || !char.IsWhiteSpace(text[i])))
            {
                if (text[i] == '"') quoted = !quoted;
                i++;
            }
            yield return (text[start..i], start);
        }
    }

    private static string Unquote(string s) => s.Replace("\"", string.Empty).Trim();

    /// <summary>Entfernt einen erkannten Teil aus dem Suchtext (Klick auf ✕ am Chip).</summary>
    public static string Remove(string text, SearchToken token)
    {
        if (token.Start < 0 || token.Start + token.Raw.Length > text.Length || string.CompareOrdinal(text, token.Start, token.Raw, 0, token.Raw.Length) != 0)
            return text;
        return (text[..token.Start] + text[(token.Start + token.Raw.Length)..]).Replace("  ", " ").Trim();
    }

    /// <summary>Art eines Eintrags für <c>typ:</c>.</summary>
    public static SearchKind KindOf(LaunchItem item) => item.CustomKind switch
    {
        CustomEntryKind.Url => SearchKind.Web,
        CustomEntryKind.Folder => SearchKind.Folder,
        CustomEntryKind.Rdp => SearchKind.Rdp,
        CustomEntryKind.Ssh => SearchKind.Ssh,
        CustomEntryKind.Snippet => SearchKind.Snippet,
        CustomEntryKind.Command => SearchKind.Command,
        CustomEntryKind.Tool => SearchKind.Tool,
        _ => item.Type switch
        {
            ItemType.PowerShell => SearchKind.PowerShell,
            ItemType.Batch => SearchKind.Batch,
            ItemType.Executable => SearchKind.Executable,
            ItemType.Shortcut => SearchKind.Shortcut,
            ItemType.ConsoleSnapIn => SearchKind.SnapIn,
            _ => SearchKind.File
        }
    };

    /// <summary>Prüft Typ, Gruppe und Merkmale (Schlagworte, Favoriten und Probleme liefert der Aufrufer).</summary>
    public bool MatchesFilters(LaunchItem item, string groupPath, bool isFavorite, IReadOnlyList<string> itemTags, Func<bool>? hasProblem = null)
    {
        if (Kinds.Count > 0 && !Kinds.Contains(KindOf(item))) return false;
        if (Groups.Count > 0 && !Groups.All(g => groupPath.Contains(g, StringComparison.CurrentCultureIgnoreCase))) return false;
        if (Favorites && !isFavorite) return false;
        if (Admin && !item.RequiresAdmin) return false;
        if (Remote && !item.SupportsRemote) return false;
        if (Tags.Count > 0 && !Config.Tags.Matches(itemTags, Tags)) return false;
        if (Problems && !(item.ParseError != null || item.IsBlocked || (hasProblem?.Invoke() ?? false))) return false;
        return true;
    }
}
