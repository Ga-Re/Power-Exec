using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using PowerExec.Core.Config;
using PowerExec.Interop;

namespace PowerExec.Views;

/// <summary>
/// Schild am Mauszeiger beim Ziehen einer Gruppe: Name, Farbe und Zahl der Untergruppen (Option „Ziehbild“)
/// und an ungültigen Stellen die Begründung (Option „Begründung an ungültigen Stellen“).
/// Liegt versetzt neben dem Mauszeiger, damit es das Ablegen nicht abfängt.
/// </summary>
internal sealed class DragFeedback : IDisposable
{
    private readonly FrameworkElement _owner;
    private readonly Popup _popup;
    private readonly FrameworkElement _nameRow;
    private readonly TextBlock _reason;
    private readonly bool _ghost, _explain;
    private bool _invalid;

    public DragFeedback(FrameworkElement owner, string label, Brush? dot, bool ghost, bool explain)
    {
        _owner = owner;
        _ghost = ghost;
        _explain = explain;

        var dotShape = new Ellipse { Width = 10, Height = 10, Fill = dot, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        if (dot == null) dotShape.SetResourceReference(Shape.FillProperty, "Text.Tertiary");
        var name = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Text.Primary");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dotShape);
        row.Children.Add(name);
        _nameRow = row;

        _reason = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 280, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        _reason.SetResourceReference(TextBlock.ForegroundProperty, "Status.Error");

        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(_reason);
        var border = new Border
        {
            Child = stack, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.3 }
        };
        border.SetResourceReference(Border.BackgroundProperty, "Bg.Card");
        border.SetResourceReference(Border.BorderBrushProperty, "Border.Default");

        _popup = new Popup
        {
            Child = border, AllowsTransparency = true, Placement = PlacementMode.AbsolutePoint,
            IsHitTestVisible = false, Focusable = false, StaysOpen = true
        };
        Refresh();
    }

    /// <summary>Begründung zeigen bzw. ausblenden.</summary>
    public void Update(GroupDropPlan? plan)
    {
        _invalid = plan is { IsValid: false };
        _reason.Text = _invalid ? plan!.Reason ?? "Hier nicht möglich." : string.Empty;
        Refresh();
        Move();
    }

    private void Refresh()
    {
        bool showReason = _explain && _invalid;
        _nameRow.Visibility = _ghost ? Visibility.Visible : Visibility.Collapsed;
        _reason.Visibility = showReason ? Visibility.Visible : Visibility.Collapsed;
        _reason.Margin = new Thickness(0, _ghost ? 4 : 0, 0, 0);
        _popup.IsOpen = _ghost || showReason;
    }

    /// <summary>Neben den Mauszeiger setzen (Bildschirmkoordinaten in geräteunabhängige Einheiten umrechnen).</summary>
    public void Move()
    {
        if (!_popup.IsOpen || !NativeMethods.GetCursorPos(out var pt)) return;
        var source = PresentationSource.FromVisual(_owner);
        var p = source?.CompositionTarget is { } target ? target.TransformFromDevice.Transform(new Point(pt.X, pt.Y)) : new Point(pt.X, pt.Y);
        _popup.HorizontalOffset = p.X + 16;
        _popup.VerticalOffset = p.Y + 14;
    }

    public void Dispose() => _popup.IsOpen = false;
}
