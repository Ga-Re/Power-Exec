namespace PowerExec.Core.Search;

/// <summary>Ein Abschnitt in einem Text (für die Hervorhebung von Treffern).</summary>
public readonly record struct TextRange(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>Ergebnis eines Abgleichs: je höher <see cref="Score"/>, desto besser.</summary>
public sealed record FuzzyResult(int Score, IReadOnlyList<TextRange> Ranges);

/// <summary>
/// Unscharfe Suche für das Panel:
/// 1. zusammenhängender Treffer („disk“ in „Get-DiskSpace“),
/// 2. Anfangsbuchstaben bzw. Teilfolge ab einem Wortanfang („usr“ in „Reset-ADUserPassword“, „gds“ in „Get-DiskSpace“),
/// 3. ein Tippfehler bei Begriffen ab 4 Zeichen („dsik“ findet „Disk“).
/// Groß-/Kleinschreibung spielt keine Rolle.
/// </summary>
public static class FuzzyMatcher
{
    public const int ScoreContiguous = 100;
    public const int ScoreSubsequence = 40;
    public const int ScoreTypo = 25;

    public static FuzzyResult? Match(string? text, string? term)
    {
        if (string.IsNullOrEmpty(term)) return new FuzzyResult(0, Array.Empty<TextRange>());
        if (string.IsNullOrEmpty(text)) return null;
        return Contiguous(text, term) ?? Subsequence(text, term) ?? Typo(text, term);
    }

    /// <summary>Wortanfang: Textbeginn, nach Trennzeichen, CamelCase-Übergang oder Wechsel zwischen Ziffer und Buchstabe.</summary>
    public static bool IsBoundary(string text, int i)
    {
        if (i <= 0) return true;
        if (i >= text.Length) return false;
        char c = text[i], p = text[i - 1];
        if (!char.IsLetterOrDigit(c)) return false;
        if (!char.IsLetterOrDigit(p)) return true;
        if (char.IsUpper(c) && char.IsLower(p)) return true;
        if (char.IsUpper(c) && char.IsUpper(p) && i + 1 < text.Length && char.IsLower(text[i + 1])) return true; // „ADUser“ → „User“
        return char.IsDigit(c) != char.IsDigit(p);
    }

    private static bool Eq(char a, char b) => a == b || char.ToLowerInvariant(a) == char.ToLowerInvariant(b);

    private static FuzzyResult? Contiguous(string text, string term)
    {
        int best = -1, bestScore = int.MinValue;
        for (int i = text.IndexOf(term, StringComparison.CurrentCultureIgnoreCase); i >= 0;
             i = i + 1 < text.Length ? text.IndexOf(term, i + 1, StringComparison.CurrentCultureIgnoreCase) : -1)
        {
            int s = ScoreContiguous - Math.Min(i, 30) / 3;
            if (i == 0) s += 50;
            else if (IsBoundary(text, i)) s += 30;
            if (i + term.Length == text.Length || IsBoundary(text, i + term.Length) || !char.IsLetterOrDigit(text[i + term.Length])) s += 10;
            if (s > bestScore) { bestScore = s; best = i; }
        }
        return best < 0 ? null : new FuzzyResult(bestScore + term.Length * 2, new[] { new TextRange(best, term.Length) });
    }

    private static FuzzyResult? Subsequence(string text, string term)
    {
        if (term.Length < 2) return null;
        FuzzyResult? best = null;
        int maxSpan = term.Length * 4 + 4;
        for (int start = 0; start < text.Length; start++)
        {
            if (!Eq(text[start], term[0]) || !IsBoundary(text, start)) continue;
            var positions = new int[term.Length];
            positions[0] = start;
            int ti = 1, pos = start + 1;
            while (ti < term.Length && pos < text.Length && pos - start < maxSpan)
            {
                // Wortanfänge bevorzugen: gibt es den Buchstaben in Reichweite an einem Wortanfang, diesen nehmen
                int next = -1, nextBoundary = -1;
                for (int j = pos; j < text.Length && j - start < maxSpan; j++)
                {
                    if (!Eq(text[j], term[ti])) continue;
                    if (next < 0) next = j;
                    if (IsBoundary(text, j)) { nextBoundary = j; break; }
                }
                if (next < 0) break;
                // Direkt anschließend schlägt Wortanfang (zusammenhängend ist besser)
                int chosen = next == pos ? next : nextBoundary >= 0 ? nextBoundary : next;
                positions[ti++] = chosen;
                pos = chosen + 1;
            }
            if (ti < term.Length) continue;
            // Bei zwei Zeichen nur Anfangsbuchstaben („gd“ → Get-Disk), sonst passt fast alles
            if (term.Length <= 2 && !positions.All(p => IsBoundary(text, p))) continue;

            int score = ScoreSubsequence, span = positions[^1] - start + 1;
            for (int k = 0; k < positions.Length; k++)
            {
                if (IsBoundary(text, positions[k])) score += 8;
                if (k > 0 && positions[k] == positions[k - 1] + 1) score += 4;
            }
            score -= span - term.Length;
            if (start == 0) score += 10;
            if (best == null || score > best.Score) best = new FuzzyResult(score, Merge(positions.Select(p => new TextRange(p, 1))));
        }
        return best is { Score: > 0 } ? best : null;
    }

    private static FuzzyResult? Typo(string text, string term)
    {
        if (term.Length < 4) return null;
        var t = term.ToLowerInvariant();
        for (int i = 0; i < text.Length; i++)
        {
            if (!IsBoundary(text, i)) continue;
            int end = i;
            while (end < text.Length && char.IsLetterOrDigit(text[end])) end++;
            var word = text[i..end].ToLowerInvariant();
            // Wortanfang mit gleicher Länge (±1) vergleichen, damit „dsik“ auch „DiskSpace“ findet
            for (int len = t.Length - 1; len <= t.Length + 1; len++)
            {
                if (len < 3 || len > word.Length) continue;
                if (Distance(word[..len], t) <= 1)
                    return new FuzzyResult(ScoreTypo, new[] { new TextRange(i, len) });
            }
        }
        return null;
    }

    /// <summary>Damerau-Levenshtein-Abstand (eingeschränkt: Vertauschung zweier benachbarter Zeichen zählt 1).</summary>
    public static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    /// <summary>Überlappende oder angrenzende Abschnitte zusammenfassen.</summary>
    public static IReadOnlyList<TextRange> Merge(IEnumerable<TextRange> ranges)
    {
        var result = new List<TextRange>();
        foreach (var r in ranges.Where(r => r.Length > 0).OrderBy(r => r.Start))
        {
            if (result.Count > 0 && r.Start <= result[^1].End)
            {
                var last = result[^1];
                result[^1] = new TextRange(last.Start, Math.Max(last.End, r.End) - last.Start);
            }
            else result.Add(r);
        }
        return result;
    }

    /// <summary>Alle Begriffe in einem Text suchen und die Treffer für die Hervorhebung liefern (Begriffe ohne Treffer werden übergangen).</summary>
    public static IReadOnlyList<TextRange> Highlights(string? text, IEnumerable<string> terms)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<TextRange>();
        return Merge(terms.Select(t => Match(text, t)).Where(r => r != null).SelectMany(r => r!.Ranges));
    }
}

