using PowerExec.Core.Models;

namespace PowerExec.Core.Search;

/// <summary>Ein Vorschlag unter dem Suchfeld: <see cref="Insert"/> ersetzt das aktuelle Wort.</summary>
public sealed record SearchSuggestion(string Insert, string Label, string Detail, int Count)
{
    /// <summary>Danach ein Leerzeichen anhängen (bei „typ:“ ohne Wert nicht, dort geht es mit dem Wert weiter).</summary>
    public bool AppendSpace => !Insert.EndsWith(':');
}

/// <summary>Was die sichtbaren Einträge hergeben – nur das wird vorgeschlagen (keine leeren Filter).</summary>
public sealed class SuggestionSource
{
    public Dictionary<string, int> Tags { get; } = new(StringComparer.CurrentCultureIgnoreCase);
    public Dictionary<SearchKind, int> Kinds { get; } = new();
    public Dictionary<string, int> Groups { get; } = new(StringComparer.CurrentCultureIgnoreCase);
    public int Favorites { get; set; }
    public int Admin { get; set; }
    public int Remote { get; set; }
    public int Problems { get; set; }
}

/// <summary>
/// Vorschläge beim Tippen: „#“ → vorhandene Schlagworte, „typ:“ → Typen, die es tatsächlich gibt,
/// „gruppe:“ → Gruppen mit Einträgen, „ist:“ → Merkmale mit Treffern; dazu die Schlüssel selbst, wenn man sie anfängt.
/// Jeder Vorschlag nennt, wie viele Einträge er trifft.
/// </summary>
public static class SearchSuggestions
{
    public const int MaxSuggestions = 8;

    /// <summary>Kanonischer Wert für <c>typ:</c> und Anzeigename je Art.</summary>
    public static readonly IReadOnlyDictionary<SearchKind, (string Alias, string Label)> KindNames = new Dictionary<SearchKind, (string, string)>
    {
        [SearchKind.PowerShell] = ("ps1", "PowerShell-Script"),
        [SearchKind.Batch] = ("cmd", "Batch (.cmd/.bat)"),
        [SearchKind.Executable] = ("exe", "Programm (.exe)"),
        [SearchKind.Shortcut] = ("lnk", "Verknüpfung"),
        [SearchKind.SnapIn] = ("msc", "Snap-in"),
        [SearchKind.Web] = ("web", "Webseite"),
        [SearchKind.Folder] = ("ordner", "Ordner"),
        [SearchKind.Rdp] = ("rdp", "Remotedesktop"),
        [SearchKind.Ssh] = ("ssh", "SSH"),
        [SearchKind.Snippet] = ("snippet", "Snippet"),
        [SearchKind.Command] = ("befehl", "Befehl"),
        [SearchKind.Tool] = ("werkzeug", "Windows-Werkzeug"),
        [SearchKind.File] = ("datei", "Datei"),
    };

    /// <summary>Wort vor dem Cursor: (Beginn, Text). Anführungszeichen halten zusammen.</summary>
    public static (int Start, string Token) CurrentToken(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        int start = caret;
        bool quoted = text[..caret].Count(c => c == '"') % 2 == 1;
        while (start > 0 && (quoted || !char.IsWhiteSpace(text[start - 1])))
        {
            if (text[start - 1] == '"') quoted = !quoted;
            start--;
        }
        return (start, text[start..caret]);
    }

