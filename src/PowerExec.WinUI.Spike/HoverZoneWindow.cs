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
    private readonly DispatcherQueueTimer _dwell, _fullscreenCheck, _pulse, _cursorPoll;
    private RectInt32 _zoneRect;
    private bool _cursorInside;
    private string _dwellSource = string.Empty;
    private string? _lastSuppressReason;
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
        _dwell.Tick += (_, _) =>
        {
            SpikeLog.Write($"Hover-Zone löst aus ({_dwellSource})");
            Triggered++;
            _panel.SlideIn();
        };
        // Weg 1: Maus-Ereignis des schmalen Fensters (wie in der WPF-App)
        _strip.PointerEntered += (_, _) => StartDwell("Maus-Ereignis");
        _strip.PointerExited += (_, _) => { if (!_cursorInside) _dwell.Stop(); };

        // Weg 2: Mausposition abfragen – unabhängig davon, ob WinUI das wenige Pixel breite Fenster „trifft“
        _cursorPoll = DispatcherQueue.CreateTimer();
        _cursorPoll.Interval = TimeSpan.FromMilliseconds(40);
        _cursorPoll.Tick += (_, _) => PollCursor();

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
        _appWindow.Closing += (_, e) => { _fullscreenCheck.Stop(); _pulse.Stop(); _cursorPoll.Stop(); _messages.Dispose(); };
    }

    /// <summary>Wie oft die Zone das Panel ausgefahren hat.</summary>
    public int Triggered { get; private set; }

    private void StartDwell(string source)
    {
        if (_panel.IsShown || _dwell.IsRunning) return;
        _dwellSource = source;
        _dwell.Start();
    }

    /// <summary>Steht der Mauszeiger am Rand im Bereich der Zone (auch knapp daneben)?</summary>
    private void PollCursor()
    {
        if (!_appWindow.IsVisible) { _cursorInside = false; return; }
        Native.GetCursorPos(out var pt);
        var r = _zoneRect;
        var inside = pt.X >= r.X - 1 && pt.X <= r.X + r.Width + 1 && pt.Y >= r.Y && pt.Y < r.Y + r.Height;
        if (inside && !_cursorInside) StartDwell("Mausposition");
        else if (!inside && _cursorInside) _dwell.Stop();
        _cursorInside = inside;
    }

    /// <summary>Mitte der Zone in Bildschirmpixeln (für den Smoke-Test).</summary>
    public (int X, int Y) Center => (_zoneRect.X + _zoneRect.Width / 2, _zoneRect.Y + _zoneRect.Height / 2);

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
        _zoneRect = new RectInt32(work.X + work.Width - w, work.Y + (work.Height - h) / 2, w, h);
        _appWindow.MoveAndResize(_zoneRect);
        _wanted = true;
        ApplyVisibility();
        _fullscreenCheck.Start();
        _cursorPoll.Start();
        SpikeLog.Write($"Hover-Zone: {_zoneRect.Width}×{_zoneRect.Height} px bei x={_zoneRect.X}, y={_zoneRect.Y} (rechter Rand des Hauptmonitors, mittig)");
    }

    private void ApplyVisibility()
    {
        var reason = Native.FullscreenReason();
        SuppressedByFullscreen = reason != null;
        if (reason != _lastSuppressReason)
        {
            SpikeLog.Write(reason == null ? "Hover-Zone wieder aktiv" : $"Hover-Zone ausgeblendet: {reason}");
            _lastSuppressReason = reason;
        }
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
