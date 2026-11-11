using PowerExec.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Desktop.Interop;
using PowerExec.Desktop.Services;

namespace PowerExec.Desktop.Views;

/// <summary>
/// Schmale, fast unsichtbare Hover-Zone am Bildschirmrand. Nach der Verweilzeit fährt das Panel aus.
/// </summary>
public sealed class EdgeTriggerWindow : Window
{
    private readonly AppController _app;
    private readonly PanelHost _host;
    private readonly MonitorInfo _monitor;
    private readonly DispatcherTimer _hoverTimer;
    private IntPtr _hwnd;

    public EdgeTriggerWindow(AppController app, PanelHost host, MonitorInfo monitor)
    {
        _app = app;
        _host = host;
        _monitor = monitor;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        // Fast transparent, damit die Maus erkannt wird
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Title = L.T("Power-Exec hover zone");

        // Die sichtbare Leiste füllt die ganze Hover-Zone: mit „Akzentfarbe von Windows“ in der Windows-Akzentfarbe,
        // sonst in der eigenen Akzentfarbe bzw. der Panelfarbe (ohne beides: Akzentfarbe des Designs).
        var settings = host.Config;
        Brush fill;
        Brush outline = Brushes.Transparent;
        if (!app.Settings.UseWindowsAccent && (PanelColors.TryParse(settings.AccentColor, out var rgb) || PanelColors.TryParse(settings.PanelColor, out rgb)))
        {
            fill = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
            // Feiner Rand, damit auch sehr dunkle oder sehr helle Farben auf dem Desktop sichtbar bleiben
            var border = PanelColors.Blend(rgb, PanelColors.IsLight(rgb) ? new Rgb(0, 0, 0) : new Rgb(255, 255, 255), 0.35);
            outline = new SolidColorBrush(Color.FromRgb(border.R, border.G, border.B));
        }
        else
        {
            fill = (Brush)Application.Current.Resources["Accent"];
        }
        double thicknessDip = Math.Max(1, settings.HotZoneThickness);
        var strip = new Border
        {
            Background = fill,
            BorderBrush = outline,
            BorderThickness = new Thickness(thicknessDip >= 4 ? 1 : 0),
            Opacity = Math.Max(0.55, settings.EffectiveOpacity / 100.0 * 0.85),
            CornerRadius = new CornerRadius(Math.Min(4, thicknessDip / 2)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Content = strip;
        _strip = strip;
        _baseOpacity = strip.Opacity;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, app.Settings.HoverDelayMs)) };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (IsMouseOver || ScreenService.CursorInside(CurrentRect)) _host.ShowFromTrigger(_monitor);
        };

        MouseEnter += (_, _) =>
        {
            if (_app.Settings.HoverDelayMs <= 0) _host.ShowFromTrigger(_monitor);
            else _hoverTimer.Start();
        };
        MouseLeave += (_, _) => _hoverTimer.Stop();
        // Eingefahren, während der Mauszeiger noch in der Zone steht (Klick auf die Zone, Einfahren nach einer Aktion):
        // ein MouseEnter kommt dann nicht mehr. Bewegung in der Zone startet die Verweilzeit neu – ruhig stehen lassen nicht.
        MouseMove += (_, _) =>
        {
            if (_hoverTimer.IsEnabled || _host.Window.IsOpen || DateTime.UtcNow - _toggledAt < TimeSpan.FromMilliseconds(500)) return;
            if (_app.Settings.HoverDelayMs <= 0) _host.ShowFromTrigger(_monitor);
            else _hoverTimer.Start();
        };
        // Angeheftetes Panel: Klick auf die Zone fährt es ein bzw. aus
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_host.Config.Pinned) return;
            _hoverTimer.Stop();
            _toggledAt = DateTime.UtcNow;
            _host.ToggleFromTrigger(_monitor);
            e.Handled = true;
        };

        // Dateien aus dem Explorer an den Rand ziehen: Panel fährt aus, damit man sie in eine Gruppe ablegen kann
        AllowDrop = true;
        DragEnter += (_, e) =>
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            _hoverTimer.Start();
        };
        DragOver += (_, e) => { e.Effects = DragDropEffects.None; e.Handled = true; };
        DragLeave += (_, _) => _hoverTimer.Stop();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.MakeToolWindow(_hwnd, noActivate: true);
            Place();
        };
        Loaded += (_, _) => Place();
        ContentRendered += (_, _) => Place();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
    }

    /// <summary>Über alle Fenster (auch das Panel) legen, ohne zu aktivieren.</summary>
    public void BringAboveWindows()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    public System.Drawing.Rectangle CurrentRect => ScreenService.HotZone(_monitor, _host.Config);
    public MonitorInfo Monitor => _monitor;

    private bool _suspended;
    private DateTime _toggledAt;
    private readonly Border _strip;
    private readonly double _baseOpacity;
    private bool _busy;

    /// <summary>Solange ein Start läuft, pulsiert die Hover-Zone (bei „Animation aus“ leuchtet sie nur kräftiger).</summary>
    public void SetBusy(bool busy)
    {
        if (_busy == busy) return;
        _busy = busy;
        _strip.BeginAnimation(OpacityProperty, null);
        if (!busy) { _strip.Opacity = _baseOpacity; return; }
        if (_app.Settings.Animation == AnimationMode.Off || !SystemParameters.ClientAreaAnimation) { _strip.Opacity = 1; return; }
        _strip.BeginAnimation(OpacityProperty, new DoubleAnimation(Math.Min(_baseOpacity, 0.45), 1, TimeSpan.FromMilliseconds(900))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    /// <summary>Hover-Zone vorübergehend deaktivieren (Vollbild-Programm auf diesem Monitor).</summary>
    public void SetSuspended(bool suspended)
    {
        if (_suspended == suspended) return;
        _suspended = suspended;
        _hoverTimer.Stop();
        if (suspended) Hide();
        else
        {
            Show();
            Place();
        }
    }

    private void Place()
    {
        if (_hwnd != IntPtr.Zero) ScreenService.Place(_hwnd, CurrentRect);
    }
}
