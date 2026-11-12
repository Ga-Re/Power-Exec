using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PowerExec.Desktop.Infrastructure;

namespace PowerExec.Desktop.Controls;

/// <summary>
/// Lücke für das Verschieben von Gruppen: geschlossen 0 px hoch, geöffnet ein gestrichelter Platzhalter mit Farbpunkt
/// und Namen der gezogenen Gruppe. Öffnet und schließt sich weich (<see cref="Animate"/>) oder sofort.
/// Nimmt Ablegen an (<see cref="UIElement.AllowDrop"/>), damit der Mauszeiger über der offenen Lücke das Ziel behält.
/// Aussehen: Vorlage in <c>Themes/Controls.xaml</c>.
/// </summary>
public sealed class DropGap : Control
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(DropGap), new PropertyMetadata(false, (d, _) => ((DropGap)d).UpdateHeight()));

    public static readonly DependencyProperty AnimateProperty = DependencyProperty.Register(
        nameof(Animate), typeof(bool), typeof(DropGap), new PropertyMetadata(true));

    public static readonly DependencyProperty OpenHeightProperty = DependencyProperty.Register(
        nameof(OpenHeight), typeof(double), typeof(DropGap), new PropertyMetadata(36.0, (d, _) => ((DropGap)d).UpdateHeight()));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(DropGap), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(
        nameof(DotBrush), typeof(Brush), typeof(DropGap), new PropertyMetadata(null));

    static DropGap() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DropGap), new FrameworkPropertyMetadata(typeof(DropGap)));

    public DropGap() => Height = 0;

    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public bool Animate { get => (bool)GetValue(AnimateProperty); set => SetValue(AnimateProperty, value); }
    public double OpenHeight { get => (double)GetValue(OpenHeightProperty); set => SetValue(OpenHeightProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Brush? DotBrush { get => (Brush?)GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }

    private void UpdateHeight()
    {
        var to = IsOpen ? OpenHeight : 0;
        if (Animate && SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(HeightProperty, new DoubleAnimation(to, Tokens.MotionFast)
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
