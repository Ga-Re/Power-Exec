using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Desktop.Interop;
using PowerExec.Desktop.Services;
using PowerExec.UI.Services;
using Rectangle = System.Drawing.Rectangle;

namespace PowerExec.Desktop.Views;

/// <summary>
/// Ein- und Ausfahren des Panels samt Absicherung der Endlage.
///
/// <para><b>Glas</b> (Fenster ohne Transparenz-Schicht): das Fenster gleitet als Ganzes vom Rand des Arbeitsbereichs.
/// Windows zeichnet den Hintergrund, WPF den Inhalt – zwei getrennte Abläufe. Ändert sich beim Gleiten die Fenstergröße
/// oder ein Fensterbereich, passt Windows den Hintergrund sofort an, WPF den Inhalt ein bis zwei Bilder später.
/// Verschiebt man dagegen nur das Fenster, bewegt Windows Inhalt und Hintergrund in einem Stück – wie beim Ziehen eines
/// Fensters. Liegt an der Kante ein Nachbarmonitor oder die Taskleiste, wird das Fenster auf dem Weg zugeschnitten
/// (Fensterbereich) und ganz eingefahren für Windows ausgeblendet.</para>
///
/// <para><b>Klarglas</b> (transparentes WPF-Fenster): das Fenster steht an seiner Ziellage, der Inhalt gleitet darin.</para>
/// </summary>
internal sealed class PanelSlideController
{
    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly bool _systemWindow;
    private readonly TranslateTransform _contentSlide;
    private readonly FrameworkElement _content;

    private Rectangle _target;
    private Rectangle _workArea;
    private ScreenEdge _edge;
    private int _cornerRadius;
    /// <summary>Eingefahrene Lage des Fensters (gleiche Größe wie das Panel).</summary>
    private Rectangle _hidden;
    /// <summary>Auf dem Weg auf den Arbeitsbereich zuschneiden: Taskleiste oder ein anderer Monitor liegt an dieser Kante.</summary>
    private bool _clip;
    /// <summary>
    /// Auch ausgefahren einen Fensterbereich behalten: ein anderer Monitor liegt in Reichweite des Fensterschattens.
    /// Ohne Fensterbereich zeichnete Windows Schatten und Rand als Streifen auf den Nachbarmonitor.
    /// </summary>
    private bool _guardShadow;
    private System.Drawing.Size _appliedSize;
    /// <summary>Gesetzter Fensterbereich (sichtbarer Teil relativ zum Fenster); null = keiner, Windows rundet die Ecken selbst.</summary>
    private Rectangle? _region;
    private bool _regionApplied;
    private bool _cloaked;

    /// <param name="window">Das Panelfenster.</param>
    /// <param name="hwnd">Sein Fenster-Handle.</param>
    /// <param name="systemWindow">Glas: das Fenster selbst gleitet; sonst gleitet der Inhalt im Fenster.</param>
    /// <param name="contentSlide">Verschiebung des Inhalts (Klarglas).</param>
    /// <param name="content">Panelinhalt (Klarglas: wird ein- und ausgeblendet).</param>
    public PanelSlideController(Window window, IntPtr hwnd, bool systemWindow, TranslateTransform contentSlide, FrameworkElement content)
    {
        _window = window;
        _hwnd = hwnd;
        _systemWindow = systemWindow;
        _contentSlide = contentSlide;
        _content = content;
        window.SetValue(OwnerProperty, this);
    }

    private static readonly DependencyProperty OwnerProperty = DependencyProperty.RegisterAttached(
        "Owner", typeof(PanelSlideController), typeof(PanelSlideController));

    /// <summary>
    /// 0 = eingefahren (hinter dem Rand des Arbeitsbereichs), 1 = ganz ausgefahren. Hängt am Fenster, damit WPF den Wert
    /// animieren kann; jede Änderung setzt das Fenster auf die passende Lage.
    /// </summary>
    private static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached(
        "Reveal", typeof(double), typeof(PanelSlideController),
        new PropertyMetadata(1.0, (d, _) => (d.GetValue(OwnerProperty) as PanelSlideController)?.Apply()));

    /// <summary>0 = eingefahren, 1 = ganz ausgefahren (nur Glas).</summary>
    public double Reveal => (double)_window.GetValue(RevealProperty);

