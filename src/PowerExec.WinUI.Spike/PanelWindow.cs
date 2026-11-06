using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using PowerExec.Core.Config;
using PowerExec.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;

namespace PowerExec.WinUI.Spike;

/// <summary>Hintergrund hinter dem Panel.</summary>
public enum SpikeBackdrop { Clear, Acrylic, Mica }

/// <summary>Messwerte eines Ein- oder Ausfahrens.</summary>
public sealed record SlideStats(bool In, int Frames, double Milliseconds, double MaxFrameGapMs);

/// <summary>
/// Panel des Prototyps (P1, P4): rahmenlos, immer oben, gleitet am rechten Rand des Monitors mit dem Mauszeiger
/// ein und aus (Fenster verschieben, nicht Größe ändern; außerhalb des Monitors per Fensterregion abgeschnitten).
/// Hintergrund Klarglas/Acrylic/Mica, Milchglas-Regler, Liste aus dem echten <see cref="PanelViewModel"/>.
/// </summary>
public sealed class PanelWindow : Window, IPanelContext
{
    private const double WidthDip = 440;
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(200);

    private readonly SpikeHost _host;
    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;
    private readonly Grid _root;
    private readonly Border _frost;
    private readonly TextBox _search;
    private readonly ListView _groups;
    private readonly TextBlock _status;
    private readonly TextBlock _dropInfo;
    private readonly DispatcherQueueTimer _hideTimer;
    private bool _closingForGood;
    private bool _pinned;
    private RectInt32 _work;
    private int _widthPx;

