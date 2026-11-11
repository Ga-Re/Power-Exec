using PowerExec.Core.Search;

namespace PowerExec.UI.Infrastructure;

/// <summary>Ein Textabschnitt; <see cref="IsHit"/> = Treffer der Suche.</summary>
public sealed record TextPart(string Text, bool IsHit)
{
    /// <summary>Zerlegt einen Text an den Treffern der Suchbegriffe.</summary>
    public static IReadOnlyList<TextPart> Split(string? text, IReadOnlyList<string> terms)
    {
        text ??= string.Empty;
        if (terms.Count == 0 || text.Length == 0) return new[] { new TextPart(text, false) };
        var parts = new List<TextPart>();
        int pos = 0;
        foreach (var r in FuzzyMatcher.Highlights(text, terms))
        {
            int start = Math.Clamp(r.Start, pos, text.Length), end = Math.Clamp(r.End, start, text.Length);
            if (start > pos) parts.Add(new TextPart(text[pos..start], false));
            if (end > start) parts.Add(new TextPart(text[start..end], true));
            pos = end;
        }
        if (pos < text.Length) parts.Add(new TextPart(text[pos..], false));
        return parts;
    }
}
