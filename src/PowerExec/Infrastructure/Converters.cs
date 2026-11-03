using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PowerExec.Infrastructure;

/// <summary>true → Visible. Mit Parameter "invert" umgekehrt. Auch für Strings (nicht leer) und Zahlen (> 0).</summary>
public sealed class VisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value switch
        {
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i > 0,
            null => false,
            _ => true
        };
        var p = parameter as string ?? string.Empty;
        if (p.Contains("invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;
        // „hidden“: Platz freihalten (z. B. damit Zeilen bündig bleiben)
        return visible ? Visibility.Visible : p.Contains("hidden", StringComparison.OrdinalIgnoreCase) ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Vergleicht einen Enum-/Stringwert mit dem Parameter (für RadioButtons).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
        {
            if (targetType.IsEnum) return Enum.Parse(targetType, parameter.ToString()!);
            return parameter;
        }
        return Binding.DoNothing;
    }
}

/// <summary>bool → Drehwinkel für Aufklapp-Pfeile.</summary>
public sealed class ExpandAngleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? 90.0 : 0.0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Wert gleich Parameter → Visible.</summary>
public sealed class EqualsVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>#RRGGBB → Pinsel (leer/ungültig = transparent).</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Core.Config.PanelColors.TryParse(value as string, out var c)
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B))
            : System.Windows.Media.Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Schlagwort → getönter Chip-Hintergrund (Parameter „has“: hat das Schlagwort eine Farbe?).</summary>
public sealed class TagBrushConverter : IValueConverter
{
    /// <summary>Farbe eines Schlagworts (setzt die App).</summary>
    public static Func<string, string>? Lookup { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value is string tag ? Lookup?.Invoke(tag) ?? string.Empty : string.Empty;
        bool has = Core.Config.PanelColors.TryParse(hex, out var c);
        if (parameter as string == "has") return has;
        return has ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x60, c.R, c.G, c.B)) : System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
