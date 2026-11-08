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
/// Mit ausgeschalteten Animationen (Einstellungen von Power-Exec) springt alles ohne Bewegung.
/// </summary>
public static class NavigationMotion
{
    private static readonly Duration ExpandDuration = new(TimeSpan.FromMilliseconds(220));
    private static readonly Duration MoveDuration = new(TimeSpan.FromMilliseconds(260));

    /// <summary>Animationen nach der Einstellung von Power-Exec (Einstellungen › Verhalten › Animation).</summary>
    private static bool Animate => Views.WindowHelper.Settings?.Invoke() is not { } s || s.Animation != Core.Config.AnimationMode.Off;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NavigationView, IndicatorAdorner> Indicators = new();

    /// <summary>Für den Selbsttest: aktuelle Höhe der Unterpunkte (0 = nicht zu sehen).</summary>
    internal static double ChildrenHeightForTest(NavigationViewItem item) =>
        item.Template?.FindName("MenuItemsPresenter", item) is FrameworkElement p && p.IsVisible ? p.ActualHeight : 0;

    /// <summary>Für den Selbsttest: woher die Sichtbarkeit der Unterpunkte kommt (Fehlersuche).</summary>
    internal static string ChildrenStateForTest(NavigationViewItem item) =>
        item.Template?.FindName("MenuItemsPresenter", item) is FrameworkElement p
            ? $"{p.Visibility}/{DependencyPropertyHelper.GetValueSource(p, UIElement.VisibilityProperty).BaseValueSource}" +
              $"{(DependencyPropertyHelper.GetValueSource(p, UIElement.VisibilityProperty).IsAnimated ? "+Animation" : "")}, {p.ActualHeight:0} px"
            : "nicht gefunden";

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
        void TakeOver()
        {
            if (!Animate || item.Template?.FindName("MenuItemsPresenter", item) is not FrameworkElement presenter) return;
            HoldVisible(presenter);
            presenter.ClipToBounds = true;
            if (!item.IsExpanded) HoldHeight(presenter, 0);
        }
        if (item.IsLoaded) TakeOver();
        item.Loaded += (_, _) => TakeOver();
        DependencyPropertyDescriptor.FromProperty(NavigationViewItem.IsExpandedProperty, typeof(NavigationViewItem))
            ?.AddValueChanged(item, (_, _) => AnimateExpand(item));
    }

    /// <summary>
    /// Unterpunkte dauerhaft sichtbar halten – per Animation, denn Animationen gehen in WPF vor lokalen Werten und
    /// Vorlagen-Triggern. Ob sie zu sehen sind, bestimmt danach allein die Höhe (zugeklappt: 0).
    /// </summary>
    private static void HoldVisible(FrameworkElement presenter)
    {
        var hold = new ObjectAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
        hold.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Visible, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        presenter.BeginAnimation(UIElement.VisibilityProperty, hold);
        presenter.SetValue(UIElement.OpacityProperty, 1.0);
    }

    private static void HoldHeight(FrameworkElement presenter, double height) =>
        presenter.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(height, height, new Duration(TimeSpan.Zero)) { FillBehavior = FillBehavior.HoldEnd });

    /// <summary>
    /// Unterpunkte weich auf- bzw. zuklappen: die Höhe wächst von 0 auf die volle Höhe (bzw. schrumpft auf 0). Während
    /// der Bewegung bleibt die Liste lokal sichtbar (der Trigger von WPF UI blendet sonst sofort aus).
    /// </summary>
    private static void AnimateExpand(NavigationViewItem item)
    {
        if (!Animate || !item.IsLoaded || item.Template?.FindName("MenuItemsPresenter", item) is not FrameworkElement presenter) return;
        HoldVisible(presenter);
        presenter.ClipToBounds = true;
        var expand = item.IsExpanded;
        var current = presenter.ActualHeight; // unterwegs umgeschaltet: von hier weiter
        // Natürliche Höhe am Inhalt messen – die gehaltene Höhe der Liste bleibt dabei stehen (kein Aufblitzen)
        var width = presenter.ActualWidth > 0 ? presenter.ActualWidth : item.ActualWidth;
        double full;
        if (VisualTreeHelper.GetChildrenCount(presenter) > 0 && VisualTreeHelper.GetChild(presenter, 0) is UIElement content)
        {
            content.Measure(new Size(width, double.PositiveInfinity));
            full = content.DesiredSize.Height;
        }
        else
        {
            presenter.BeginAnimation(FrameworkElement.HeightProperty, null);
            presenter.Measure(new Size(width, double.PositiveInfinity));
            full = presenter.DesiredSize.Height;
        }
        if (full <= 0) { if (!expand) HoldHeight(presenter, 0); return; }
        var from = Math.Clamp(current, 0, full);
        var animation = new DoubleAnimation(from, expand ? full : 0, ExpandDuration)
        {
            EasingFunction = new CubicEase { EasingMode = expand ? EasingMode.EaseOut : EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };
        if (expand)
        {
            // Aufgeklappt wieder automatische Höhe (neue oder ausgeblendete Unterpunkte passen dann von selbst)
            animation.Completed += (_, _) =>
            {
                if (item.IsExpanded) presenter.BeginAnimation(FrameworkElement.HeightProperty, null);
            };
        }
        presenter.BeginAnimation(FrameworkElement.HeightProperty, animation);
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
