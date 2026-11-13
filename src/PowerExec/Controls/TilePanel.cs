using System.Windows;
using System.Windows.Controls;

namespace PowerExec.Desktop.Controls;

/// <summary>
/// Raster für Kacheln: so viele gleich breite Spalten, wie mit <see cref="MinItemWidth"/> in die Breite passen – die
/// Kacheln füllen die Zeile ganz aus. Ein fest breites Raster (WrapPanel) ließ rechts einen Rest frei und zwang zu
/// schmalen Kacheln, in denen Namen nach wenigen Zeichen abgeschnitten wurden.
/// </summary>
public sealed class TilePanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(TilePanel),
        new FrameworkPropertyMetadata(88.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Mindestbreite einer Kachel in DIP; die tatsächliche Breite ist die Zeilenbreite geteilt durch die Spaltenzahl.</summary>
    public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }

    /// <summary>Spaltenzahl und Kachelbreite für eine verfügbare Breite.</summary>
    internal static (int Columns, double ItemWidth) Layout(double available, double minItemWidth, int count)
    {
        var min = Math.Max(1, minItemWidth);
        if (double.IsInfinity(available) || double.IsNaN(available)) return (Math.Max(1, count), min);
        var columns = Math.Max(1, (int)Math.Floor(available / min));
        return (columns, Math.Floor(available / columns));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, itemWidth) = Layout(availableSize.Width, MinItemWidth, InternalChildren.Count);
        double height = 0, rowHeight = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (i % columns == columns - 1 || i == InternalChildren.Count - 1)
            {
                height += rowHeight;
                rowHeight = 0;
            }
        }
        var width = double.IsInfinity(availableSize.Width) ? itemWidth * Math.Min(columns, Math.Max(1, InternalChildren.Count)) : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = Layout(finalSize.Width, MinItemWidth, InternalChildren.Count);
        double y = 0;
        for (int start = 0; start < InternalChildren.Count; start += columns)
        {
            var end = Math.Min(start + columns, InternalChildren.Count);
            double rowHeight = 0;
            for (int i = start; i < end; i++) rowHeight = Math.Max(rowHeight, InternalChildren[i].DesiredSize.Height);
            for (int i = start; i < end; i++) InternalChildren[i].Arrange(new Rect((i - start) * itemWidth, y, itemWidth, rowHeight));
            y += rowHeight;
        }
        return finalSize;
    }
}
