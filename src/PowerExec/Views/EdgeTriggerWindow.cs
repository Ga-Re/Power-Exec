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
    private readonly MonitorInfo _monitor;
    private readonly DispatcherTimer _hoverTimer;
    private IntPtr _hwnd;

    public EdgeTriggerWindow(AppController app, MonitorInfo monitor)
    {
        _app = app;
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

        var vertical = app.Settings.Edge is ScreenEdge.Left or ScreenEdge.Right;
        var strip = new Border
        {
            Background = (Brush)Application.Current.Resources["Accent"],
            Opacity = 0.55,
            CornerRadius = new CornerRadius(2),
            Width = vertical ? 3 : double.NaN,
            Height = vertical ? double.NaN : 3,
            HorizontalAlignment = app.Settings.Edge switch
            {
                ScreenEdge.Left => HorizontalAlignment.Left,
                ScreenEdge.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Stretch
            },
            VerticalAlignment = app.Settings.Edge switch
            {
                ScreenEdge.Top => VerticalAlignment.Top,
                ScreenEdge.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Stretch
            },
            Margin = vertical ? new Thickness(0, 24, 0, 24) : new Thickness(24, 0, 24, 0)
        };
        Content = strip;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, app.Settings.HoverDelayMs)) };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (IsMouseOver || ScreenService.CursorInside(CurrentRect)) _app.ShowPanelFromTrigger(_monitor);
        };

        MouseEnter += (_, _) =>
        {
            if (_app.Settings.HoverDelayMs <= 0) _app.ShowPanelFromTrigger(_monitor);
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

    public System.Drawing.Rectangle CurrentRect => ScreenService.HotZone(_monitor, _app.Settings);

    private void Place()
    {
        if (_hwnd != IntPtr.Zero) ScreenService.Place(_hwnd, CurrentRect);
    }
}
