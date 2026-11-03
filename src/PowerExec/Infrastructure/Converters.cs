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
