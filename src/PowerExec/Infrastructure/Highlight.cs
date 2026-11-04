using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PowerExec.Core.Search;

namespace PowerExec.Infrastructure;

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

/// <summary>
/// Füllt einen TextBlock aus Abschnitten und hebt Treffer hervor:
/// <c>&lt;TextBlock infra:Highlight.Parts="{Binding NameParts}"/&gt;</c>.
/// </summary>
public static class Highlight
{
    public static readonly DependencyProperty PartsProperty = DependencyProperty.RegisterAttached(
        "Parts", typeof(IEnumerable<TextPart>), typeof(Highlight), new PropertyMetadata(null, OnPartsChanged));

    public static IEnumerable<TextPart>? GetParts(DependencyObject d) => (IEnumerable<TextPart>?)d.GetValue(PartsProperty);
    public static void SetParts(DependencyObject d, IEnumerable<TextPart>? value) => d.SetValue(PartsProperty, value);

    private static void OnPartsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        if (e.NewValue is not IEnumerable<TextPart> parts) return;
        foreach (var p in parts)
        {
            var run = new Run(p.Text);
            if (p.IsHit)
            {
                run.SetResourceReference(TextElement.BackgroundProperty, "Search.Highlight");
                run.FontWeight = FontWeights.Bold;
            }
            tb.Inlines.Add(run);
        }
    }
}
