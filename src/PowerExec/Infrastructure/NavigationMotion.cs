using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace PowerExec.Desktop.Infrastructure;

/// <summary>
/// Bewegung für die Navigationsleiste (WPF UI) wie in den Windows-Einstellungen:
/// <list type="bullet">
/// <item>Oberkategorien klappen weich auf und zu (WPF UI blendet die Unterpunkte nur ein; die Höhe springt).</item>
/// <item>Ein einzelner Auswahlbalken gleitet zum neuen Menüpunkt, statt an einer Stelle zu verschwinden und an der
/// anderen aufzutauchen. Ist die Oberkategorie des ausgewählten Punkts zugeklappt, steht er an der Oberkategorie.</item>
/// </list>
/// Die Vorlagen von WPF UI bleiben unverändert: die eigenen Balken der Einträge werden nur unsichtbar gemacht.
/// Dazu Seitenwechsel (<see cref="SwapPages"/>), gestaffeltes Einblenden (<see cref="Stagger"/>), weiches Hervorheben
/// (<see cref="FadeToProperty"/>) und gleitende Kacheln nach einem Umbruch (<see cref="Glide"/>).
/// Mit ausgeschalteten Animationen (Einstellungen von Power-Exec oder Windows) springt alles ohne Bewegung.
/// </summary>
public static class NavigationMotion
{
    private static readonly Duration ExpandDuration = new(Tokens.MotionNormal);
    private static readonly Duration MoveDuration = new(Tokens.MotionNormal);

    /// <summary>Animationen nach der Einstellung von Power-Exec (Einstellungen › Verhalten › Animation).</summary>
    private static bool Animate => Views.WindowHelper.Settings?.Invoke() is not { } s || s.Animation != Core.Config.AnimationMode.Off;

    /// <summary>
    /// Bewegung erlaubt? Einstellung von Power-Exec und von Windows („Animationseffekte“,
    /// <see cref="SystemParameters.ClientAreaAnimation"/>). Sonst wird sofort umgeschaltet.
    /// </summary>
    public static bool Enabled => Animate && SystemParameters.ClientAreaAnimation;

    // ---- Seitenwechsel, gestaffeltes Einblenden, Hervorheben, Umbruch ----------------------------

