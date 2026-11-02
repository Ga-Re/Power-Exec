using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Interop;
using PowerExec.Services;

namespace PowerExec.Views;

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
        Title = "Power-Exec Hover-Zone";

        // Die sichtbare Leiste füllt die ganze Hover-Zone und nimmt die Panelfarbe an
        // (ohne eigene Farbe: Akzentfarbe des Designs).
        var settings = host.Config;
        Brush fill;
        Brush outline = Brushes.Transparent;
        if (PanelColors.TryParse(settings.PanelColor, out var rgb))
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
            Opacity = Math.Max(0.55, settings.PanelOpacity / 100.0 * 0.85),
            CornerRadius = new CornerRadius(Math.Min(4, thicknessDip / 2)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Content = strip;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, app.Settings.HoverDelayMs)) };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (IsMouseOver || ScreenService.CursorInside(CurrentRect)) _app.ShowPanelFromTrigger(_host, _monitor);
        };

        MouseEnter += (_, _) =>
        {
            if (_app.Settings.HoverDelayMs <= 0) _app.ShowPanelFromTrigger(_host, _monitor);
            else _hoverTimer.Start();
        };
        MouseLeave += (_, _) => _hoverTimer.Stop();

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

    public System.Drawing.Rectangle CurrentRect => ScreenService.HotZone(_monitor, _host.Config);
    public MonitorInfo Monitor => _monitor;

    private bool _suspended;

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
