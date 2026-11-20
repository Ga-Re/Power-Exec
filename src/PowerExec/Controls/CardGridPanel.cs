using System.Windows;
using System.Windows.Controls;
using PowerExec.Desktop.Infrastructure;

namespace PowerExec.Desktop.Controls;

/// <summary>
/// Raster für Karten: so viele gleich breite Spalten, wie mit <see cref="MinItemWidth"/> in die Breite passen, Karten einer
/// Zeile gleich hoch. Breiter als <see cref="MaxItemWidth"/> wird keine Karte – bei viel Platz bleibt rechts Raum, statt
/// dass sich die Karten in die Breite ziehen. Mit <see cref="Balanced"/> werden die Zeilen gleichmäßig gefüllt (7 Karten:
/// 4 + 3 statt 6 + 1). Ein Element mit <c>CardGridPanel.Span="True"</c> bekommt eine eigene Zeile über die ganze Breite;
/// die Reihenfolge der übrigen Karten bleibt.
/// Ändert sich die Spaltenzahl (Fenstergröße), gleiten die Karten an ihren neuen Platz, statt zu springen.
/// </summary>
public sealed class CardGridPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(CardGridPanel), new FrameworkPropertyMetadata(260.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxItemWidthProperty = DependencyProperty.Register(
        nameof(MaxItemWidth), typeof(double), typeof(CardGridPanel), new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(CardGridPanel), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty BalancedProperty = DependencyProperty.Register(
        nameof(Balanced), typeof(bool), typeof(CardGridPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty CenterWhenCappedProperty = DependencyProperty.Register(
        nameof(CenterWhenCapped), typeof(bool), typeof(CardGridPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>
    /// Greift die Höchstbreite, bleibt Platz übrig: das Raster mittig setzen statt den Rest rechts leer zu lassen
    /// (Spalten bleiben auch in der letzten, unvollständigen Zeile in der Flucht).
    /// </summary>
    public bool CenterWhenCapped { get => (bool)GetValue(CenterWhenCappedProperty); set => SetValue(CenterWhenCappedProperty, value); }

    public static readonly DependencyProperty SpanProperty = DependencyProperty.RegisterAttached(
        "Span", typeof(bool), typeof(CardGridPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>Mindestbreite einer Karte in DIP.</summary>
    public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }

    /// <summary>Höchstbreite einer Karte in DIP (Vorgabe: unbegrenzt).</summary>
    public double MaxItemWidth { get => (double)GetValue(MaxItemWidthProperty); set => SetValue(MaxItemWidthProperty, value); }

    /// <summary>Abstand zwischen Karten, waagerecht und senkrecht.</summary>
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    /// <summary>Zeilen gleichmäßig füllen: so wenige Spalten, wie es bei gleicher Zeilenzahl braucht.</summary>
    public bool Balanced { get => (bool)GetValue(BalancedProperty); set => SetValue(BalancedProperty, value); }

    public static bool GetSpan(DependencyObject element) => (bool)element.GetValue(SpanProperty);
    public static void SetSpan(DependencyObject element, bool value) => element.SetValue(SpanProperty, value);

    /// <summary>Eine Zeile des Rasters: Indizes der Elemente, Höhe, und ob sie ein einzelnes Element über die ganze Breite ist.</summary>
    internal readonly record struct Row(int Start, int Count, bool Spans);

    /// <summary>
    /// Spaltenzahl und Kartenbreite für eine verfügbare Breite: so viele Spalten wie mit der Mindestbreite passen, die
    /// Breite höchstens <paramref name="maxItemWidth"/>. <paramref name="balanced"/>: bei gleicher Zeilenzahl so wenige
    /// Spalten wie nötig (7 Karten in 6 Spalten → 4 + 3).
    /// </summary>
    internal static (int Columns, double ItemWidth) Columns(double available, double minItemWidth, double maxItemWidth, double gap, int count, bool balanced)
    {
        var min = Math.Max(1, minItemWidth);
        var max = Math.Max(min, maxItemWidth);
        if (double.IsInfinity(available) || double.IsNaN(available)) return (Math.Max(1, count), min);
        var columns = Math.Max(1, (int)Math.Floor((available + gap) / (min + gap)));
        if (balanced && count > 0 && columns < count)
        {
            var rows = (count + columns - 1) / columns;
            columns = (count + rows - 1) / rows;
        }
        else if (count > 0) columns = Math.Min(columns, Math.Max(1, count));
        var width = Math.Max(0, (available - gap * (columns - 1)) / columns);
        return (columns, Math.Min(width, max));
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
    private int _columns;
    private int _arrangedColumns;
    /// <summary>Wo jede Karte zuletzt stand (für das Gleiten nach einem Umbruch).</summary>
    private readonly Dictionary<UIElement, Point> _positions = new();

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;
        var (columns, itemWidth) = Columns(availableSize.Width, MinItemWidth, MaxItemWidth, Gap, children.Count, Balanced);
        _itemWidth = itemWidth;
        _columns = columns;
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
        // Nur ein Umbruch (andere Spaltenzahl) lässt die Karten gleiten – nicht jede kleine Breitenänderung beim Ziehen
        var glide = _arrangedColumns != 0 && _columns != _arrangedColumns && IsVisible && NavigationMotion.Enabled;
        var seen = new HashSet<UIElement>();
        var gridWidth = _columns * _itemWidth + Gap * (_columns - 1);
        var offset = CenterWhenCapped ? Math.Max(0, Math.Floor((finalSize.Width - gridWidth) / 2)) : 0;
        double y = 0;
        for (int r = 0; r < _rows.Count; r++)
        {
            var row = _rows[r];
            for (int i = row.Start; i < row.Start + row.Count && i < children.Count; i++)
            {
                var x = row.Spans ? 0 : offset + (i - row.Start) * (_itemWidth + Gap);
                var child = children[i];
                child.Arrange(new Rect(x, y, row.Spans ? finalSize.Width : _itemWidth, _heights[r]));
                var at = new Point(x, y);
                if (glide && _positions.TryGetValue(child, out var before) && before != at) NavigationMotion.Glide(child, before - at);
                _positions[child] = at;
                seen.Add(child);
            }
            y += _heights[r] + Gap;
        }
        // Entfernte Karten vergessen
        foreach (var gone in _positions.Keys.Where(k => !seen.Contains(k)).ToList()) _positions.Remove(gone);
        _arrangedColumns = _columns;
        return finalSize;
    }
}
