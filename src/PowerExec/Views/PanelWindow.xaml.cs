using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Interop;
using PowerExec.Services;

namespace PowerExec.Views;

public partial class PanelWindow : Window
{
    private readonly AppController _app;
    private readonly DispatcherTimer _poll;
    private IntPtr _hwnd;
    private MonitorInfo? _monitor;
    private System.Drawing.Rectangle _rect;
    private DateTime? _outsideSince;
    private bool _hiding;

    public bool IsOpen { get; private set; }
    public bool OpenedViaHotkey { get; private set; }

    public PanelWindow(AppController app)
    {
        _app = app;
        InitializeComponent();
        DataContext = app.Panel;

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.MakeToolWindow(_hwnd, noActivate: false);

        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _poll.Tick += (_, _) => CheckAutoHide();

        Deactivated += (_, _) => OnDeactivated();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_app.Panel.IsSearching) _app.Panel.SearchText = string.Empty;
                else HidePanel();
                e.Handled = true;
            }
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (IsOpen) Place(); });
    }

    private TimeSpan AnimationDuration => _app.Settings.Animation switch
    {
        AnimationMode.Off => TimeSpan.Zero,
        AnimationMode.Fast => TimeSpan.FromMilliseconds(150),
        _ => TimeSpan.FromMilliseconds(320)
    };

    public void ShowPanel(MonitorInfo monitor, bool viaHotkey)
    {
        OpenedViaHotkey = viaHotkey;
        _outsideSince = null;

        if (IsOpen && !_hiding && _monitor?.Index == monitor.Index)
        {
            if (viaHotkey) FocusSearch();
            return;
        }

        _monitor = monitor;
        _hiding = false;
        Place();
        if (!IsVisible) Show();
        Place(); // WPF kann beim ersten Anzeigen die Größe zurücksetzen
        IsOpen = true;
        _poll.Start();
        Animate(show: true);
        if (viaHotkey) FocusSearch();
    }

    private void FocusSearch()
    {
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    public void HidePanel()
    {
        if (!IsOpen || _hiding) return;
        _hiding = true;
        _poll.Stop();
        Animate(show: false);
    }

    /// <summary>Nach einer Aktion (Start, Dialog geöffnet) einfahren – außer das Panel ist angeheftet.</summary>
    public void HideAfterAction(bool force = false)
    {
        if (!force && _app.Settings.Pinned) return;
        HidePanel();
    }

    public void ApplySettings()
    {
        if (IsOpen && _monitor != null)
        {
            // Monitor kann sich geändert haben
            _monitor = ScreenService.TriggerMonitors(_app.Settings)[0];
            Place();
        }
    }

    private void Place()
    {
        if (_monitor == null) return;
        _rect = ScreenService.PanelRect(_monitor, _app.Settings);
        ScreenService.Place(_hwnd, _rect);
    }

    private void Animate(bool show)
    {
        var duration = AnimationDuration;
        var edge = _app.Settings.Edge;
        double distance = edge is ScreenEdge.Left or ScreenEdge.Right
            ? (ActualWidth > 0 ? ActualWidth : _app.Settings.PanelWidth) + 24
            : (ActualHeight > 0 ? ActualHeight : 600) + 24;
        double hiddenOffset = edge is ScreenEdge.Left or ScreenEdge.Top ? -distance : distance;
        var property = edge is ScreenEdge.Left or ScreenEdge.Right ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var other = property == TranslateTransform.XProperty ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        Slide.BeginAnimation(other, null);
        Slide.SetValue(other, 0.0);

        if (duration == TimeSpan.Zero)
        {
            Slide.BeginAnimation(property, null);
            Slide.SetValue(property, show ? 0.0 : hiddenOffset);
            PanelBorder.BeginAnimation(OpacityProperty, null);
            PanelBorder.Opacity = show ? 1 : 0;
            if (!show) FinishHide();
            return;
        }

        var ease = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
        var from = show ? hiddenOffset : (double)Slide.GetValue(property);
        var slide = new DoubleAnimation(from, show ? 0.0 : hiddenOffset, show ? duration : TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.7))
        {
            EasingFunction = ease
        };
        var fade = new DoubleAnimation(show ? 0.0 : 1.0, show ? 1.0 : 0.0, TimeSpan.FromMilliseconds(Math.Min(220, duration.TotalMilliseconds)));
        if (!show) slide.Completed += (_, _) => { if (_hiding) FinishHide(); };
        Slide.BeginAnimation(property, slide);
        PanelBorder.BeginAnimation(OpacityProperty, fade);
    }

    private void FinishHide()
    {
        _hiding = false;
        IsOpen = false;
        OpenedViaHotkey = false;
        Hide();
        _app.Panel.SearchText = string.Empty;
        _app.Panel.ClearSelection();
        ListScroll.ScrollToTop();
    }

    private void CheckAutoHide()
    {
        if (!IsOpen || _hiding || _app.Settings.Pinned) { _outsideSince = null; return; }
        if (AnyPopupOpen()) { _outsideSince = null; return; }
        // Per Tastenkürzel geöffnet oder aktiv am Tippen: Schließen erst beim Fokusverlust.
        if (OpenedViaHotkey || (IsActive && _app.Panel.IsSearching)) { _outsideSince = null; return; }

        bool inside = ScreenService.CursorInside(_rect, 2) || _app.Triggers.Any(t => ScreenService.CursorInside(t.CurrentRect, 2));
        if (inside)
        {
            _outsideSince = null;
            return;
        }
        _outsideSince ??= DateTime.Now;
        if ((DateTime.Now - _outsideSince.Value).TotalMilliseconds >= _app.Settings.CollapseDelayMs)
            HidePanel();
    }

    private void OnDeactivated()
    {
        if (!IsOpen || _app.Settings.Pinned) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!IsOpen || IsActive || AnyPopupOpen()) return;
            if (OpenedViaHotkey || !ScreenService.CursorInside(_rect)) HidePanel();
        });
    }

    /// <summary>Offenes Kontextmenü oder Auswahlliste? Dann nicht automatisch einfahren.</summary>
    private static bool AnyPopupOpen() =>
        PresentationSource.CurrentSources.OfType<HwndSource>()
            .Any(s => s.RootVisual is FrameworkElement fe && fe.GetType().Name == "PopupRoot" && fe.IsVisible);

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.DataContext = button.DataContext;
            menu.IsOpen = true;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                ScrollIntoView(_app.Panel.MoveSelection(1));
                e.Handled = true;
                break;
            case Key.Up:
                ScrollIntoView(_app.Panel.MoveSelection(-1));
                e.Handled = true;
                break;
            case Key.Enter:
                _app.Panel.RunSelectedOrFirst();
                e.Handled = true;
                break;
        }
    }

    private void ScrollIntoView(object? item)
    {
        if (item == null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var element = FindContainer(ListScroll, item);
            element?.BringIntoView();
        });
    }

    private static FrameworkElement? FindContainer(DependencyObject parent, object item)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ContentPresenter cp && ReferenceEquals(cp.Content, item)) return cp;
            var found = FindContainer(child, item);
            if (found != null) return found;
        }
        return null;
    }
}
