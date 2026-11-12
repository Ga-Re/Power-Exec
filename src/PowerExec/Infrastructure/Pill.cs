using System.Windows;
using System.Windows.Controls;

namespace PowerExec.Desktop.Infrastructure;

/// <summary>
/// Ganz runde Enden für Chips, Abzeichen und runde Farbfelder: <c>infra:Pill.IsEnabled="True"</c> an einem
/// <see cref="Border"/> hält dessen Eckenradius auf der halben Höhe. Ein fester, sehr großer Radius leistet das nicht:
/// WPF begrenzt den waagrechten und den senkrechten Radius getrennt, breite Elemente würden zu Ellipsen.
/// </summary>
public static class Pill
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(Pill), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border) return;
        border.SizeChanged -= OnSizeChanged;
        if (!(bool)e.NewValue) return;
        border.SizeChanged += OnSizeChanged;
        Apply(border);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged) Apply((Border)sender);
    }

    private static void Apply(Border border)
    {
        if (border.ActualHeight > 0) border.CornerRadius = new CornerRadius(border.ActualHeight / 2);
    }
}
