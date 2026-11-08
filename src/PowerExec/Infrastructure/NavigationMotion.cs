using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace PowerExec.Infrastructure;

/// <summary>
/// Bewegung für die Navigationsleiste (WPF UI) wie in den Windows-Einstellungen:
/// <list type="bullet">
/// <item>Oberkategorien klappen weich auf und zu (WPF UI blendet die Unterpunkte nur ein; die Höhe springt).</item>
/// <item>Ein einzelner Auswahlbalken gleitet zum neuen Menüpunkt, statt an einer Stelle zu verschwinden und an der
/// anderen aufzutauchen. Ist die Oberkategorie des ausgewählten Punkts zugeklappt, steht er an der Oberkategorie.</item>
/// </list>
/// Die Vorlagen von WPF UI bleiben unverändert: die eigenen Balken der Einträge werden nur unsichtbar gemacht.
/// Mit ausgeschalteten Animationen in Windows springt alles ohne Bewegung.
/// </summary>
public static class NavigationMotion
{
    private static readonly Duration ExpandDuration = new(TimeSpan.FromMilliseconds(220));
    private static readonly Duration MoveDuration = new(TimeSpan.FromMilliseconds(260));

    private static bool Animate => SystemParameters.ClientAreaAnimation;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NavigationView, IndicatorAdorner> Indicators = new();

    /// <summary>Für den Selbsttest: wo der Balken hin soll (null = nirgends) und wo er gerade steht (oben).</summary>
    internal static (Rect? Target, double Top)? IndicatorForTest(NavigationView nav) =>
        Indicators.TryGetValue(nav, out var a) ? (Target(nav), a.BarTop) : null;

    public static void Attach(NavigationView nav)
    {
        IndicatorAdorner? adorner = null;
        void Setup()
        {
            if (adorner != null) return;
            if (AdornerLayer.GetAdornerLayer(nav) is not { } layer) return;
            adorner = new IndicatorAdorner(nav);
            layer.Add(adorner);
            Indicators.AddOrUpdate(nav, adorner);
            foreach (var item in AllItems(nav)) Prepare(item);
            adorner.MoveTo(Target(nav), animate: false);
        }

        nav.Loaded += (_, _) => Setup();
        nav.SelectionChanged += (_, _) => nav.Dispatcher.BeginInvoke(() => adorner?.MoveTo(Target(nav), Animate),
            System.Windows.Threading.DispatcherPriority.Loaded);
        // Scrollen, Aufklappen, Ein-/Ausklappen des Rands: der Balken bleibt am Eintrag
        nav.LayoutUpdated += (_, _) => adorner?.Follow(Target(nav));
    }

    private static IEnumerable<NavigationViewItem> AllItems(NavigationView nav)
    {
        foreach (var top in nav.MenuItems.OfType<NavigationViewItem>())
        {
            yield return top;
            foreach (var child in top.MenuItems.OfType<NavigationViewItem>()) yield return child;
        }
    }

    /// <summary>Eigenen Balken des Eintrags ausblenden und – bei Oberkategorien – das Aufklappen beobachten.</summary>
    private static void Prepare(NavigationViewItem item)
    {
        void HideOwnBar()
        {
            item.ApplyTemplate();
            if (item.Template?.FindName("ActiveRectangle", item) is UIElement bar) bar.SetValue(UIElement.OpacityProperty, 0.0); // lokal: schlägt den Trigger
        }
        if (item.IsLoaded) HideOwnBar();
        item.Loaded += (_, _) => HideOwnBar();

        if (item.MenuItems.Count == 0) return;
        DependencyPropertyDescriptor.FromProperty(NavigationViewItem.IsExpandedProperty, typeof(NavigationViewItem))
            ?.AddValueChanged(item, (_, _) => AnimateExpand(item));
    }