    public PanelWindow(SpikeHost host)
    {
        _host = host;
        Title = "Power-Exec (WinUI-Prototyp)";
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
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
        _appWindow.Closing += (_, e) => { if (!_closingForGood) { e.Cancel = true; SlideOut(); } };

        ViewModel = new PanelViewModel(host, this);

        // ---- Aufbau ----------------------------------------------------------------
        _frost = new Border { IsHitTestVisible = false };
        Grid.SetRowSpan(_frost, 3);

        _search = new TextBox { PlaceholderText = "Suchen … (z. B. gds, typ:exe, #tag)", Margin = new Thickness(0, 0, 0, 8) };
        _search.TextChanged += (_, _) => _ = OnSearchChangedAsync(_search.Text);

        var backdrop = new ComboBox { ItemsSource = new[] { "Klarglas", "Acrylic", "Mica" }, SelectedIndex = 1, MinWidth = 120 };
        backdrop.SelectionChanged += (_, _) => SetBackdrop((SpikeBackdrop)backdrop.SelectedIndex);
        var frost = new Slider { Minimum = 0, Maximum = 80, Value = 0, Width = 140, Header = "Milchglas" };
        frost.ValueChanged += (_, e) => SetFrost(e.NewValue);
        var pin = new ToggleButton { Content = "📌", VerticalAlignment = VerticalAlignment.Bottom };
        pin.Checked += (_, _) => _pinned = true;
        pin.Unchecked += (_, _) => _pinned = false;
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        tools.Children.Add(backdrop);
        tools.Children.Add(frost);
        tools.Children.Add(pin);

        var header = new StackPanel { Padding = new Thickness(12, 12, 12, 4) };
        header.Children.Add(new TextBlock { Text = "Power-Exec · WinUI-Prototyp", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        header.Children.Add(_search);
        header.Children.Add(tools);

        _groups = new ListView
        {
            ItemsSource = ViewModel.Groups,
            ItemTemplate = (DataTemplate)XamlReader.Load(GroupTemplate),
            SelectionMode = ListViewSelectionMode.None,
            // Gruppen per Ziehen umsortieren: WinUI öffnet an der Zielstelle selbst eine Lücke (P4)
            CanReorderItems = true,
            CanDragItems = true,
            AllowDrop = true,
            Padding = new Thickness(4, 0, 4, 0)
        };
        _groups.DragItemsCompleted += (_, e) =>
            SpikeLog.Write("Gruppen-Reihenfolge: " + string.Join(", ", ViewModel.Groups.Select(g => g.Title)));
        Grid.SetRow(_groups, 1);

        _status = new TextBlock { FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        _dropInfo = new TextBlock { FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Text = "Dateien, Ordner oder Links hierher ziehen." };
        var footer = new StackPanel { Padding = new Thickness(12, 6, 12, 10), Spacing = 2 };
        footer.Children.Add(_status);
        footer.Children.Add(_dropInfo);
        Grid.SetRow(footer, 2);

        _root = new Grid { AllowDrop = true };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.Children.Add(_frost);
        _root.Children.Add(header);
        _root.Children.Add(_groups);
        _root.Children.Add(footer);
        Content = _root;

        // Ablegen aus Explorer und Browser (P4) – auf der Fläche und auf der Gruppenliste
        _root.DragOver += OnDragOver;
        _root.Drop += OnDrop;
        _groups.DragOver += OnDragOver;
        _groups.Drop += OnDrop;

        // Automatisch einfahren, wenn die Maus das Panel verlässt (außer angeheftet)
        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(700);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => { if (!_pinned && !IsPointerOver()) SlideOut(); };
        _root.PointerEntered += (_, _) => _hideTimer.Stop();
        _root.PointerExited += (_, _) => { if (IsShown) _hideTimer.Start(); };
        _root.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) SlideOut(); };

        SetBackdrop(SpikeBackdrop.Acrylic);
        ViewModel.Rebuild();
        UpdateStatus();
    }

    public PanelViewModel ViewModel { get; }
    public PanelConfig Config { get; } = new() { Edge = ScreenEdge.Right };
    public IntPtr Handle => _hwnd;
    public bool IsShown { get; private set; }
    public SpikeBackdrop CurrentBackdrop { get; private set; }
    public List<double> TypingSamples { get; } = new();
    public List<SlideStats> Slides { get; } = new();
    public TextBox SearchBox => _search;

    // ---- Hintergrund ---------------------------------------------------------------

    /// <summary>Klarglas (durchsichtig, getönt), Acrylic (weichgezeichnet) oder Mica (Desktopfarbe).</summary>
    public void SetBackdrop(SpikeBackdrop backdrop)
    {
        CurrentBackdrop = backdrop;
        SystemBackdrop = backdrop switch
        {
            SpikeBackdrop.Clear => new WinUIEx.TransparentTintBackdrop { TintColor = Color.FromArgb(0x00, 0, 0, 0) },
            SpikeBackdrop.Mica => new Microsoft.UI.Xaml.Media.MicaBackdrop(),
            _ => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop()
        };
        // Klarglas: leichte Tönung, damit die Schrift lesbar bleibt
        _root.Background = backdrop == SpikeBackdrop.Clear
            ? new SolidColorBrush(Color.FromArgb(0x66, 0x20, 0x20, 0x20))
            : new SolidColorBrush(Colors.Transparent);
        SpikeLog.Write($"Hintergrund: {backdrop} ({SystemBackdrop?.GetType().Name})");
    }

    /// <summary>Milchglas 0–80 %: helle Schicht über dem Hintergrund.</summary>
    public void SetFrost(double percent) =>
        _frost.Background = new SolidColorBrush(Color.FromArgb((byte)(255 * percent / 100), 0xF3, 0xF3, 0xF3));

    // ---- Ein- und Ausfahren ----------------------------------------------------------

    public void Toggle()
    {
        if (IsShown) SlideOut();
        else SlideIn();
    }

    public void SlideIn() => _ = SlideAsync(true);
    public void SlideOut() => _ = SlideAsync(false);

    private bool _sliding;

    /// <summary>
    /// Verschiebt das Fenster je Bildschirmbild (CompositionTarget.Rendering) zwischen „knapp außerhalb“ und
    /// „am Rand“ des Monitors mit dem Mauszeiger. Teile außerhalb des Monitors schneidet eine Fensterregion ab,
    /// damit das Panel nicht auf einem Nachbarmonitor auftaucht.
    /// </summary>
    public async Task<SlideStats?> SlideAsync(bool show)
    {
        if (_sliding || show == IsShown) return null;
        _sliding = true;
        try
        {
            if (show) PlaceOnCursorMonitor();
            int shownX = _work.X + _work.Width - _widthPx;
            int hiddenX = _work.X + _work.Width;
            int from = show ? hiddenX : shownX, to = show ? shownX : hiddenX;
            if (show)
            {
                MoveTo(from);
                _appWindow.Show(true);
                Activate();
            }

            var tcs = new TaskCompletionSource();
            var sw = Stopwatch.StartNew();
            int frames = 0;
            double last = 0, maxGap = 0;
            void OnFrame(object? sender, object e)
            {
                var t = Math.Min(1, sw.Elapsed.TotalMilliseconds / SlideDuration.TotalMilliseconds);
                var eased = 1 - Math.Pow(1 - t, 3); // kubisch ausklingend
                MoveTo((int)Math.Round(from + (to - from) * eased));
                frames++;
                maxGap = Math.Max(maxGap, sw.Elapsed.TotalMilliseconds - last);
                last = sw.Elapsed.TotalMilliseconds;
                if (t >= 1)
                {
                    CompositionTarget.Rendering -= OnFrame;
                    tcs.TrySetResult();
                }
            }
            CompositionTarget.Rendering += OnFrame;
            await tcs.Task;

            IsShown = show;
            if (show)
            {
                ClearRegion();
                _search.Focus(FocusState.Programmatic);
            }
            else _appWindow.Hide();
            var stats = new SlideStats(show, frames, sw.Elapsed.TotalMilliseconds, maxGap);
            Slides.Add(stats);
            SpikeLog.Write($"Gleiten {(show ? "ein" : "aus")}: {frames} Bilder in {stats.Milliseconds:0} ms, größte Lücke {maxGap:0} ms");
            UpdateStatus();
            return stats;
        }
        finally
        {
            _sliding = false;
        }
    }

    /// <summary>Arbeitsbereich (ohne Taskleiste) und Breite in Pixeln für den Monitor unter dem Mauszeiger.</summary>
    private void PlaceOnCursorMonitor()
    {
        Native.GetCursorPos(out var pt);
        var area = DisplayArea.GetFromPoint(new PointInt32(pt.X, pt.Y), DisplayAreaFallback.Primary);
        _work = area.WorkArea;
        var dpi = Native.GetDpiForMonitorAt(pt.X, pt.Y);
        _widthPx = (int)Math.Round(WidthDip * dpi / 96.0);
        _appWindow.Resize(new SizeInt32(_widthPx, _work.Height));
    }

    private void MoveTo(int x)
    {
        _appWindow.Move(new PointInt32(x, _work.Y));
        // Sichtbar nur der Teil innerhalb des Arbeitsbereichs
        int visible = Math.Clamp(_work.X + _work.Width - x, 0, _widthPx);
        Native.SetWindowRegion(_hwnd, 0, 0, visible, _work.Height);
    }

    private void ClearRegion() => Native.ClearWindowRegion(_hwnd);

    private bool IsPointerOver()
    {
        Native.GetCursorPos(out var pt);
        var pos = _appWindow.Position;
        var size = _appWindow.Size;
        return pt.X >= pos.X && pt.X < pos.X + size.Width && pt.Y >= pos.Y && pt.Y < pos.Y + size.Height;
    }

    // ---- Suche und Messung -------------------------------------------------------------

    private async Task OnSearchChangedAsync(string text)
    {
        var sw = Stopwatch.StartNew();
        ViewModel.SearchText = text;
        await UiIdle.NextAsync(DispatcherQueue);
        TypingSamples.Add(sw.Elapsed.TotalMilliseconds);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var count = ViewModel.AllItems.Count();
        var typing = TypingSamples.Count == 0 ? "noch nicht getippt"
            : $"Tippen Ø {TypingSamples.Average():0} ms · p95 {Percentile(TypingSamples, 95):0} ms ({TypingSamples.Count}×)";
        var slide = Slides.LastOrDefault() is { } s ? $" · Gleiten {s.Frames} Bilder/{s.Milliseconds:0} ms" : string.Empty;
        _status.Text = $"{count} Einträge · {typing}{slide} · {CurrentBackdrop}";
    }

    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var idx = (int)Math.Ceiling(p / 100 * sorted.Count) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }

