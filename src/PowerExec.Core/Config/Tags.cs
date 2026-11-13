namespace PowerExec.Core.Config;

/// <summary>Schlagworte an Einträgen; in der Suche mit „#schlagwort“ filterbar.</summary>
public static class Tags
{
    public static List<string> Parse(string? text) =>
        (text ?? string.Empty).Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.TrimStart('#').Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static string Format(IEnumerable<string> tags) => string.Join(", ", tags);

    public static IReadOnlyList<string> Of(AppSettings s, string path) => s.FindEntry(path)?.Tags ?? (IReadOnlyList<string>)Array.Empty<string>();

    public static IReadOnlyList<string> All(AppSettings s) =>
        s.Entries.SelectMany(e => e.Tags).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Schlagworte mit Anzahl der Einträge.</summary>
    public static IReadOnlyList<(string Tag, int Count)> Counts(AppSettings s) =>
        s.Entries.SelectMany(e => e.Tags.Distinct(StringComparer.CurrentCultureIgnoreCase))
            .GroupBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => (g.First(), g.Count()))
            .OrderBy(x => x.Item1, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Einträge (Pfade/Schlüssel) mit diesem Schlagwort.</summary>
    public static IReadOnlyList<string> EntriesWith(AppSettings s, string tag) =>
        s.Entries.Where(e => e.Tags.Contains(tag, StringComparer.CurrentCultureIgnoreCase)).Select(e => e.Path).ToList();

    /// <summary>Umbenennen; gibt es den neuen Namen schon, werden beide zusammengeführt.</summary>
    public static void Rename(AppSettings s, string oldTag, string newTag)
    {
        newTag = newTag.Trim().TrimStart('#');
        if (newTag.Length == 0) return;
        foreach (var e in s.Entries.Where(e => e.Tags.Contains(oldTag, StringComparer.CurrentCultureIgnoreCase)))
        {
            e.Tags = e.Tags.Where(t => !t.Equals(oldTag, StringComparison.OrdinalIgnoreCase) && !t.Equals(newTag, StringComparison.OrdinalIgnoreCase))
                .Append(newTag).ToList();
        }
        var color = ColorOf(s, oldTag);
        SetColor(s, oldTag, null);
        if (color.Length > 0 && ColorOf(s, newTag).Length == 0) SetColor(s, newTag, color);
    }

    /// <summary>Schlagwort von allen Einträgen entfernen.</summary>
    public static int Delete(AppSettings s, string tag)
    {
        int n = 0;
        foreach (var e in s.Entries)
            n += e.Tags.RemoveAll(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)) > 0 ? 1 : 0;
        s.Entries.RemoveAll(e => e.IsEmpty);
        SetColor(s, tag, null);
        return n;
    }

    public static string ColorOf(AppSettings s, string tag) =>
        s.TagColors.FirstOrDefault(kv => kv.Key.Equals(tag, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    public static void SetColor(AppSettings s, string tag, string? color)
    {
        foreach (var k in s.TagColors.Keys.Where(k => k.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList()) s.TagColors.Remove(k);
        if (PanelColors.TryParse(color, out var c)) s.TagColors[tag] = c.Hex;
    }

    public static void Set(AppSettings s, string path, IEnumerable<string> tags)
    {
        var list = tags.Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        var entry = s.FindEntry(path);
        if (entry == null)
        {
            if (list.Count == 0) return;
            entry = new EntryConfig { Path = path };
            s.Entries.Add(entry);
        }
        entry.Tags = list;
    }

    /// <summary>Teilt eine Suche in Schlagwort-Filter („#ad“) und normalen Text.</summary>
    public static (IReadOnlyList<string> Tags, string Text) SplitQuery(string query)
    {
        var tags = new List<string>();
        var words = new List<string>();
        foreach (var w in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (w.Length > 1 && w[0] == '#') tags.Add(w[1..]);
            else words.Add(w);
        }
        return (tags, string.Join(' ', words));
    }

    /// <summary>Alle gesuchten Schlagworte vorhanden (Präfix genügt: „#ex“ findet „Exchange“).</summary>
    public static bool Matches(IReadOnlyList<string> itemTags, IReadOnlyList<string> wanted) =>
        wanted.All(w => itemTags.Any(t => t.StartsWith(w, StringComparison.CurrentCultureIgnoreCase)));
}

/// <summary>Erkennt, was in der Zwischenablage steht, und schlägt eine Eintragsart vor.</summary>
public static class ClipboardClassifier
{
    public static CustomEntry Suggest(string text, Func<string, bool> isDirectory, Func<string, bool> isFile)
    {
        var t = (text ?? string.Empty).Trim().Trim('"');
        if (t.Contains('\n')) return new CustomEntry { Kind = CustomEntryKind.Snippet, Code = text!.Trim() };
        if (Uri.TryCreate(t, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return new CustomEntry { Kind = CustomEntryKind.Url, Target = t };
        if (t.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            return new CustomEntry { Kind = CustomEntryKind.Tool, Target = t };
        if (t.Length > 2 && (t.StartsWith(@"\\", StringComparison.Ordinal) || (t.Length > 2 && t[1] == ':' && (t[2] == '\\' || t[2] == '/'))))
        {
            if (isDirectory(t)) return new CustomEntry { Kind = CustomEntryKind.Folder, Target = t };
            if (isFile(t)) return new CustomEntry { Kind = CustomEntryKind.File, Target = t };
            return new CustomEntry { Kind = CustomEntryKind.Folder, Target = t };
        }
        if (t.StartsWith("ssh ", StringComparison.OrdinalIgnoreCase))
        {
            var target = t[4..].Trim().Split(' ')[0];
            var at = target.IndexOf('@');
            return new CustomEntry { Kind = CustomEntryKind.Ssh, Target = at >= 0 ? target[(at + 1)..] : target, User = at >= 0 ? target[..at] : string.Empty };
        }
        // Einzelnes Wort aus Buchstaben, Ziffern, Punkt und Bindestrich: wahrscheinlich ein Rechnername
        if (t.Length > 0 && t.Length <= 253 && t.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'))
            return new CustomEntry { Kind = CustomEntryKind.Rdp, Target = t };
        return new CustomEntry { Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text, Action = SnippetAction.Copy, Code = text?.Trim() ?? string.Empty };
    }
}