    /// <summary>Ziellage des ausgefahrenen Fensters in Pixeln.</summary>
    public Rectangle Target => _target;

    /// <summary>Wird auf dem Weg zugeschnitten (Nachbarmonitor oder Taskleiste an der Kante)?</summary>
    public bool Clips => _clip;

    /// <summary>Eingefahrenes Fenster für Windows ausgeblendet?</summary>
    public bool IsCloaked => _cloaked;

    /// <summary>Glas: das Fenster gleitet gerade (oder steht eingefahren am Rand).</summary>
    public bool IsSliding => _systemWindow && _window.IsVisible && Reveal < 1;

    /// <summary>Weg zum Ein-/Ausfahren für die aktuelle Ziellage bestimmen (bei jedem Platzieren).</summary>
    /// <param name="target">Ziellage in Pixeln.</param>
    /// <param name="workArea">Arbeitsbereich des Monitors.</param>
    /// <param name="edge">Bildschirmrand des Panels.</param>
    /// <param name="monitors">Bildschirmbereiche aller Monitore, auch der eigene (für Zuschnitt und Schatten).</param>
    /// <param name="cornerRadius">Eckenradius des Panels in Pixeln (für den Fensterbereich).</param>
    public void Prepare(Rectangle target, Rectangle workArea, ScreenEdge edge, IEnumerable<Rectangle> monitors, int cornerRadius)
    {
        _target = target;
        _workArea = workArea;
        _edge = edge;
        _cornerRadius = cornerRadius;
        if (!_systemWindow) return;
        _hidden = PanelSlide.Hidden(target, workArea, edge);
        var all = monitors as IReadOnlyCollection<Rectangle> ?? monitors.ToList();
        _clip = PanelSlide.NeedsClip(target, workArea, edge, all);
        _guardShadow = PanelSlide.ShadowReachesOtherMonitor(target, all);
        // Größe bei jedem Platzieren ausdrücklich setzen: hat WPF oder Windows das Fenster inzwischen anders bemessen,
        // bliebe das sonst unbemerkt (SWP_NOSIZE gilt nur für die Einzelbilder der Bewegung)
        _appliedSize = System.Drawing.Size.Empty;
        _regionApplied = false;
    }

    /// <summary>
    /// Fenster platzieren. Glas: auf die Lage zum aktuellen <see cref="Reveal"/>; Klarglas: an die Ziellage (dort gleitet
    /// nur der Inhalt).
    /// </summary>
    public void Place()
    {
        if (_systemWindow) Apply();
        else ScreenService.Place(_hwnd, _target);
    }

