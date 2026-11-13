using System.Windows;
using System.Windows.Controls;

namespace PowerExec.Desktop.Controls;

/// <summary>
/// Raster für Karten: so viele gleich breite Spalten, wie mit <see cref="MinItemWidth"/> in die Breite passen, Karten einer
/// Zeile gleich hoch. Ein Element mit <c>CardGridPanel.Span="True"</c> (aufgeklappte Karte) bekommt eine eigene Zeile
/// über die ganze Breite; die Reihenfolge der übrigen Karten bleibt.
/// </summary>
public sealed class CardGridPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(CardGridPanel), new FrameworkPropertyMetadata(260.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(CardGridPanel), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpanProperty = DependencyProperty.RegisterAttached(
        "Span", typeof(bool), typeof(CardGridPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>Mindestbreite einer Karte in DIP.</summary>
    public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }

    /// <summary>Abstand zwischen Karten, waagerecht und senkrecht.</summary>
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    public static bool GetSpan(DependencyObject element) => (bool)element.GetValue(SpanProperty);
    public static void SetSpan(DependencyObject element, bool value) => element.SetValue(SpanProperty, value);

    /// <summary>Eine Zeile des Rasters: Indizes der Elemente, Höhe, und ob sie ein einzelnes Element über die ganze Breite ist.</summary>
    internal readonly record struct Row(int Start, int Count, bool Spans);

    /// <summary>Spaltenzahl und Kartenbreite für eine verfügbare Breite.</summary>
    internal static (int Columns, double ItemWidth) Columns(double available, double minItemWidth, double gap, int count)
    {
        var min = Math.Max(1, minItemWidth);
        if (double.IsInfinity(available) || double.IsNaN(available)) return (Math.Max(1, count), min);
        var columns = Math.Max(1, (int)Math.Floor((available + gap) / (min + gap)));
        return (columns, Math.Max(0, (available - gap * (columns - 1)) / columns));
    }

    /// <summary>Teilt die Elemente in Zeilen: je <paramref name="columns"/> Karten, aufgeklappte einzeln.</summary>
    internal static IReadOnlyList<Row> Rows(IReadOnlyList<bool> spans, int columns)
    {
        var rows = new List<Row>();
        int start = 0, n = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            if (spans[i])
            {
                if (n > 0) rows.Add(new Row(start, n, false));
                rows.Add(new Row(i, 1, true));
                start = i + 1;
                n = 0;
                continue;
            }
            if (n == 0) start = i;
            if (++n == columns) { rows.Add(new Row(start, n, false)); n = 0; }
        }
        if (n > 0) rows.Add(new Row(start, n, false));
        return rows;
    }

    private IReadOnlyList<Row> _rows = Array.Empty<Row>();
    private double[] _heights = Array.Empty<double>();
    private double _itemWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;
        var (columns, itemWidth) = Columns(availableSize.Width, MinItemWidth, Gap, children.Count);
        _itemWidth = itemWidth;
        var full = double.IsInfinity(availableSize.Width) ? itemWidth * columns + Gap * (columns - 1) : availableSize.Width;
        _rows = Rows(children.Cast<UIElement>().Select(GetSpan).ToList(), columns);
        _heights = new double[_rows.Count];
        double total = 0;
        for (int r = 0; r < _rows.Count; r++)
        {
            var row = _rows[r];
            double height = 0;
            for (int i = row.Start; i < row.Start + row.Count; i++)
            {
                children[i].Measure(new Size(row.Spans ? full : itemWidth, double.PositiveInfinity));
                height = Math.Max(height, children[i].DesiredSize.Height);
            }
            _heights[r] = height;
            total += height + (r > 0 ? Gap : 0);
        }
        return new Size(full, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;
        double y = 0;
        for (int r = 0; r < _rows.Count; r++)
        {
            var row = _rows[r];
            for (int i = row.Start; i < row.Start + row.Count && i < children.Count; i++)
            {
                var x = row.Spans ? 0 : (i - row.Start) * (_itemWidth + Gap);
                children[i].Arrange(new Rect(x, y, row.Spans ? finalSize.Width : _itemWidth, _heights[r]));
            }
            y += _heights[r] + Gap;
        }
        return finalSize;
    }
}
