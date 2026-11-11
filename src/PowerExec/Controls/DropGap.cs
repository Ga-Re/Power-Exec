using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace PowerExec.Desktop.Controls;

/// <summary>
/// Lücke für das Verschieben von Gruppen: geschlossen 0 px hoch, geöffnet ein gestrichelter Platzhalter mit Farbpunkt
/// und Namen der gezogenen Gruppe. Öffnet und schließt sich weich (<see cref="Animate"/>) oder sofort.
/// Nimmt Ablegen an (<see cref="UIElement.AllowDrop"/>), damit der Mauszeiger über der offenen Lücke das Ziel behält.
/// </summary>
public sealed class DropGap : ContentControl
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(DropGap), new PropertyMetadata(false, (d, _) => ((DropGap)d).UpdateHeight()));

    public static readonly DependencyProperty AnimateProperty = DependencyProperty.Register(
        nameof(Animate), typeof(bool), typeof(DropGap), new PropertyMetadata(true));

    public static readonly DependencyProperty OpenHeightProperty = DependencyProperty.Register(
        nameof(OpenHeight), typeof(double), typeof(DropGap), new PropertyMetadata(36.0, (d, _) => ((DropGap)d).UpdateHeight()));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(DropGap), new PropertyMetadata(string.Empty, (d, e) => ((DropGap)d)._label.Text = (string)e.NewValue ?? string.Empty));

    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(
        nameof(DotBrush), typeof(Brush), typeof(DropGap), new PropertyMetadata(null, (d, e) => ((DropGap)d)._dot.Fill = e.NewValue as Brush));

    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public bool Animate { get => (bool)GetValue(AnimateProperty); set => SetValue(AnimateProperty, value); }
    public double OpenHeight { get => (double)GetValue(OpenHeightProperty); set => SetValue(OpenHeightProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Brush? DotBrush { get => (Brush?)GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }

    private readonly TextBlock _label = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Ellipse _dot = new() { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

    public DropGap()
    {
        Focusable = false;
        IsTabStop = false;
        AllowDrop = true;
        ClipToBounds = true;
        Height = 0;
        IsHitTestVisible = true;

        var frame = new Rectangle { RadiusX = 6, RadiusY = 6, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } };
        frame.SetResourceReference(Shape.StrokeProperty, "Accent");
        frame.SetResourceReference(Shape.FillProperty, "Bg.Selected");
        _label.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 0) };
        row.Children.Add(_dot);
        row.Children.Add(_label);
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.Children.Add(frame);
        grid.Children.Add(row);
        Content = grid;
    }

    private void UpdateHeight()
    {
        var to = IsOpen ? OpenHeight : 0;
        if (Animate && SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(HeightProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }
        else
        {
            BeginAnimation(HeightProperty, null);
            Height = to;
        }
    }
}
