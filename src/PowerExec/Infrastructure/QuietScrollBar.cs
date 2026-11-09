using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PowerExec.Infrastructure;

/// <summary>
/// Scrollbalken wie in den Windows-11-Einstellungen: nur sichtbar, wenn es etwas zu scrollen gibt und gerade gescrollt
/// oder die Maus über dem Bereich bewegt wird; sonst blendet er weich aus. Die Breite bleibt reserviert, damit der
/// Inhalt beim Ein- und Ausblenden nicht springt.
/// </summary>
public static class QuietScrollBar
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(1200);

    public static void Attach(ScrollViewer viewer)
    {
        ScrollBar? bar = null;
        var idle = new DispatcherTimer(DispatcherPriority.Background, viewer.Dispatcher) { Interval = IdleDelay };

        bool Scrollable() => viewer.ScrollableHeight > 0.5;

        void Fade(double to, int ms)
        {
            if (bar == null) return;
            if (ms <= 0 || !SystemParameters.ClientAreaAnimation) { bar.BeginAnimation(UIElement.OpacityProperty, null); bar.Opacity = to; return; }
            bar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase() });
        }

        void Wake()
        {
            if (bar == null) return;
            if (!Scrollable()) { idle.Stop(); Fade(0, 0); return; }
            if (bar.Opacity < 1) Fade(1, 120);
            idle.Stop();
            idle.Start();
        }

        idle.Tick += (_, _) =>
        {
            if (bar?.IsMouseOver == true || bar?.IsMouseCaptureWithin == true) return; // beim Ziehen am Balken sichtbar lassen
            idle.Stop();
            Fade(0, 300);
        };

        void Hook()
        {
            if (bar != null) return;
            viewer.ApplyTemplate();
            bar = viewer.Template?.FindName("PART_VerticalScrollBar", viewer) as ScrollBar;
            if (bar == null) return;
            bar.Opacity = 0;
            bar.MouseEnter += (_, _) => Wake();
        }

        viewer.Loaded += (_, _) => Hook();
        viewer.PreviewMouseMove += (_, _) => Wake();
        viewer.MouseLeave += (_, _) => { if (bar != null && bar.Opacity > 0) { idle.Stop(); idle.Start(); } };
        viewer.ScrollChanged += (_, e) =>
        {
            Hook();
            if (e.VerticalChange != 0) Wake();
            else if (!Scrollable()) { idle.Stop(); Fade(0, 0); }
        };
    }
}
