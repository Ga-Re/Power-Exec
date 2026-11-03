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
    private readonly PanelHost _host;
    private ViewModels.PanelViewModel Vm => _host.ViewModel;
    private PanelConfig Cfg => _host.Config;
    private readonly DispatcherTimer _poll;
    private IntPtr _hwnd;
    private MonitorInfo? _monitor;
    private System.Drawing.Rectangle _rect;
    private DateTime? _outsideSince;
    private bool _hiding;

    public bool IsOpen { get; private set; }
    public bool OpenedViaHotkey { get; private set; }

    public PanelWindow(AppController app, PanelHost host)
    {
        _app = app;
        _host = host;
        InitializeComponent();
        DataContext = host.ViewModel;
        ApplyAppearance();

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.MakeToolWindow(_hwnd, noActivate: false);
        if (Cfg.Acrylic) Interop.Acrylic.Apply(_hwnd, true);

        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _poll.Tick += (_, _) => CheckAutoHide();

        Deactivated += (_, _) => OnDeactivated();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (Vm.IsSearching) Vm.SearchText = string.Empty;
                else HidePanel();
                e.Handled = true;
            }
            // Strg+V außerhalb eines Textfelds bzw. Strg+Umschalt+V: Zwischenablage als neuen Eintrag übernehmen
            else if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                     (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.FocusedElement is not TextBox))
            {
                e.Handled = true;
                Vm.AddFromClipboardCommand.Execute(null);
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
        // Für „Text einfügen“: das Fenster merken, in dem vorher gearbeitet wurde
        var fg = NativeMethods.GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != _hwnd) Services.ForegroundTracker.Remember(fg);
        Place();
        if (!IsVisible) Show();
        Place(); // WPF kann beim ersten Anzeigen die Größe zurücksetzen
        IsOpen = true;
        _poll.Start();
        Animate(show: true);
        Vm.OnShown();
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

    /// <summary>Fenster endgültig schließen (Panel entfernt oder Einstellungen neu angewendet).</summary>
    public void CloseForGood()
    {
        _poll.Stop();
        IsOpen = false;
        Close();
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
        if (!force && Cfg.Pinned) return;
        HidePanel();
    }

    private static readonly string[] AppearanceKeys =
        ["Bg.Panel", "Bg.Hover", "Bg.Selected", "Bg.Input", "Bg.Chip", "Border.Panel", "Border.Default"];

    /// <summary>
    /// Panelfarbe und Deckkraft anwenden. Bei einer hellen Farbe im dunklen Design (oder umgekehrt)
    /// wird für das Panel das passende Farbschema für Schrift und Symbole verwendet.
    /// </summary>
    public void ApplyAppearance()
    {
        var s = Cfg;
        Resources.MergedDictionaries.Clear();
        foreach (var key in AppearanceKeys) Resources.Remove(key);
        double opacity = s.PanelOpacity / 100.0;
        // Acrylic: Hintergrund muss durchscheinen, damit die Weichzeichnung sichtbar ist
        if (s.Acrylic) opacity = Math.Min(opacity, 0.72);
        Interop.Acrylic.Apply(_hwnd, s.Acrylic);

        if (PanelColors.TryParse(s.PanelColor, out var rgb))
        {
            bool light = PanelColors.IsLight(rgb);
            if (light == ThemeService.IsDark)
                Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(light ? "pack://application:,,,/Themes/Light.xaml" : "pack://application:,,,/Themes/Dark.xaml")
                });
            var d = PanelColors.Derive(rgb);
            Resources["Bg.Panel"] = Solid(rgb, opacity);
            Resources["Bg.Hover"] = Solid(d.Hover, Math.Max(opacity, 0.85));
            Resources["Bg.Selected"] = Solid(d.Selected, Math.Max(opacity, 0.9));
            Resources["Bg.Chip"] = Solid(d.Selected, 1);
            Resources["Bg.Input"] = Solid(d.Input, 1);
            Resources["Border.Panel"] = Solid(d.Border, 1);
            Resources["Border.Default"] = Solid(d.Border, 1);
        }
        else if (opacity < 1 && Application.Current.Resources["Bg.Panel"] is SolidColorBrush themeBrush)
        {
            var c = themeBrush.Color;
            Resources["Bg.Panel"] = Solid(new Rgb(c.R, c.G, c.B), opacity);
        }
    }

    private static SolidColorBrush Solid(Rgb c, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(opacity, 0, 1)), c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    public void ApplySettings()
    {
        ApplyAppearance();
        Vm.ResetForNextOpen();
        if (IsOpen && _monitor != null)
        {
            // Monitor kann sich geändert haben
            _monitor = ScreenService.TriggerMonitors(Cfg)[0];
            Place();
        }
    }

    private void Place()
    {
        if (_monitor == null) return;
        _rect = ScreenService.PanelRect(_monitor, Cfg);
        ScreenService.Place(_hwnd, _rect);
        PlaceGrips();
    }

    // ---- Größe per Ziehen am Innenrand ----------------------------------------

    private System.Drawing.Point _dragOrigin;
    private int _dragStartWidth;
    private int _dragStartHeightPx;
    private bool _resizing;

    /// <summary>Breiten-Griff an der inneren Kante, Höhen-Griff an der freien Kante (je nach Bildschirmrand).</summary>
    private void PlaceGrips()
    {
        WidthGrip.HorizontalAlignment = Cfg.Edge == ScreenEdge.Right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        HeightGrip.VerticalAlignment = Cfg.Edge == ScreenEdge.Bottom ? VerticalAlignment.Top : VerticalAlignment.Bottom;
    }

    private void Grip_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        _dragOrigin = System.Windows.Forms.Cursor.Position;
        _dragStartWidth = Cfg.PanelWidth;
        _dragStartHeightPx = _rect.Height;
        _resizing = true;
    }

    private void Grip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (_monitor == null) return;
        var now = System.Windows.Forms.Cursor.Position;
        double scale = _monitor.Scale;
        if (sender == WidthGrip)
        {
            int dx = now.X - _dragOrigin.X;
            if (WidthGrip.HorizontalAlignment == HorizontalAlignment.Left) dx = -dx;
            Cfg.PanelWidth = Math.Clamp(_dragStartWidth + (int)Math.Round(dx / scale), 280, 900);
        }
        else
        {
            int dy = now.Y - _dragOrigin.Y;
            if (HeightGrip.VerticalAlignment == VerticalAlignment.Top) dy = -dy;
            int available = _monitor.WorkArea.Height - 2 * (int)Math.Round(8 * scale);
            if (available <= 0) return;
            Cfg.PanelHeightPercent = Math.Clamp((int)Math.Round((_dragStartHeightPx + dy) * 100.0 / available), 30, 100);
        }
        Place();
    }

    private void Grip_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _resizing = false;
        _app.SaveSettings();
    }

    private void Workspace_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 1 && e.AddedItems[0] is string name &&
            !name.Equals(Vm.ActiveWorkspace, StringComparison.CurrentCultureIgnoreCase))
            Dispatcher.BeginInvoke(() => Vm.SwitchWorkspaceCommand.Execute(name));
    }

    private void Animate(bool show)
    {
        var duration = AnimationDuration;
        var edge = Cfg.Edge;
        double distance = edge is ScreenEdge.Left or ScreenEdge.Right
            ? (ActualWidth > 0 ? ActualWidth : Cfg.PanelWidth) + 24
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
        Vm.ResetForNextOpen();
        ListScroll.ScrollToTop();
    }

    private void CheckAutoHide()
    {
        if (!IsOpen || _hiding || Cfg.Pinned || _resizing || _app.IsDialogOpen) { _outsideSince = null; return; }
        if (AnyPopupOpen()) { _outsideSince = null; return; }
        // Per Tastenkürzel geöffnet oder aktiv am Tippen: Schließen erst beim Fokusverlust.
        if (OpenedViaHotkey || (IsActive && Vm.IsSearching)) { _outsideSince = null; return; }

        bool inside = ScreenService.CursorInside(_rect, 2) || _host.Triggers.Any(t => ScreenService.CursorInside(t.CurrentRect, 2));
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
        if (!IsOpen || Cfg.Pinned) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!IsOpen || IsActive || AnyPopupOpen() || _app.IsDialogOpen) return;
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

    // ---- Drag & Drop: Reihenfolge innerhalb einer Gruppe ----------------------

    private const string DragFormat = "PowerExec.Item";
    private Point _dragStart;
    private ViewModels.ItemViewModel? _dragCandidate;

    private void Row_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragCandidate = (sender as FrameworkElement)?.DataContext as ViewModels.ItemViewModel;
    }

    private void Row_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate == null || Vm.IsSearching) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
        var item = _dragCandidate;
        _dragCandidate = null;
        Mouse.Capture(null);
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, item), DragDropEffects.Move);
    }

    /// <summary>Einfügen hinter dem Ziel: in der Liste untere Hälfte, im Raster rechte Hälfte.</summary>
    private bool InsertAfter(FrameworkElement row, DragEventArgs e) => Vm.IsGrid
        ? e.GetPosition(row).X > row.ActualWidth / 2
        : e.GetPosition(row).Y > row.ActualHeight / 2;

    private Thickness InsertMarker(FrameworkElement row, DragEventArgs e) => Vm.IsGrid
        ? (InsertAfter(row, e) ? new Thickness(0, 0, 2, 0) : new Thickness(2, 0, 0, 0))
        : (InsertAfter(row, e) ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0));

    private ViewModels.GroupViewModel? GroupOf(ViewModels.ItemViewModel item) => Vm.AllGroups.FirstOrDefault(g => g.Items.Contains(item));

    private void ClearSelection_Click(object sender, RoutedEventArgs e) => Vm.ClearSelection();

    // ---- Drag & Drop: Gruppen verschieben und verschachteln ---------------------

    private const string GroupDragFormat = "PowerExec.Group";
    private Point _groupDragStart;
    private ViewModels.GroupViewModel? _groupDragCandidate;

    private void GroupHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _groupDragStart = e.GetPosition(this);
        _groupDragCandidate = (sender as FrameworkElement)?.DataContext is ViewModels.GroupViewModel { IsUserGroup: true } g ? g : null;
    }

    private void GroupHeader_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _groupDragCandidate == null || Vm.IsSearching) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _groupDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _groupDragStart.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
        var group = _groupDragCandidate;
        _groupDragCandidate = null;
        Mouse.Capture(null);
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(GroupDragFormat, group), DragDropEffects.Move);
    }

    /// <summary>Oberes Viertel = davor, unteres Viertel = dahinter, Mitte = hinein (Untergruppe).</summary>
    private static Core.Config.GroupDropPosition GroupPosition(FrameworkElement header, DragEventArgs e)
    {
        var y = e.GetPosition(header).Y;
        var h = Math.Max(1, header.ActualHeight);
        return y < h * 0.28 ? Core.Config.GroupDropPosition.Before
             : y > h * 0.72 ? Core.Config.GroupDropPosition.After
             : Core.Config.GroupDropPosition.Into;
    }

    private void GroupHeader_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border header || !e.Data.GetDataPresent(GroupDragFormat)) return; // Einträge/Dateien: übernimmt die Gruppe
        var dragged = (ViewModels.GroupViewModel)e.Data.GetData(GroupDragFormat)!;
        var target = header.DataContext as ViewModels.GroupViewModel;
        bool allowed = target is { IsUserGroup: true } && !ReferenceEquals(target, dragged) &&
                       !Core.Config.GroupManager.IsSameOrBelow(target.GroupPath!, dragged.GroupPath!);
        e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        if (allowed)
        {
            var pos = GroupPosition(header, e);
            header.BorderThickness = pos switch
            {
                Core.Config.GroupDropPosition.Before => new Thickness(0, 2, 0, 0),
                Core.Config.GroupDropPosition.After => new Thickness(0, 0, 0, 2),
                _ => new Thickness(1)
            };
            header.Background = pos == Core.Config.GroupDropPosition.Into ? (Brush)FindResource("Bg.Hover") : Brushes.Transparent;
        }
        else
        {
            header.BorderThickness = new Thickness(0);
            header.Background = Brushes.Transparent;
        }
        e.Handled = true;
    }

    private void GroupHeader_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is not Border header) return;
        header.BorderThickness = new Thickness(0);
        header.Background = Brushes.Transparent;
    }

    private void GroupHeader_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border header || !e.Data.GetDataPresent(GroupDragFormat)) return;
        GroupHeader_DragLeave(sender, e);
        e.Handled = true;
        var dragged = (ViewModels.GroupViewModel)e.Data.GetData(GroupDragFormat)!;
        if (header.DataContext is ViewModels.GroupViewModel { IsUserGroup: true } target)
            Vm.MoveGroup(dragged, target, GroupPosition(header, e));
    }

    private void Row_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border row) return;
        if (e.Data.GetDataPresent(SettingsWindow.EntryDragFormat) && row.DataContext is ViewModels.ItemViewModel over && GroupOf(over) is { AcceptsItems: true } overGroup)
        {
            e.Effects = DragDropEffects.Move;
            row.BorderThickness = overGroup.Kind != "fav" ? InsertMarker(row, e) : new Thickness(0);
            e.Handled = true;
            return;
        }
        if (!e.Data.GetDataPresent(DragFormat)) return; // Dateien aus dem Explorer: übernimmt die Gruppe
        var source = (ViewModels.ItemViewModel)e.Data.GetData(DragFormat)!;
        var target = row.DataContext as ViewModels.ItemViewModel;
        var group = target != null ? GroupOf(target) : null;
        bool sameGroup = group != null && group.Items.Contains(source);
        bool allowed = target != null && !ReferenceEquals(source, target) && group != null &&
                       (sameGroup ? group.AllowReorder : group.AcceptsItems);
        e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        // Einfügemarke oben bzw. unten (nicht bei Favoriten: dort wird nur markiert)
        row.BorderThickness = allowed && group!.Kind != "fav" ? InsertMarker(row, e) : new Thickness(0);
        e.Handled = true;
    }

    private void Row_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border row) row.BorderThickness = new Thickness(0);
    }

    private void Row_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border row) return;
        row.BorderThickness = new Thickness(0);
        if (e.Data.GetDataPresent(SettingsWindow.EntryDragFormat) && row.DataContext is ViewModels.ItemViewModel dropTarget && GroupOf(dropTarget) is { } dropGroup)
        {
            e.Handled = true;
            if (e.Data.GetData(SettingsWindow.EntryDragFormat) is string key)
                Vm.MoveKeyToGroup(key, dropGroup, dropGroup.Kind == "fav" ? null : dropTarget, InsertAfter(row, e));
            return;
        }
        if (!e.Data.GetDataPresent(DragFormat) || row.DataContext is not ViewModels.ItemViewModel target) return;
        var source = (ViewModels.ItemViewModel)e.Data.GetData(DragFormat)!;
        var group = GroupOf(target);
        e.Handled = true;
        if (group == null || ReferenceEquals(source, target)) return;
        if (group.Items.Contains(source)) Vm.MoveItem(source, target, InsertAfter(row, e));
        else Vm.MoveToGroup(source, group, group.Kind == "fav" ? null : target, InsertAfter(row, e));
    }

    // ---- Drag & Drop: Gruppen und Dateien aus dem Explorer ----------------------

    private static DragDropEffects ExternalEffect(DragEventArgs e) =>
        e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link
        : e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.None;

    private void Group_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border box || box.DataContext is not ViewModels.GroupViewModel group) return;
        bool allowed;
        if (e.Data.GetDataPresent(GroupDragFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(DragFormat))
        {
            var source = (ViewModels.ItemViewModel)e.Data.GetData(DragFormat)!;
            allowed = group.AcceptsItems && !group.Items.Contains(source);
            e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        }
        else if (e.Data.GetDataPresent(SettingsWindow.EntryDragFormat))
        {
            allowed = group.AcceptsItems;
            e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        }
        else
        {
            allowed = ExternalDrop.HasData(e.Data);
            e.Effects = allowed ? ExternalEffect(e) : DragDropEffects.None;
        }
        if (allowed)
        {
            box.BorderBrush = (Brush)FindResource("Accent");
            box.Background = (Brush)FindResource("Bg.Hover");
        }
        else Group_DragLeave(sender, e);
        e.Handled = true;
    }

    private void Group_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is not Border box) return;
        // zurück zur Gruppenfarbe aus dem Stil
        box.ClearValue(Border.BorderBrushProperty);
        box.ClearValue(Border.BackgroundProperty);
    }

    private void Group_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border box || box.DataContext is not ViewModels.GroupViewModel group) return;
        Group_DragLeave(sender, e);
        e.Handled = true;
        if (e.Data.GetDataPresent(DragFormat))
        {
            Vm.MoveToGroup((ViewModels.ItemViewModel)e.Data.GetData(DragFormat)!, group);
            return;
        }
        if (e.Data.GetData(SettingsWindow.EntryDragFormat) is string entryKey)
        {
            if (group.AcceptsItems) Vm.MoveKeyToGroup(entryKey, group);
            return;
        }
        var targets = ExternalDrop.Read(e.Data);
        if (targets.Count > 0) Vm.DropExternal(targets, group);
    }

    /// <summary>Außerhalb einer Gruppe abgelegt: als eigener Eintrag ohne Gruppe.</summary>
    private void Panel_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(SettingsWindow.EntryDragFormat) ? DragDropEffects.Move
            : !e.Data.GetDataPresent(DragFormat) && !e.Data.GetDataPresent(GroupDragFormat) && ExternalDrop.HasData(e.Data) ? ExternalEffect(e) : DragDropEffects.None;
        e.Handled = true;
    }

    private void Panel_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(SettingsWindow.EntryDragFormat) is string entryKey)
        {
            Vm.DropEntryWithoutGroup(entryKey);
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(DragFormat) || e.Data.GetDataPresent(GroupDragFormat)) return;
        var targets = ExternalDrop.Read(e.Data);
        if (targets.Count > 0) Vm.DropExternal(targets, null);
        e.Handled = true;
    }

    /// <summary>Doppelklick auf einen Eintrag: wie der Ausführen-Knopf (Standardprofil).</summary>
    private void Row_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is FrameworkElement { DataContext: ViewModels.ItemViewModel item })
        {
            e.Handled = true;
            item.RunCommand.Execute(null);
        }
    }

    /// <summary>Pfeil am Ausführen-Knopf: Menü mit allen Startprofilen.</summary>
    private void ProfileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ViewModels.ItemViewModel item } button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var p in item.ProfileMenu)
        {
            var header = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold });
            if (p.IsDefault)
                title.Children.Add(new TextBlock { Text = "Standard", FontSize = 11, Margin = new Thickness(8, 1, 0, 0), Foreground = (Brush)FindResource("Text.Tertiary") });
            if (p.IsAdmin)
                title.Children.Add(new TextBlock { Text = "\uE7EF", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 12, Margin = new Thickness(8, 1, 0, 0), Foreground = (Brush)FindResource("Status.Warning") });
            header.Children.Add(title);
            header.Children.Add(new TextBlock { Text = p.Summary, FontSize = 11, Foreground = (Brush)FindResource("Text.Secondary") });
            var mi = new MenuItem { Header = header, Command = p.Command };
            mi.Height = double.NaN;
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Profile verwalten …", Command = item.ManageProfilesCommand });
        menu.IsOpen = true;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                ScrollIntoView(Vm.MoveSelection(1));
                e.Handled = true;
                break;
            case Key.Up:
                ScrollIntoView(Vm.MoveSelection(-1));
                e.Handled = true;
                break;
            case Key.Enter:
                Vm.RunSelectedOrFirst();
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
