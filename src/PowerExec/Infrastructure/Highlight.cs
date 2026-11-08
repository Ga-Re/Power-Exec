using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PowerExec.Core.Search;

namespace PowerExec.Infrastructure;

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

/// <summary>
/// Füllt einen TextBlock mit farbigem Quelltext: <c>&lt;TextBlock infra:CodeView.Segments="{Binding Code}"/&gt;</c>.
/// Farben kommen aus dem Design (Code.Keyword, Code.String …).
/// </summary>
public static class CodeView
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments", typeof(IEnumerable<Core.Metadata.CodeSegment>), typeof(CodeView), new PropertyMetadata(null, OnChanged));

    public static IEnumerable<Core.Metadata.CodeSegment>? GetSegments(DependencyObject d) => (IEnumerable<Core.Metadata.CodeSegment>?)d.GetValue(SegmentsProperty);
    public static void SetSegments(DependencyObject d, IEnumerable<Core.Metadata.CodeSegment>? value) => d.SetValue(SegmentsProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        if (e.NewValue is not IEnumerable<Core.Metadata.CodeSegment> segments) return;
        foreach (var s in segments)
        {
            var run = new Run(s.Text);
            if (s.Kind != Core.Metadata.CodeTokenKind.Text) run.SetResourceReference(TextElement.ForegroundProperty, "Code." + s.Kind);
            if (s.Kind == Core.Metadata.CodeTokenKind.Comment) run.FontStyle = FontStyles.Italic;
            tb.Inlines.Add(run);
        }
    }
}