    /// <summary>Glas: Fenster auf die Lage zum aktuellen <see cref="Reveal"/> setzen; die Größe bleibt die des Panels.</summary>
    private void Apply()
    {
        if (!_systemWindow || _hwnd == IntPtr.Zero || _target.Width <= 0) return;
        var r = PanelSlide.At(_target, _hidden, Reveal);
        var visible = _clip ? PanelSlide.Visible(r, _workArea) : new Rectangle(0, 0, r.Width, r.Height);
        bool full = visible.Size == r.Size;
        // Fensterbereich, solange zugeschnitten wird (Taskleiste oder Nachbarmonitor an der Kante) – und dauerhaft,
        // wenn sonst der Fensterschatten auf einen anderen Monitor fiele
        Rectangle? region = !full ? visible : RestRegion(r.Size);

        // Wird der sichtbare Teil kleiner, zuerst zuschneiden, dann verschieben (sonst umgekehrt) –
        // so ragt das Panel auch für ein einzelnes Bild nicht über die Taskleiste oder auf den Nachbarmonitor.
        static long Area(Rectangle? v, Rectangle win) => v is { } x ? (long)x.Width * x.Height : (long)win.Width * win.Height;
        bool shrinking = !_regionApplied || Area(region, r) < Area(_region, r);
        if (shrinking) SetRegion(region, r.Size);
        uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER
                   | NativeMethods.SWP_NOCOPYBITS | NativeMethods.SWP_NOSENDCHANGING;
        if (r.Size == _appliedSize) flags |= NativeMethods.SWP_NOSIZE;
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, flags);
        _appliedSize = r.Size;
        if (!shrinking) SetRegion(region, r.Size);
        // Ganz eingefahren mit Zuschnitt (Nachbarmonitor oder Taskleiste an der Kante): Windows zeichnet Schatten und
        // Rand des geparkten Fensters trotz leerem Fensterbereich – dort als Rahmen sichtbar. Dann ausblenden (Cloak).
        SetCloaked(_clip && Reveal <= 0);
    }

    /// <summary>Fensterbereich des ganz sichtbaren Fensters: keiner (Windows rundet und schattiert) oder das ganze Fenster.</summary>
    private Rectangle? RestRegion(System.Drawing.Size size) => _guardShadow ? new Rectangle(0, 0, size.Width, size.Height) : null;

    /// <summary>Behält das ausgefahrene Fenster einen Fensterbereich, damit kein Schatten auf einen anderen Monitor fällt?</summary>
    public bool GuardsShadow => _guardShadow;

    private void SetCloaked(bool cloaked)
    {
        if (cloaked == _cloaked) return;
        _cloaked = cloaked;
        PanelWindowNative.SetCloaked(_hwnd, cloaked);
    }

    private void SetRegion(Rectangle? region, System.Drawing.Size size)
    {
        if (_regionApplied && Nullable.Equals(region, _region)) return; // unverändert: kein Fensterbereich pro Bild
        _region = region;
        _regionApplied = true;
        if (region is not { } v) { PanelWindowNative.ClearRegion(_hwnd); return; }
        PanelWindowNative.SetClippedRoundedRegion(_hwnd, size.Width, size.Height, _cornerRadius, v.Left, v.Top, v.Right, v.Bottom);
    }

    /// <summary>
    /// Glas: <see cref="Reveal"/> sofort auf einen festen Wert setzen. Erst der Wert, dann die Animation entfernen:
    /// umgekehrt fiele die Eigenschaft für einen Augenblick auf ihren alten festen Wert zurück und das Fenster spränge dorthin.
    /// </summary>
    public void SetNow(double reveal)
    {
        _window.SetValue(RevealProperty, reveal);
        _window.BeginAnimation(RevealProperty, null);
    }

    /// <summary>Ein- bzw. ausfahren; <paramref name="completed"/> läuft, wenn die Bewegung zu Ende ist (ohne Dauer: sofort).</summary>
    /// <param name="show">Ausfahren (true) oder einfahren.</param>
    /// <param name="duration">Dauer des Ausfahrens; <see cref="TimeSpan.Zero"/> = ohne Animation.</param>
    /// <param name="fallbackWidth">Klarglas: Panelbreite in DIP, solange das Fenster noch keine Größe hat.</param>
    /// <param name="completed">Abschluss der Bewegung.</param>
    public void Animate(bool show, TimeSpan duration, double fallbackWidth, Action completed)
    {
        if (_systemWindow) AnimateWindow(show, duration, completed);
        else AnimateContent(show, duration, fallbackWidth, completed);
    }

    /// <summary>Glas: das Fenster gleitet.</summary>
    private void AnimateWindow(bool show, TimeSpan duration, Action completed)
    {
        ResetContent();
        if (duration == TimeSpan.Zero)
        {
            SetNow(show ? 1 : 0);
            completed();
            return;
        }
        // Ausfahren zügig (80 % der Dauer, wie Windows 11) mit Sine-EaseOut, Einfahren mit Cubic-EaseIn (70 %);
        // Startwert ausdrücklich der aktuelle Stand
        var showDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.8);
        var hideDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.7);
        var anim = new DoubleAnimation(show ? 1.0 : 0.0, show ? showDuration : hideDuration)
        {
            From = Reveal,
            EasingFunction = show
                ? new SineEase { EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) => completed();
        _window.BeginAnimation(RevealProperty, anim);
    }

    /// <summary>Klarglas: Fenster mit Transparenz-Schicht, der Inhalt gleitet im Fenster und blendet ein bzw. aus.</summary>
    private void AnimateContent(bool show, TimeSpan duration, double fallbackWidth, Action completed)
    {
        double distance = _edge is ScreenEdge.Left or ScreenEdge.Right
            ? (_window.ActualWidth > 0 ? _window.ActualWidth : fallbackWidth) + 24
            : (_window.ActualHeight > 0 ? _window.ActualHeight : 600) + 24;
        double hiddenOffset = _edge is ScreenEdge.Left or ScreenEdge.Top ? -distance : distance;
        var property = _edge is ScreenEdge.Left or ScreenEdge.Right ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var other = property == TranslateTransform.XProperty ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        _contentSlide.BeginAnimation(other, null);
        _contentSlide.SetValue(other, 0.0);

        if (duration == TimeSpan.Zero)
        {
            _contentSlide.BeginAnimation(property, null);
            _contentSlide.SetValue(property, show ? 0.0 : hiddenOffset);
            _content.BeginAnimation(UIElement.OpacityProperty, null);
            _content.Opacity = show ? 1 : 0;
            completed();
            return;
        }

        var ease = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
        var from = show ? hiddenOffset : (double)_contentSlide.GetValue(property);
        var slide = new DoubleAnimation(from, show ? 0.0 : hiddenOffset, show ? duration : TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.7))
        {
            EasingFunction = ease
        };
        var fade = new DoubleAnimation(show ? 0.0 : 1.0, show ? 1.0 : 0.0, TimeSpan.FromMilliseconds(Math.Min(220, duration.TotalMilliseconds)));
        slide.Completed += (_, _) => completed();
        _contentSlide.BeginAnimation(property, slide);
        _content.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Inhalt an seinen Platz im Fenster, voll sichtbar – als feste Werte, nicht nur als Rest einer Animation.</summary>
    private void ResetContent()
    {
        _contentSlide.X = 0;
        _contentSlide.Y = 0;
        _contentSlide.BeginAnimation(TranslateTransform.XProperty, null);
        _contentSlide.BeginAnimation(TranslateTransform.YProperty, null);
        _content.Opacity = 1;
        _content.BeginAnimation(UIElement.OpacityProperty, null);
    }

    /// <summary>
    /// Endlage nach dem Ausfahren absichern. Die Lage entsteht sonst nur aus den Einzelbildern der Bewegung: hat
    /// inzwischen jemand das Fenster verschoben oder anders bemessen (Windows nach Standby oder Monitorwechsel, WPF
    /// beim ersten Anzeigen), bliebe das bis zum nächsten Platzieren so – das Panel stünde nur teilweise im Bild.
    /// Abweichungen werden korrigiert und protokolliert.
    /// </summary>
    /// <returns>true, wenn das Fenster schon richtig stand.</returns>
    public bool Settle()
    {
        if (_hwnd == IntPtr.Zero || _target.Width <= 0) return true;
        if (_systemWindow)
        {
            SetNow(1);
            var settled = PanelSlide.IsSettled(WindowRect(), _target, tolerance: 0);
            if (!settled)
            {
                AppLog.Warn($"Panel: corrected end position, window at {WindowRect()}, expected {_target}");
                NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, _target.X, _target.Y, _target.Width, _target.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
                _appliedSize = _target.Size;
            }
            _regionApplied = false;
            SetRegion(RestRegion(_target.Size), _target.Size);
            SetCloaked(false);
            return settled;
        }
        ResetContent();
        // WPF rechnet Lage und Größe über DIP um: bis zu 2 px Abweichung sind Rundung
        if (PanelSlide.IsSettled(WindowRect(), _target, tolerance: 2)) return true;
        AppLog.Warn($"Panel: corrected end position, window at {WindowRect()}, expected {_target}");
        ScreenService.Place(_hwnd, _target);
        return false;
    }

    /// <summary>Steht das ausgefahrene Fenster an seiner Ziellage (Klarglas: bis auf Rundung)?</summary>
    public bool IsSettled => PanelSlide.IsSettled(WindowRect(), _target, _systemWindow ? 0 : 2);

    /// <summary>Tatsächliche Lage des Fensters in Pixeln.</summary>
    public Rectangle WindowRect()
    {
        NativeMethods.GetWindowRect(_hwnd, out var rc);
        return Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom);
    }

    /// <summary>Für den Selbsttest: Fenster von außen verschieben, wie es Windows nach Standby oder Monitorwechsel tut.</summary>
    public void Displace(int dx, int dy)
    {
        if (_hwnd == IntPtr.Zero || !_window.IsVisible) return;
        var r = WindowRect();
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, r.X + dx, r.Y + dy, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }
}