/// <summary>Bewertet einen Eintrag über mehrere Felder (Name zählt mehr als Beschreibung).</summary>
public static class SearchScoring
{
    /// <summary>Jeder Begriff muss in mindestens einem Feld vorkommen; null = kein Treffer.</summary>
    public static int? Score(IReadOnlyList<(string? Text, int Weight)> fields, IReadOnlyList<string> terms)
    {
        int total = 0;
        foreach (var term in terms)
        {
            int best = int.MinValue;
            foreach (var (text, weight) in fields)
            {
                if (string.IsNullOrEmpty(text)) continue;
                // In langen Feldern (Beschreibung, Code) nur zusammenhängend suchen – sonst passt fast alles
                var r = text.Length > 300 ? ContiguousOnly(text, term) : FuzzyMatcher.Match(text, term);
                if (r != null) best = Math.Max(best, r.Score * weight);
            }
            if (best == int.MinValue) return null;
            total += best;
        }
        return total;
    }

    private static FuzzyResult? ContiguousOnly(string text, string term) =>
        text.Contains(term, StringComparison.CurrentCultureIgnoreCase) ? new FuzzyResult(FuzzyMatcher.ScoreContiguous / 2, Array.Empty<TextRange>()) : null;

    /// <summary>Zusatzpunkte für häufig und kürzlich gestartete Einträge.</summary>
    public static int FrecencyBonus(double frecency) => frecency <= 0 ? 0 : (int)Math.Round(Math.Log2(1 + frecency) * 30);
}