    private static void AnimateExpand(NavigationViewItem item)
    {
        if (!Animate || item.Template?.FindName("MenuItemsPresenter", item) is not FrameworkElement presenter) return;
        var scale = presenter.LayoutTransform as ScaleTransform;
        if (scale == null || scale.IsFrozen) presenter.LayoutTransform = scale = new ScaleTransform(1, 1);
        var expand = item.IsExpanded;
        // Beim Zuklappen sichtbar halten, bis die Bewegung fertig ist (der Trigger von WPF UI blendet sofort aus)
        if (!expand) presenter.SetValue(UIElement.VisibilityProperty, Visibility.Visible);
        var from = scale.ScaleY;
        if (expand && from >= 1) from = 0;
        var animation = new DoubleAnimation(from, expand ? 1 : 0, ExpandDuration)
        {
            EasingFunction = new CubicEase { EasingMode = expand ? EasingMode.EaseOut : EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            if (item.IsExpanded != expand) return; // inzwischen wieder umgeschaltet
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleY = 1;
            presenter.ClearValue(UIElement.VisibilityProperty);
        };
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    /// <summary>
    /// Wo der Balken stehen soll: am ausgewählten Eintrag oder – wenn der nicht zu sehen ist (Oberkategorie zugeklappt,
    /// Rand eingeklappt) – an seiner Oberkategorie. null = nirgends.
    /// </summary>
    private static Rect? Target(NavigationView nav)
    {
        if (nav.SelectedItem is not NavigationViewItem selected) return null;
        var item = selected;
        if (!IsShown(item, nav))
        {
            item = nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(p => p.MenuItems.Contains(selected)) ?? selected;
            if (!IsShown(item, nav)) return null;
        }
        if (item.Template?.FindName("ActiveRectangle", item) is not FrameworkElement bar || !bar.IsVisible) return null;
        try
        {
            var topLeft = bar.TranslatePoint(new Point(0, 0), nav);
            var rect = new Rect(topLeft, new Size(bar.ActualWidth, bar.ActualHeight));
            // Außerhalb des sichtbaren Bereichs der Leiste (weggescrollt): nicht zeichnen
            if (FindScrollViewer(item) is { } scroller)
            {
                var view = new Rect(scroller.TranslatePoint(new Point(0, 0), nav), new Size(scroller.ViewportWidth, scroller.ViewportHeight));
                if (!view.IntersectsWith(rect)) return null;
            }
            return rect;
        }
        catch (InvalidOperationException) { return null; } // nicht (mehr) im Baum
    }

    private static bool IsShown(FrameworkElement e, NavigationView nav)
    {
        if (!e.IsVisible || e.ActualHeight < 1) return false;
        // Unterpunkte einer zugeklappten (oder gerade zuklappenden) Oberkategorie zählen nicht
        for (DependencyObject? d = VisualTreeHelper.GetParent(e); d != null && d != nav; d = VisualTreeHelper.GetParent(d))
            if (d is NavigationViewItem parent && parent != e && !parent.IsExpanded) return false;
        return true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject e)
    {
        for (var d = VisualTreeHelper.GetParent(e); d != null; d = VisualTreeHelper.GetParent(d))
            if (d is ScrollViewer s) return s;
        return null;
    }

    /// <summary>Der eine, gleitende Auswahlbalken über der Navigationsleiste.</summary>
    private sealed class IndicatorAdorner : Adorner
    {
        public static readonly DependencyProperty BarTopProperty = DependencyProperty.Register(nameof(BarTop), typeof(double), typeof(IndicatorAdorner),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BarLeftProperty = DependencyProperty.Register(nameof(BarLeft), typeof(double), typeof(IndicatorAdorner),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private readonly NavigationView _nav;
        private Rect? _target;
        private bool _moving;

        public IndicatorAdorner(NavigationView nav) : base(nav)
        {
            _nav = nav;
            IsHitTestVisible = false;
        }

        public double BarTop { get => (double)GetValue(BarTopProperty); set => SetValue(BarTopProperty, value); }
        public double BarLeft { get => (double)GetValue(BarLeftProperty); set => SetValue(BarLeftProperty, value); }

        public void MoveTo(Rect? target, bool animate)
        {
            var from = _target;
            _target = target;
            if (target is not { } t) { InvalidateVisual(); return; }
            if (!animate || from == null)
            {
                Stop();
                BarTop = t.Top;
                BarLeft = t.Left;
                InvalidateVisual();
                return;
            }
            _moving = true;
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var top = new DoubleAnimation(BarTop, t.Top, MoveDuration) { EasingFunction = ease };
            top.Completed += (_, _) =>
            {
                _moving = false;
                Stop();
                if (_target is { } latest) { BarTop = latest.Top; BarLeft = latest.Left; }
                InvalidateVisual();
            };
            BeginAnimation(BarLeftProperty, new DoubleAnimation(BarLeft, t.Left, MoveDuration) { EasingFunction = ease });
            BeginAnimation(BarTopProperty, top);
        }

        /// <summary>Nach einer Layoutänderung: ohne Bewegung an den Eintrag anlegen (während des Gleitens nur das Ziel merken).</summary>
        public void Follow(Rect? target)
        {
            if (target == _target) return;
            if (_moving) { _target = target; return; }
            MoveTo(target, animate: false);
        }

        private void Stop()
        {
            BeginAnimation(BarTopProperty, null);
            BeginAnimation(BarLeftProperty, null);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_target is not { } t) return;
            var brush = _nav.TryFindResource("NavigationViewSelectionIndicatorForeground") as Brush
                        ?? _nav.TryFindResource("SystemAccentColorPrimaryBrush") as Brush ?? Brushes.DodgerBlue;
            dc.DrawRoundedRectangle(brush, null, new Rect(BarLeft, BarTop, Math.Max(3, t.Width), Math.Max(16, t.Height)), 2, 2);
        }
    }
}
