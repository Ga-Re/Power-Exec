using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using PowerExec.Core.Config;
using PowerExec.Desktop.Services;

namespace PowerExec.Desktop.Controls;

/// <summary>
/// Zeigt ein Symbol aus <see cref="IconRef"/>-Text an: Fluent (gefüllter Pfad), Heroicons (Linien),
/// Segoe-Glyphe, Emoji oder Bilddatei. Die Farbe folgt <see cref="Control.Foreground"/>.
/// </summary>
public sealed class IconPresenter : ContentControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(IconPresenter), new PropertyMetadata(string.Empty, (d, _) => ((IconPresenter)d).Update()));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(IconPresenter), new PropertyMetadata(20.0, (d, _) => ((IconPresenter)d).Update()));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public IconPresenter()
    {
        Focusable = false;
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
    }

    private void Update()
    {
        var icon = IconRef.Parse(Icon);
        Width = Size;
        Height = Size;
        Content = icon.Source switch
        {
            IconSource.Fluent or IconSource.Hero => PathFor(icon),
            IconSource.Glyph => Glyph(icon.Value),
            IconSource.Emoji => new TextBlock
            {
                Text = icon.Value,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = Size * 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            IconSource.File => IconLibrary.ImageOf(icon.Value) is { } img
                ? new Image { Source = img, Width = Size, Height = Size, Stretch = Stretch.Uniform }
                : null,
            _ => null
        };
    }

    private object? PathFor(IconRef icon)
    {
        var geometry = IconLibrary.GeometryOf(icon);
        if (geometry == null) return null;
        var path = new Path { Data = geometry, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
        // Pfaddaten haben ein 24×24-Raster; Viewbox hält den Maßstab samt Rand
        var canvas = new Canvas { Width = 24, Height = 24 };
        path.Stretch = Stretch.None;
        if (icon.Source == IconSource.Hero)
        {
            path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = this });
            path.StrokeThickness = 1.5;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
            path.StrokeLineJoin = PenLineJoin.Round;
        }
        else
        {
            path.SetBinding(Shape.FillProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = this });
        }
        canvas.Children.Add(path);
        return new Viewbox { Child = canvas, Width = Size, Height = Size, Stretch = Stretch.Uniform };
    }

    private TextBlock Glyph(string code)
    {
        var text = int.TryParse(code, System.Globalization.NumberStyles.HexNumber, null, out var cp) ? char.ConvertFromUtf32(cp) : code;
        return new TextBlock
        {
            Text = text,
            FontFamily = TryFindResource("IconFont") as FontFamily ?? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = Size * 0.85,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