    public static IReadOnlyList<SearchSuggestion> For(string text, int caret, SuggestionSource src)
    {
        var (_, token) = CurrentToken(text ?? string.Empty, caret);
        if (token.Length == 0) return Array.Empty<SearchSuggestion>();
        var list = new List<SearchSuggestion>();

        if (token[0] == '#')
        {
            var prefix = token[1..];
            list.AddRange(src.Tags.Where(t => prefix.Length == 0 || t.Key.Contains(prefix, StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(t => t.Key.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)).ThenByDescending(t => t.Value).ThenBy(t => t.Key)
                .Select(t => new SearchSuggestion("#" + t.Key, "#" + t.Key, "Schlagwort", t.Value)));
            return list.Take(MaxSuggestions).ToList();
        }

        var colon = token.IndexOf(':');
        if (colon > 0)
        {
            var key = token[..colon].ToLowerInvariant();
            var value = token[(colon + 1)..].Trim('"');
            switch (key)
            {
                case "typ" or "type" or "t" or "art":
                    list.AddRange(src.Kinds.Where(k => k.Value > 0 && KindNames.ContainsKey(k.Key))
                        .Select(k => (k, n: KindNames[k.Key]))
                        .Where(x => value.Length == 0 || x.n.Alias.StartsWith(value, StringComparison.OrdinalIgnoreCase) || x.n.Label.Contains(value, StringComparison.CurrentCultureIgnoreCase))
                        .OrderByDescending(x => x.k.Value)
                        .Select(x => new SearchSuggestion($"{key}:{x.n.Alias}", $"{key}:{x.n.Alias}", x.n.Label, x.k.Value)));
                    break;
                case "gruppe" or "group" or "g" or "in":
                    list.AddRange(src.Groups.Where(g => g.Value > 0 && g.Key.Length > 0 && (value.Length == 0 || g.Key.Contains(value, StringComparison.CurrentCultureIgnoreCase)))
                        .OrderByDescending(g => g.Key.StartsWith(value, StringComparison.CurrentCultureIgnoreCase)).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                        .Select(g => new SearchSuggestion($"{key}:{Quote(g.Key)}", $"{key}:{g.Key}", "Gruppe", g.Value)));
                    break;
                case "ist" or "is":
                    foreach (var (alias, label, count) in new[] { ("fav", "Favoriten", src.Favorites), ("admin", "Benötigt Administrator", src.Admin), ("remote", "Remote-fähig", src.Remote), ("problem", "Mit Problemen", src.Problems) })
                        if (count > 0 && alias.StartsWith(value, StringComparison.OrdinalIgnoreCase))
                            list.Add(new SearchSuggestion($"{key}:{alias}", $"{key}:{alias}", label, count));
                    break;
            }
            return list.Take(MaxSuggestions).ToList();
        }

        // Schlüssel anbieten, wenn das Wort wie ihr Anfang aussieht („ty“ → typ:, „gr“ → gruppe:)
        if (token.Length >= 2)
        {
            var t = token.ToLowerInvariant();
            if ("typ".StartsWith(t) && src.Kinds.Count > 0) list.Add(new SearchSuggestion("typ:", "typ:", $"nach Art filtern ({string.Join(", ", src.Kinds.Keys.Take(4).Select(k => KindNames[k].Alias))} …)", src.Kinds.Values.Sum()));
            if ("gruppe".StartsWith(t) && src.Groups.Count > 0) list.Add(new SearchSuggestion("gruppe:", "gruppe:", "nach Gruppe filtern", src.Groups.Count));
            if ("ist".StartsWith(t) && src.Favorites + src.Admin + src.Remote + src.Problems > 0) list.Add(new SearchSuggestion("ist:", "ist:", "Favoriten, Admin, Remote, Probleme", src.Favorites));
        }
        return list;
    }

    private static string Quote(string value) => value.Contains(' ') ? "\"" + value + "\"" : value;

    /// <summary>Vorschlag einsetzen: ersetzt das Wort vor dem Cursor; liefert neuen Text und Cursorposition.</summary>
    public static (string Text, int Caret) Apply(string text, int caret, SearchSuggestion s)
    {
        text ??= string.Empty;
        caret = Math.Clamp(caret, 0, text.Length);
        var (start, _) = CurrentToken(text, caret);
        // Rest des Wortes hinter dem Cursor mit ersetzen
        int end = caret;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        var insert = s.Insert + (s.AppendSpace ? " " : string.Empty);
        var rest = text[end..].TrimStart();
        var result = text[..start] + insert + (rest.Length > 0 && s.AppendSpace ? rest : rest.Length > 0 ? " " + rest : string.Empty);
        return (result, start + insert.Length);
    }
}
