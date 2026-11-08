using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PowerExec.Infrastructure;

/// <summary>Neutrale Farben (<see cref="ArgbColor"/>) als WPF-Pinsel.</summary>
public static class WpfColors
{
    private static readonly ConcurrentDictionary<ArgbColor, SolidColorBrush> Cache = new();

    /// <summary>Eingefrorener, wiederverwendeter Pinsel; null bleibt null.</summary>
    public static SolidColorBrush? ToBrush(ArgbColor? color)
    {
        if (color is not { } c) return null;
        if (Cache.Count > 512) Cache.Clear();
        return Cache.GetOrAdd(c, static x =>
        {
            var b = new SolidColorBrush(Color.FromArgb(x.A, x.R, x.G, x.B));
            b.Freeze();
            return b;
        });
    }
}

/// <summary>
/// <see cref="ArgbColor"/> → Pinsel. Ist die Farbe null, gilt die Theme-Ressource aus dem ConverterParameter
/// (z. B. <c>ConverterParameter=Bg.Chip</c>), sonst bleibt es bei null.
/// </summary>
public sealed class ArgbBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        WpfColors.ToBrush(value as ArgbColor?)
        ?? (parameter is string key ? System.Windows.Application.Current?.TryFindResource(key) : null);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
