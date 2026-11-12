using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PowerExec.Desktop.Infrastructure;

namespace PowerExec.Desktop.Controls;

/// <summary>
/// Klappt seinen Inhalt weich auf und zu (Höhe 0 ↔ volle Höhe). Zugeklappt ist der Inhalt <see cref="Visibility.Collapsed"/>,
/// damit WPF für eingeklappte Gruppen keine Container erzeugt und misst. Animiert wird nur, wenn <see cref="Animate"/>
/// beim Umschalten gesetzt ist (Klick auf den Gruppenkopf) – nicht beim Suchen oder „Alle ausklappen“.
/// </summary>
public sealed class CollapsePanel : Decorator
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(CollapsePanel), new PropertyMetadata(true, (d, e) => ((CollapsePanel)d).OnExpandedChanged((bool)e.NewValue)));

    public static readonly DependencyProperty AnimateProperty = DependencyProperty.Register(
        nameof(Animate), typeof(bool), typeof(CollapsePanel), new PropertyMetadata(false));

    /// <summary>0 = zu, 1 = offen (wird animiert).</summary>
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(CollapsePanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Ändert sich dieser Wert (z. B. Liste ↔ Raster) und ist <see cref="AnimateResize"/> gesetzt, gleitet die Höhe zur neuen.</summary>
    public static readonly DependencyProperty TransitionKeyProperty = DependencyProperty.Register(
        nameof(TransitionKey), typeof(object), typeof(CollapsePanel), new PropertyMetadata(null, (d, _) => ((CollapsePanel)d).OnTransitionKeyChanged()));

    public static readonly DependencyProperty AnimateResizeProperty = DependencyProperty.Register(
        nameof(AnimateResize), typeof(bool), typeof(CollapsePanel), new PropertyMetadata(false));

    /// <summary>Während einer Größenänderung: angezeigte Höhe (NaN = Inhalt bestimmt die Höhe).</summary>
    public static readonly DependencyProperty ResizeHeightProperty = DependencyProperty.Register(
        nameof(ResizeHeight), typeof(double), typeof(CollapsePanel), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public object? TransitionKey { get => GetValue(TransitionKeyProperty); set => SetValue(TransitionKeyProperty, value); }
    public bool AnimateResize { get => (bool)GetValue(AnimateResizeProperty); set => SetValue(AnimateResizeProperty, value); }
    public double ResizeHeight { get => (double)GetValue(ResizeHeightProperty); set => SetValue(ResizeHeightProperty, value); }

    private double? _resizeFrom;

    private void OnTransitionKeyChanged()
    {
        if (!AnimateResize || !IsLoaded || !IsExpanded || !SystemParameters.ClientAreaAnimation || ActualHeight <= 0) return;
        // Alte Höhe merken; die Animation startet, sobald die neue Höhe beim nächsten Messen feststeht
        _resizeFrom = double.IsNaN(ResizeHeight) ? ActualHeight : ResizeHeight;
        InvalidateMeasure();
    }

    private void StartResize(double from, double to)
    {
        if (Math.Abs(from - to) < 1) return;
        var anim = new DoubleAnimation(from, to, Duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        anim.Completed += (_, _) => { BeginAnimation(ResizeHeightProperty, null); ResizeHeight = double.NaN; };
        BeginAnimation(ResizeHeightProperty, anim);
    }

    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public bool Animate { get => (bool)GetValue(AnimateProperty); set => SetValue(AnimateProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public static TimeSpan Duration => Tokens.MotionFast;

    public CollapsePanel()
    {
        ClipToBounds = true;
    }

    public override UIElement? Child
    {
        get => base.Child;
        set
        {
            base.Child = value;
            if (value != null) value.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnExpandedChanged(bool expanded)
    {
        var animate = Animate && IsLoaded && SystemParameters.ClientAreaAnimation;
        if (!animate)
        {
            BeginAnimation(ProgressProperty, null);
            Progress = expanded ? 1 : 0;
            if (Child != null) Child.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        if (Child != null) Child.Visibility = Visibility.Visible;
        var from = expanded ? 0.0 : Progress;
        var anim = new DoubleAnimation(from, expanded ? 1 : 0, Duration)
        {
            EasingFunction = new CubicEase { EasingMode = expanded ? EasingMode.EaseOut : EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            // Nur abschließen, wenn inzwischen nicht wieder umgeschaltet wurde
            if (IsExpanded != expanded) return;
            BeginAnimation(ProgressProperty, null);
            Progress = expanded ? 1 : 0;
            if (!expanded && Child != null) Child.Visibility = Visibility.Collapsed;
        };
        BeginAnimation(ProgressProperty, anim);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child == null) return new Size();
        Child.Measure(new Size(constraint.Width, double.PositiveInfinity));
        var d = Child.DesiredSize;
        if (_resizeFrom is { } from)
        {
            _resizeFrom = null;
            var to = d.Height;
            Dispatcher.BeginInvoke(() => StartResize(from, to));
            return new Size(d.Width, from);
        }
        if (!double.IsNaN(ResizeHeight)) return new Size(d.Width, ResizeHeight);
        var p = Math.Clamp(Progress, 0, 1);
        return new Size(d.Width, d.Height * p);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child == null) return arrangeSize;
        // Inhalt in voller Höhe anordnen, oben bündig; sichtbar ist nur der aufgeklappte Teil (ClipToBounds)
        Child.Arrange(new Rect(0, 0, arrangeSize.Width, Math.Max(arrangeSize.Height, Child.DesiredSize.Height)));
        return arrangeSize;
    }

    protected override Geometry? GetLayoutClip(Size layoutSlotSize) => new RectangleGeometry(new Rect(RenderSize));
}
