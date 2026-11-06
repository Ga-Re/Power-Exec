using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI;

namespace PowerExec.WinUI.Spike;

/// <summary>
/// Hover-Zone (P2): schmaler, halbtransparenter Streifen am rechten Rand des Hauptmonitors. Verweilt die Maus
/// kurz darauf (oder wird etwas darauf gezogen), fährt das Panel aus. Bei Vollbild-Programmen ausgeblendet;
/// pulsiert, solange „Aktivität“ läuft.
/// </summary>
public sealed class HoverZoneWindow : Window
{
    private const double WidthDip = 5;
    private const double HeightShare = 0.4;

    private readonly PanelWindow _panel;
    private readonly AppWindow _appWindow;
    private readonly Grid _strip;
    private readonly DispatcherQueueTimer _dwell, _fullscreenCheck, _pulse;
    private bool _wanted;
    private readonly WinUIEx.Messaging.WindowMessageMonitor _messages;
    private double _pulsePhase;

    public HoverZoneWindow(PanelWindow panel)
    {
        _panel = panel;
        Title = "Power-Exec Hover-Zone";
        _appWindow = AppWindow;
        if (_appWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsAlwaysOnTop = true;
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }
        _appWindow.IsShownInSwitchers = false;
        // Windows gibt Fenstern sonst eine Mindestbreite (~136 px) – die Zone soll nur wenige Pixel breit sein,
        // damit sie am Bildschirmrand keine Klicks abfängt.
        _messages = new WinUIEx.Messaging.WindowMessageMonitor(this);
        _messages.WindowMessageReceived += (_, e) =>
        {
            if (e.Message.MessageId != Native.WM_GETMINMAXINFO) return;
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.MINMAXINFO>((IntPtr)e.Message.LParam);
            info.ptMinTrackSize = new Native.POINT { X = 1, Y = 1 };
            System.Runtime.InteropServices.Marshal.StructureToPtr(info, (IntPtr)e.Message.LParam, false);
            e.Result = 0;
            e.Handled = true;
        };
        SystemBackdrop = new WinUIEx.TransparentTintBackdrop { TintColor = Color.FromArgb(0, 0, 0, 0) };

        _strip = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x70, 0x00, 0x78, 0xD4)), AllowDrop = true };
        Content = _strip;

        _dwell = DispatcherQueue.CreateTimer();
        _dwell.Interval = TimeSpan.FromMilliseconds(250);
        _dwell.IsRepeating = false;
        _dwell.Tick += (_, _) => _panel.SlideIn();
        _strip.PointerEntered += (_, _) => _dwell.Start();
        _strip.PointerExited += (_, _) => _dwell.Stop();

        // Etwas aus dem Explorer an den Rand ziehen: Panel fährt aus, dort ablegen
        _strip.DragEnter += (_, e) =>
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            _panel.SlideIn();
        };

        _fullscreenCheck = DispatcherQueue.CreateTimer();
        _fullscreenCheck.Interval = TimeSpan.FromSeconds(1);
        _fullscreenCheck.Tick += (_, _) => ApplyVisibility();

        _pulse = DispatcherQueue.CreateTimer();
        _pulse.Interval = TimeSpan.FromMilliseconds(40);
        _pulse.Tick += (_, _) =>
        {
            _pulsePhase += 0.12;
            _strip.Opacity = 0.45 + 0.55 * (0.5 + 0.5 * Math.Sin(_pulsePhase));
        };
        _appWindow.Closing += (_, e) => { _fullscreenCheck.Stop(); _pulse.Stop(); _messages.Dispose(); };
    }

    /// <summary>Wird gerade ein Vollbild-Programm erkannt (Zone dann ausgeblendet)?</summary>
    public bool SuppressedByFullscreen { get; private set; }
    public bool IsZoneVisible => _appWindow.IsVisible;
    public bool IsPulsing => _pulse.IsRunning;
    public SizeInt32 SizePx => _appWindow.Size;

    /// <summary>Zone am rechten Rand des Hauptmonitors anzeigen (ohne Fokus zu nehmen) und überwachen.</summary>
    public void ShowZone()
    {
        var area = DisplayArea.Primary;
        var work = area.WorkArea;
        var dpi = Native.GetDpiForMonitorAt(work.X + work.Width - 1, work.Y + work.Height / 2);
        int w = Math.Max(2, (int)Math.Round(WidthDip * dpi / 96.0));
        int h = (int)(work.Height * HeightShare);
        _appWindow.MoveAndResize(new RectInt32(work.X + work.Width - w, work.Y + (work.Height - h) / 2, w, h));
        _wanted = true;
        ApplyVisibility();
        _fullscreenCheck.Start();
    }

    private void ApplyVisibility()
    {
        SuppressedByFullscreen = Native.IsFullscreenAppActive();
        var show = _wanted && !SuppressedByFullscreen;
        if (show && !_appWindow.IsVisible) _appWindow.Show(false);
        else if (!show && _appWindow.IsVisible) _appWindow.Hide();
    }

    /// <summary>Pulsieren, solange etwas läuft (Aktivitäten-Leiste der echten App).</summary>
    public void SetBusy(bool busy)
    {
        if (busy) _pulse.Start();
        else
        {
            _pulse.Stop();
            _strip.Opacity = 1;
        }
    }
}
