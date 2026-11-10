using PowerExec.Core.Localization;
using PowerExec.Core.Models;

namespace PowerExec.Core.Search;

/// <summary>Ein Vorschlag unter dem Suchfeld: <see cref="Insert"/> ersetzt das aktuelle Wort.</summary>
public sealed record SearchSuggestion(string Insert, string Label, string Detail, int Count)
{
    /// <summary>Danach ein Leerzeichen anhängen (bei „typ:“ oder „#“ ohne Wert nicht, dort geht es mit dem Wert weiter).</summary>
    public bool AppendSpace => !Insert.EndsWith(':') && Insert != "#";

    /// <summary>Filterart (Schlagwort, Typ, Gruppe, Merkmal) statt eines Werts – Auswahl zeigt danach die Werte.</summary>
    public bool IsCategory { get; init; }

    /// <summary>Symbol (Segoe Fluent Icons) vor einer Filterart.</summary>
    public string Glyph { get; init; } = string.Empty;
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
    /// <summary>Höchstzahl beim Weitertippen; ohne getippten Wert („#“, „typ:“) bis <see cref="MaxBrowse"/> zum Durchblättern.</summary>
    public const int MaxSuggestions = 8;
    public const int MaxBrowse = 40;

    /// <summary>
    /// Filterarten beim Klick ins Suchfeld (aktuelles Wort leer): Schlagwort, Typ, Gruppe, Merkmal –
    /// nur, wofür es Einträge gibt; als Beschreibung ein paar Beispiele.
    /// </summary>
    public static IReadOnlyList<SearchSuggestion> Categories(SuggestionSource src)
    {
        var list = new List<SearchSuggestion>();
        if (src.Tags.Count > 0)
            list.Add(new SearchSuggestion("#", L.T("Schlagwort"), Examples(src.Tags.OrderByDescending(t => t.Value).ThenBy(t => t.Key).Select(t => "#" + t.Key), src.Tags.Count), src.Tags.Count)
                { IsCategory = true, Glyph = "\uE8EC" });
        var kinds = src.Kinds.Where(k => k.Value > 0 && KindNames.ContainsKey(k.Key)).OrderByDescending(k => k.Value).ToList();
        if (kinds.Count > 0)
            list.Add(new SearchSuggestion(TypeKey, L.T("Typ"), Examples(kinds.Select(k => KindNames[k.Key].Label), kinds.Count), kinds.Count)
                { IsCategory = true, Glyph = "\uE7C3" });
        var groups = src.Groups.Where(g => g.Value > 0 && g.Key.Length > 0).ToList();
        if (groups.Count > 0)
            list.Add(new SearchSuggestion(GroupKey, L.T("Gruppe"), Examples(groups.OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).Select(g => g.Key), groups.Count), groups.Count)
                { IsCategory = true, Glyph = "\uE8B7" });
        var flags = Flags(src).Where(f => f.Count > 0).ToList();
        if (flags.Count > 0)
            list.Add(new SearchSuggestion(FlagKey, L.T("Merkmal"), Examples(flags.Select(f => f.Label), flags.Count), flags.Count)
                { IsCategory = true, Glyph = "\uE734" });
        return list;
    }

    private static string Examples(IEnumerable<string> values, int total)
    {
        var shown = values.Take(3).ToList();
        return string.Join(", ", shown) + (total > shown.Count ? " …" : string.Empty);
    }

    private static IEnumerable<(string Alias, string Label, int Count)> Flags(SuggestionSource src) => new[]
    {
        ("fav", L.T("Favoriten"), src.Favorites), ("admin", L.T("Benötigt Administrator"), src.Admin),
        ("remote", L.T("Remote-fähig"), src.Remote), ("problem", L.T("Mit Problemen"), src.Problems)
    };

    /// <summary>Sammelarten wie die früheren Filterknöpfe: nur, wenn sie mehr als eine vorhandene Art zusammenfassen.</summary>
    private static readonly (string Alias, string Label, SearchKind[] Kinds)[] KindGroups =
    {
        ("script", L.T("Scripte (PowerShell und Batch)"), new[] { SearchKind.PowerShell, SearchKind.Batch }),
        (L.IsEnglish ? "program" : "programm", L.T("Programme (.exe, Verknüpfungen, Snap-ins)"), new[] { SearchKind.Executable, SearchKind.Shortcut, SearchKind.SnapIn }),
    };

    /// <summary>Kanonischer Wert für <c>typ:</c> und Anzeigename je Art.</summary>
    public static readonly IReadOnlyDictionary<SearchKind, (string Alias, string Label)> KindNames = new Dictionary<SearchKind, (string, string)>
    {
        [SearchKind.PowerShell] = ("ps1", "PowerShell-Script"),
        [SearchKind.Batch] = ("cmd", "Batch (.cmd/.bat)"),
        [SearchKind.Executable] = ("exe", "Programm (.exe)"),
        [SearchKind.Shortcut] = ("lnk", "Verknüpfung"),
        [SearchKind.SnapIn] = ("msc", "Snap-in"),
        [SearchKind.Web] = ("web", "Webseite"),
        [SearchKind.Folder] = (L.IsEnglish ? "folder" : "ordner", "Ordner"),
        [SearchKind.Rdp] = ("rdp", "Remotedesktop"),
        [SearchKind.Ssh] = ("ssh", "SSH"),
        [SearchKind.Snippet] = ("snippet", "Snippet"),
        [SearchKind.Command] = (L.IsEnglish ? "command" : "befehl", "Befehl"),
        [SearchKind.Tool] = (L.IsEnglish ? "tool" : "werkzeug", "Windows-Werkzeug"),
        [SearchKind.File] = (L.IsEnglish ? "file" : "datei", "Datei"),
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
                .Select(t => new SearchSuggestion("#" + t.Key, "#" + t.Key, L.T("Schlagwort"), t.Value)));
            return list.Take(prefix.Length == 0 ? MaxBrowse : MaxSuggestions).ToList();
        }

        var colon = token.IndexOf(':');
        if (colon > 0)
        {
            var key = token[..colon].ToLowerInvariant();
            var value = token[(colon + 1)..].Trim('"');
            switch (key)
            {
                case "typ" or "type" or "t" or "art":
                    foreach (var (alias, label, groupKinds) in KindGroups)
                    {
                        var present = groupKinds.Where(k => src.Kinds.GetValueOrDefault(k) > 0).ToList();
                        if (present.Count > 1 && (value.Length == 0 || alias.StartsWith(value, StringComparison.OrdinalIgnoreCase) || label.Contains(value, StringComparison.CurrentCultureIgnoreCase)))
                            list.Add(new SearchSuggestion($"{key}:{alias}", $"{key}:{alias}", label, present.Sum(k => src.Kinds[k])));
                    }
                    list.AddRange(src.Kinds.Where(k => k.Value > 0 && KindNames.ContainsKey(k.Key))
                        .Select(k => (k, n: KindNames[k.Key]))
                        .Where(x => value.Length == 0 || x.n.Alias.StartsWith(value, StringComparison.OrdinalIgnoreCase) || x.n.Label.Contains(value, StringComparison.CurrentCultureIgnoreCase))
                        .OrderByDescending(x => x.k.Value)
                        .Select(x => new SearchSuggestion($"{key}:{x.n.Alias}", $"{key}:{x.n.Alias}", x.n.Label, x.k.Value)));
                    break;
                case "gruppe" or "group" or "g" or "in":
                    list.AddRange(src.Groups.Where(g => g.Value > 0 && g.Key.Length > 0 && (value.Length == 0 || g.Key.Contains(value, StringComparison.CurrentCultureIgnoreCase)))
                        .OrderByDescending(g => g.Key.StartsWith(value, StringComparison.CurrentCultureIgnoreCase)).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                        .Select(g => new SearchSuggestion($"{key}:{Quote(g.Key)}", $"{key}:{g.Key}", L.T("Gruppe"), g.Value)));
                    break;
                case "ist" or "is":
                    foreach (var (alias, label, count) in Flags(src))
                        if (count > 0 && alias.StartsWith(value, StringComparison.OrdinalIgnoreCase))
                            list.Add(new SearchSuggestion($"{key}:{alias}", $"{key}:{alias}", label, count));
                    break;
            }
            return list.Take(value.Length == 0 ? MaxBrowse : MaxSuggestions).ToList();
        }

        // Schlüssel anbieten, wenn das Wort wie ihr Anfang aussieht („ty“ → typ:, „gr“ → gruppe:)
        if (token.Length >= 2)
        {
            var t = token.ToLowerInvariant();
            var typ = TypeKey.TrimEnd(':'); var grp = GroupKey.TrimEnd(':'); var ist = FlagKey.TrimEnd(':');
            if (typ.StartsWith(t) && src.Kinds.Count > 0)
                list.Add(new SearchSuggestion(TypeKey, TypeKey, L.F("nach Art filtern ({0} …)", string.Join(", ", src.Kinds.Keys.Take(4).Select(k => KindNames[k].Alias))), src.Kinds.Values.Sum()));
            if (grp.StartsWith(t) && src.Groups.Count > 0) list.Add(new SearchSuggestion(GroupKey, GroupKey, L.T("nach Gruppe filtern"), src.Groups.Count));
            if (ist.StartsWith(t) && src.Favorites + src.Admin + src.Remote + src.Problems > 0) list.Add(new SearchSuggestion(FlagKey, FlagKey, L.T("Favoriten, Admin, Remote, Probleme"), src.Favorites));
        }
        return list;
    }

    /// <summary>Filterschlüssel in der Oberflächensprache (erkannt werden immer beide).</summary>
    private static string TypeKey => L.IsEnglish ? "type:" : "typ:";
    private static string GroupKey => L.IsEnglish ? "group:" : "gruppe:";
    private static string FlagKey => L.IsEnglish ? "is:" : "ist:";

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
