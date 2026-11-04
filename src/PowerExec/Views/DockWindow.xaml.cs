using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Infrastructure;
using PowerExec.Interop;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec.Views;

/// <summary>Ein Platz im Mini-Dock: Favorit (Klick startet) oder „⋯“ (öffnet das Panel).</summary>
public sealed class DockSlot : ObservableObject
{
    private readonly ItemViewModel? _item;

    public DockSlot(ItemViewModel item, int number)
    {
        _item = item;
        Name = item.DisplayName;
        ToolTip = $"{item.DisplayName} – Klick startet (Alt+{number})" + (string.IsNullOrWhiteSpace(item.Synopsis) ? string.Empty : "\n" + item.Synopsis);
        Command = item.RunCommand;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ItemViewModel.Icon) or nameof(ItemViewModel.HasIcon) or nameof(ItemViewModel.ShowBadge))
            {
                OnPropertyChanged(nameof(Icon)); OnPropertyChanged(nameof(HasIcon)); OnPropertyChanged(nameof(ShowBadge));
            }
        };
    }

    public DockSlot(ICommand openPanel)
    {
        Name = "Panel öffnen";
        ToolTip = "Panel öffnen – auch beim Darüberfahren";
        Command = openPanel;
        IsMore = true;
    }

    public string Name { get; }
    public string ToolTip { get; }
    public ICommand Command { get; }
    public bool IsMore { get; }
    public string Glyph => ""; // „⋯“
    public ImageSource? Icon => _item?.Icon;
    public bool HasIcon => _item?.HasIcon == true;
    public string CustomIcon => _item?.CustomIcon ?? string.Empty;
    public bool HasCustomIcon => _item?.HasCustomIcon == true;
    public bool ShowBadge => _item?.ShowBadge == true;
    public string Badge => _item?.BadgeText ?? string.Empty;
}

/// <summary>
/// Mini-Dock: schmale Leiste mit den Favoriten am Rand des Arbeitsbereichs (in den Einstellungen einschaltbar).
/// Ein Klick startet den Favoriten, Darüberfahren über „⋯“ öffnet nach der Verweilzeit das volle Panel.
/// Solange das Panel offen ist oder ein Vollbild-Programm läuft, ist das Dock ausgeblendet.
/// </summary>
public partial class DockWindow : Window
{
    private readonly AppController _app;
    private readonly PanelHost _host;
    private readonly MonitorInfo _monitor;
    private readonly DispatcherTimer _hoverTimer;
    private IntPtr _hwnd;
    private bool _suspended;
    private bool _closed;
    private int _slotCount = 1;

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(DockWindow), new PropertyMetadata(Orientation.Vertical));

    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    public DockWindow(AppController app, PanelHost host, MonitorInfo monitor)
    {
        _app = app;
        _host = host;
        _monitor = monitor;
        InitializeComponent();
        Orientation = host.Config.Edge is ScreenEdge.Left or ScreenEdge.Right ? Orientation.Vertical : Orientation.Horizontal;
        // Abgerundet nur zur Bildschirmmitte hin
        DockBorder.CornerRadius = host.Config.Edge switch
        {
            ScreenEdge.Left => new CornerRadius(0, 8, 8, 0),
            ScreenEdge.Top => new CornerRadius(0, 0, 8, 8),
            ScreenEdge.Bottom => new CornerRadius(8, 8, 0, 0),
            _ => new CornerRadius(8, 0, 0, 8)
        };
        DockBorder.Opacity = Math.Max(0.6, host.Config.PanelOpacity / 100.0);

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(150, app.Settings.HoverDelayMs)) };
        _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); OpenPanel(); };

        host.ViewModel.QuickItemsChanged += Rebuild;
        host.Window.IsVisibleChanged += OnPanelVisibleChanged;
        // Nach dem Schließen (Dock ausgeschaltet, Panels neu erzeugt) nicht mehr reagieren
        Closed += (_, _) =>
        {
            _closed = true;
            _hoverTimer.Stop();
            host.ViewModel.QuickItemsChanged -= Rebuild;
            host.Window.IsVisibleChanged -= OnPanelVisibleChanged;
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.MakeToolWindow(_hwnd, noActivate: true);
            Place();
        };
        Loaded += (_, _) => Place();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        Rebuild();
    }

    public MonitorInfo Monitor => _monitor;
    public int SlotCount => _slotCount;

    private void OpenPanel() => _app.ShowPanelFromTrigger(_host, _monitor);

    /// <summary>Favoriten neu einlesen (Reihenfolge wie in der Gruppe „Favoriten“).</summary>
    private void OnPanelVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e) => UpdateVisibility();

    public void Rebuild()
    {
        if (_closed) return;
        var slots = _host.ViewModel.QuickItems
            .Select((item, i) => new DockSlot(new ItemViewModel(_app, _host.ViewModel, item, "dock"), i + 1))
            .ToList();
        slots.Add(new DockSlot(new RelayCommand(OpenPanel)));
        Slots.ItemsSource = slots;
        _slotCount = slots.Count;
        Place();
    }

    private void Slot_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DockSlot { IsMore: true } }) _hoverTimer.Start();
    }

    private void Slot_MouseLeave(object sender, MouseEventArgs e) => _hoverTimer.Stop();

    /// <summary>Bei Vollbild-Programmen ausblenden (wie die Hover-Zone).</summary>
    public void SetSuspended(bool suspended)
    {
        if (_suspended == suspended) return;
        _suspended = suspended;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (_closed) return;
        if (_suspended || _host.Window.IsVisible) { _hoverTimer.Stop(); Hide(); }
        else { Show(); Place(); }
    }

    private void Place()
    {
        if (_hwnd == IntPtr.Zero) return;
        ScreenService.Place(_hwnd, PanelLayout.Dock(_monitor.WorkArea, _host.Config, _monitor.Scale, _slotCount));
    }
}