    /// <summary>So weit weicht die alte Seite aus bzw. kommt die Seite beim Zurückgehen herein (DIP).</summary>
    private const double PageOutShift = 16;
    /// <summary>So weit gleitet die neue Seite beim Öffnen herein (DIP).</summary>
    private const double PageInShift = 20;
    /// <summary>So weit gleiten die Karten einer Seite beim gestaffelten Einblenden nach oben (DIP).</summary>
    private const double StaggerShift = 10;

    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction EaseIn = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });

    private static IEasingFunction Frozen(CubicEase ease)
    {
        ease.Freeze();
        return ease;
    }

    /// <summary>Soll die Seite (noch) zu sehen sein? Ein unterbrochenes Ausblenden versteckt sie dann nicht mehr.</summary>
    private static readonly DependencyProperty ShowingProperty = DependencyProperty.RegisterAttached(
        "Showing", typeof(bool), typeof(NavigationMotion), new PropertyMetadata(true));

    /// <summary>
    /// Von einer Seite zur anderen wechseln: die alte blendet aus und weicht leicht zur Seite, die neue blendet ein und
    /// gleitet an ihren Platz – vorwärts von rechts, zurück von links (etwas schneller). Beide liegen übereinander.
    /// </summary>
    public static void SwapPages(FrameworkElement from, FrameworkElement to, bool forward)
    {
        from.SetValue(ShowingProperty, false);
        to.SetValue(ShowingProperty, true);
        to.IsHitTestVisible = true;
        to.Visibility = Visibility.Visible;
        if (!Enabled || !from.IsVisible)
        {
            Reset(from);
            Reset(to);
            from.Visibility = Visibility.Collapsed;
            return;
        }
        from.IsHitTestVisible = false;
        var outDuration = new Duration(Tokens.MotionPageOut);
        var fade = new DoubleAnimation(0, outDuration) { EasingFunction = EaseIn, FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) =>
        {
            if ((bool)from.GetValue(ShowingProperty)) return; // inzwischen wieder gezeigt
            from.Visibility = Visibility.Collapsed;
            from.IsHitTestVisible = true;
            Reset(from);
        };
        Shift(from).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(forward ? -PageOutShift : PageOutShift, outDuration) { EasingFunction = EaseIn, FillBehavior = FillBehavior.HoldEnd });
        from.BeginAnimation(UIElement.OpacityProperty, fade);
        FadeIn(to, forward ? Tokens.MotionPageIn : Tokens.MotionPageBack, TimeSpan.Zero, forward ? PageInShift : -PageOutShift, 0);
    }

    /// <summary>Teile einer Seite nacheinander einblenden (je leicht von unten), z. B. Kopf, dann die Karten.</summary>
    public static void Stagger(IEnumerable<UIElement> parts)
    {
        if (!Enabled) return;
        var i = 0;
        foreach (var part in parts.Where(p => p.Visibility == Visibility.Visible))
            FadeIn(part, Tokens.MotionPageIn, TimeSpan.FromTicks(Tokens.MotionStagger.Ticks * i++), 0, StaggerShift);
    }

    /// <summary>
    /// Element blendet weich ein (leicht von unten), sobald es sichtbar wird – z. B. ein Abschnitt nach dem Wechsel in der
    /// Navigation oder eine neue Karte in einer Liste. Ohne Animationen: sofort.
    /// </summary>
    public static readonly DependencyProperty AppearProperty = DependencyProperty.RegisterAttached(
        "Appear", typeof(bool), typeof(NavigationMotion), new PropertyMetadata(false, OnAppearChanged));

    public static bool GetAppear(DependencyObject element) => (bool)element.GetValue(AppearProperty);
    public static void SetAppear(DependencyObject element, bool value) => element.SetValue(AppearProperty, value);

    private static void OnAppearChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.IsVisibleChanged -= OnAppearVisible;
        element.Loaded -= OnAppearLoaded;
        if (!(bool)e.NewValue) return;
        element.IsVisibleChanged += OnAppearVisible;
        element.Loaded += OnAppearLoaded;
    }

    private static void OnAppearVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is FrameworkElement { IsLoaded: true } element) Appear(element);
    }

    private static void OnAppearLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { IsVisible: true } element) Appear(element);
    }

    private static void Appear(UIElement element)
    {
        if (!Enabled) return;
        FadeIn(element, Tokens.MotionPageIn, TimeSpan.Zero, 0, StaggerShift);
    }

    /// <summary>
    /// Deckkraft, die ein Element weich ansteuert (<see cref="Tokens.MotionFast"/>) – für Hervorhebungen per Trigger,
    /// z. B. die gewählte Variante hell/dunkel. Ohne Animationen oder solange das Element nicht zu sehen ist: sofort.
    /// </summary>
    public static readonly DependencyProperty FadeToProperty = DependencyProperty.RegisterAttached(
        "FadeTo", typeof(double), typeof(NavigationMotion), new PropertyMetadata(1.0, OnFadeTo));

    public static double GetFadeTo(DependencyObject element) => (double)element.GetValue(FadeToProperty);
    public static void SetFadeTo(DependencyObject element, double value) => element.SetValue(FadeToProperty, value);

    private static void OnFadeTo(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        var to = (double)e.NewValue;
        var from = element.Opacity; // auch mitten in einer laufenden Bewegung
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = to;
        if (!Enabled || !element.IsVisible || Math.Abs(from - to) < 0.001) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, to, new Duration(Tokens.MotionFast)) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>
    /// Element gleitet von seinem bisherigen Platz an den neuen: <paramref name="offset"/> = alter minus neuer Platz.
    /// Eine laufende Bewegung wird vom aktuellen Stand aus fortgesetzt.
    /// </summary>
    public static void Glide(UIElement element, Vector offset)
    {
        var t = Shift(element);
        var (x, y) = (offset.X + t.X, offset.Y + t.Y); // t.X/Y: aktueller Stand einer laufenden Bewegung
        var duration = new Duration(Tokens.MotionReflow);
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, 0, duration) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, 0, duration) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>Einblenden ab <paramref name="delay"/>: Deckkraft 0 → 1, Versatz (<paramref name="x"/>, <paramref name="y"/>) → 0.</summary>
    private static void FadeIn(UIElement element, TimeSpan duration, TimeSpan delay, double x, double y)
    {
        // Schlüsselbilder statt BeginTime: bis zum Start bleibt das Element unsichtbar statt kurz aufzublitzen
        static DoubleAnimationUsingKeyFrames Frames(double from, double to, TimeSpan delay, TimeSpan duration)
        {
            var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (delay > TimeSpan.Zero) a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(delay)));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(delay + duration), EaseOut));
            return a;
        }
        element.Opacity = 1;
        element.BeginAnimation(UIElement.OpacityProperty, Frames(0, 1, delay, duration));
        var t = Shift(element);
        t.X = 0;
        t.Y = 0;
        t.BeginAnimation(TranslateTransform.XProperty, x == 0 ? null : Frames(x, 0, delay, duration));
        t.BeginAnimation(TranslateTransform.YProperty, y == 0 ? null : Frames(y, 0, delay, duration));
    }

    /// <summary>Bewegung beenden: ganz sichtbar, am eigenen Platz.</summary>
    private static void Reset(UIElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        if (element.RenderTransform is TranslateTransform { IsFrozen: false } t)
        {
            t.BeginAnimation(TranslateTransform.XProperty, null);
            t.BeginAnimation(TranslateTransform.YProperty, null);
            t.X = 0;
            t.Y = 0;
        }
    }

    /// <summary>Verschiebung des Elements (wird bei Bedarf angelegt).</summary>
    private static TranslateTransform Shift(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform { IsFrozen: false } t) return t;
        t = new TranslateTransform();
        element.RenderTransform = t;
        return t;
    }

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
            // Akzentbalken wie in den Windows-11-Einstellungen: 3 px breit, in der Akzentfarbe (auch die von Windows)
            var brush = _nav.TryFindResource("Accent") as Brush
                        ?? _nav.TryFindResource("NavigationViewSelectionIndicatorForeground") as Brush
                        ?? _nav.TryFindResource("SystemAccentColorPrimaryBrush") as Brush ?? Brushes.DodgerBlue;
            var height = Math.Max(18, t.Height);
            dc.DrawRoundedRectangle(brush, null, new Rect(BarLeft, BarTop + (t.Height - height) / 2, 3, height), 1.5, 1.5);
        }
    }
}