    // ---- Ablegen -------------------------------------------------------------------

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.WebLink) ||
            e.DataView.Contains(StandardDataFormats.Uri) || e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = "Als Eintrag übernehmen";
            e.Handled = true;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true; // Gruppenliste und Fläche teilen sich den Handler; das Ereignis steigt sonst weiter auf
        var targets = await DropTargetsAsync(e.DataView);
        if (targets.Count == 0) return;
        _host.AddDroppedPaths(targets);
        ViewModel.Rebuild();
        _dropInfo.Text = $"Abgelegt ({targets.Count}): {string.Join(", ", targets.Take(3))}{(targets.Count > 3 ? " …" : string.Empty)}";
        UpdateStatus();
    }

    /// <summary>Pfade oder Adressen aus einem Ablegen (Dateien/Ordner, Links, Text).</summary>
    public static async Task<IReadOnlyList<string>> DropTargetsAsync(DataPackageView data)
    {
        var list = new List<string>();
        if (data.Contains(StandardDataFormats.StorageItems))
            foreach (var item in await data.GetStorageItemsAsync())
                if (item is IStorageItem s && !string.IsNullOrEmpty(s.Path)) list.Add(s.Path);
        if (list.Count == 0 && data.Contains(StandardDataFormats.WebLink))
            list.Add((await data.GetWebLinkAsync()).ToString());
        if (list.Count == 0 && data.Contains(StandardDataFormats.Uri))
            list.Add((await data.GetUriAsync()).ToString());
        if (list.Count == 0 && data.Contains(StandardDataFormats.Text))
        {
            var text = (await data.GetTextAsync()).Trim();
            if (text.Length > 0) list.Add(text);
        }
        return list;
    }

    public void CloseForGood()
    {
        _closingForGood = true;
        Close();
    }

    // ---- Vorlagen (zur Laufzeit geladen, {Binding} auf die ViewModels aus UI.Core) ----

    private const string GroupTemplate = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <StackPanel Padding="2,8,2,4">
            <Grid Margin="4,0,4,4">
              <TextBlock Text="{Binding Title}" FontWeight="SemiBold" />
              <TextBlock Text="{Binding CountText}" HorizontalAlignment="Right" Opacity="0.6" />
            </Grid>
            <ItemsRepeater ItemsSource="{Binding Items}">
              <ItemsRepeater.ItemTemplate>
                <DataTemplate>
                  <Grid Padding="6,4" ColumnSpacing="8">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="40" />
                      <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <Border CornerRadius="3" Height="20" Background="{ThemeResource SubtleFillColorSecondaryBrush}">
                      <TextBlock Text="{Binding BadgeText}" FontSize="10" HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <StackPanel Grid.Column="1">
                      <TextBlock Text="{Binding DisplayName}" TextTrimming="CharacterEllipsis" />
                      <TextBlock Text="{Binding Synopsis}" FontSize="11" Opacity="0.65" TextTrimming="CharacterEllipsis" />
                    </StackPanel>
                  </Grid>
                </DataTemplate>
              </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>
          </StackPanel>
        </DataTemplate>
        """;
}

/// <summary>Warten, bis die Oberfläche nach einer Änderung gezeichnet und wieder frei ist.</summary>
public static class UiIdle
{
    public static Task NextAsync(DispatcherQueue queue)
    {
        var tcs = new TaskCompletionSource();
        void OnFrame(object? sender, object e)
        {
            CompositionTarget.Rendering -= OnFrame;
            queue.TryEnqueue(DispatcherQueuePriority.Low, () => tcs.TrySetResult());
        }
        CompositionTarget.Rendering += OnFrame;
        return tcs.Task;
    }
}
