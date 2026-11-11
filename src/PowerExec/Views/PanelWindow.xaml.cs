using PowerExec.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Desktop.Infrastructure;
using PowerExec.Desktop.Interop;
using PowerExec.UI.Services;
using PowerExec.Desktop.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.Desktop.Views;

public partial class PanelWindow : Window
{
    private readonly AppController _app;
    private readonly PanelHost _host;
    private PanelViewModel Vm => _host.ViewModel;
    private PanelConfig Cfg => _host.Config;
    private readonly DispatcherTimer _poll;
    private IntPtr _hwnd;
    private MonitorInfo? _monitor;
    private System.Drawing.Rectangle _rect;
    private DateTime? _outsideSince;
    private bool _hiding;

    private bool _isOpen;

    /// <summary>Ausgefahren (auch während des Ein-/Ausfahrens). Bei Glas bleibt das Fenster eingefahren sichtbar geparkt –
    /// „offen“ ist deshalb nicht dasselbe wie <see cref="UIElement.IsVisible"/>.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (_isOpen == value) return;
            _isOpen = value;
            OpenChanged?.Invoke();
        }
    }

    /// <summary>Das Panel wurde aus- bzw. ganz eingefahren.</summary>
    public event Action? OpenChanged;

    public bool OpenedViaHotkey { get; private set; }

    public PanelWindow(AppController app, PanelHost host)
    {
        _app = app;
        _host = host;
        InitializeComponent();
        // Weichzeichner-Varianten: Fenster ohne Transparenz-Schicht, Windows zeichnet dahinter und rundet die Ecken
        _backdrop = PanelBackdrops.Effective(host.Config.EffectiveBackdropSetting, Environment.OSVersion.Version.Build, ThemeService.IsHighContrast,
            ThemeService.TransparencyEffects());
        _systemWindow = PanelBackdrops.NeedsSystemWindow(_backdrop);
        _glass = new PanelGlassHost(this, _backdrop, _systemWindow);
        _appearance = new PanelAppearance(this, app, () => Cfg, () => Vm.IsDarkSurface, _backdrop, _glass);
        if (_systemWindow)
        {
            AllowsTransparency = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
            // Größe bestimmt allein das Fenster (immer so groß wie das Panel, beim Ein-/Ausfahren wird es nur verschoben)
            ClearValue(WidthProperty);
            ClearValue(HeightProperty);
        }
        DataContext = host.ViewModel;
        DetachPeekCard();
        HeaderLogo.Source = Infrastructure.AppIcon.Frame(52); // scharf auch bei 200 % Skalierung
        // Geteilte Kontextmenüs (ItemMenu, GroupMenu): beim Rechtsklick an das angeklickte Element binden
        AddHandler(ContextMenuOpeningEvent, new ContextMenuEventHandler(OnContextMenuOpening));
        host.ViewModel.ScrollToGroupRequested += ScrollToGroup;
        // Der Abgleich beim Tippen meldet viele kleine Änderungen: Sticky-Header nur einmal danach neu bestimmen
        host.ViewModel.Groups.CollectionChanged += (_, _) => ScheduleStickyUpdate();
        host.ViewModel.Rebuilt += ScheduleStickyUpdate;
        ApplyAppearance();

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        _glass.Attach(_hwnd);
        _slide = new PanelSlideController(this, _hwnd, _systemWindow, Slide, PanelBorder);
        NativeMethods.MakeToolWindow(_hwnd, noActivate: false);
        PanelWindowNative.DisableTransitions(_hwnd);
        if (_systemWindow) NativeMethods.RemoveCaptionButtons(_hwnd); // keine Fensterknöpfe von Windows (Schließen-X)
        if (_systemWindow && HwndSource.FromHwnd(_hwnd) is { } src)
        {
            if (src.CompositionTarget != null)
            {
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
            src.AddHook(SystemWindowHook);
        }
        _appearance.ApplyBackdrop();
        if (GlassActive)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _glass.HostContent(WireHostedContent);
            _appearance.ApplyFrost(); // Weichzeichner läuft erst mit dem Fenster-Handle: Schichten neu verteilen
        }

        // Auto-Einfahren: der Zeitgeber läuft nur, solange der Mauszeiger draußen sein kann. Steht er im Panel,
        // ruht der Zeitgeber, bis das Verlassen (MouseLeave) ihn wieder weckt.
        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _poll.Tick += (_, _) => CheckAutoHide();
        MouseLeave += (_, _) => WatchCursor();

        Deactivated += (_, _) => OnDeactivated();
        PreviewKeyDown += OnPanelPreviewKeyDown;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (IsOpen || IsParked) Place(); });
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

        // Monitor immer frisch abfragen: Hover-Zonen reichen den Stand vom Anlegen weiter, Auflösung, Arbeitsbereich
        // (Taskleiste) oder Skalierung können sich seither geändert haben.
        var live = ScreenService.Refresh(monitor, Cfg);
        var sameMonitor = _monitor?.Index == live.Index;
        _monitor = live;
        // Geparkt steht das Fenster nur dann schon richtig, wenn die Ziellage noch dieselbe ist
        var geometryChanged = ScreenService.PanelRect(live, Cfg) != _rect;
        _hiding = false;
        // Für „Text einfügen“: das Fenster merken, in dem vorher gearbeitet wurde
        var fg = NativeMethods.GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != _hwnd) Services.ForegroundTracker.Remember(fg);
        if (_systemWindow && !IsVisible)
        {
            // Fenster beginnt eingefahren hinter dem Rand des Arbeitsbereichs
            _slide.SetNow(0);
        }
        var wasVisible = IsVisible;
        // Geparkt auf demselben Monitor mit unveränderter Ziellage steht das Fenster schon genau bei Reveal = 0 –
        // kein SetWindowPos vor dem Start
        if (!wasVisible || !sameMonitor || geometryChanged)
        {
            Place();
            if (!wasVisible)
            {
                Show();
                _glass.EnsureTransparentBackground(); // die Zeichenfläche entsteht erst mit dem ersten Anzeigen
                Place(); // WPF kann beim ersten Anzeigen die Größe zurücksetzen – nur dann ein zweites Mal
            }
        }
        else if (_systemWindow) _glass.Resize(_rect.Width, _rect.Height); // geparkt: Kindfenster trotzdem verankern
        if (!wasVisible && _glass.HasContentHost)
        {
            // Glas: das Kindfenster erst fertig zeichnen lassen – sonst fällt das Zeichnen in den schnellen Anfang des Ausfahrens
            PanelBorder.UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        }
        if (_systemWindow) PanelWindowNative.ShowAsActive(_hwnd);
        IsOpen = true;
        _poll.Start();
        if (!wasVisible && _glass.HasContentHost && !_skipNextAnimation)
        {
            // Glas: erstes Zeichnen des Kindfensters zuerst – die Bewegung startet erst danach
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => { if (IsOpen && !_hiding) Animate(show: true); });
            return;
        }
        // Fokus (Vordergrund, Tastatur, Textauswahl) erst nach dem Ausfahren – siehe OnSlideInFinished
        Animate(show: true);
    }

    /// <summary>
    /// Nach dem Ausfahren: Hintergrundprüfungen (Erreichbarkeit der Einträge) und Fokus erst jetzt – während des Gleitens
    /// würden sie den UI-Thread belasten.
    /// </summary>
    private void OnSlideInFinished()
    {
        if (IsOpen && !_hiding)
        {
            // Endlage absichern: hat inzwischen jemand das Fenster verschoben oder ist das Kindfenster verrutscht,
            // stünde das Panel sonst bis zum nächsten Platzieren nur teilweise im Bild
            _slide.Settle();
            if (_systemWindow) _glass.Resize(); // Kindfenster (Glas) wieder bei (0, 0) in voller Größe
            Vm.OnShown();
            // Per Tastenkürzel geöffnet: Fokus ins Suchfeld erst jetzt – während des Gleitens bleibt der UI-Thread frei
            if (OpenedViaHotkey) FocusSearch();
        }
    }

    private void FocusSearch()
    {
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    /// <summary>Für den Selbsttest: läuft der Zeitgeber fürs Auto-Einfahren?</summary>
    internal bool AutoHideTimerRunning => _poll.IsEnabled;

    /// <summary>Fenster endgültig schließen (Panel entfernt oder Einstellungen neu angewendet).</summary>
    public void CloseForGood()
    {
        _poll.Stop();
        IsOpen = false;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _peekWindow?.Close();
        _peekWindow = null;
        _glass.Dispose();
        Close();
    }

    /// <summary>
    /// Einstellungen wurden ersetzt (gleiche Fensterart): Aussehen, Weichzeichner und Lage neu anwenden – das Panel bleibt
    /// dabei sichtbar. Liegt der bisherige Monitor nicht mehr in der Auswahl, wechselt es auf den ersten passenden.
    /// </summary>
    public void ApplyConfigChange()
    {
        ApplyAppearance(); // samt Hintergrund, Milchglas und Weichzeichner
        // Geparkt (Glas, eingefahren): neu platzieren – beim nächsten Ausfahren wird Place() übersprungen
        if (IsParked && _monitor != null) { Place(); return; }
        if (!IsOpen || _hiding || _monitor == null) return;
        var monitors = ScreenService.TriggerMonitors(Cfg);
        if (!monitors.Any(m => m.Index == _monitor.Index) && monitors.Count > 0) _monitor = monitors[0];
        Place();
        UpdatePeekWindow();
    }

    /// <summary>Fährt gerade ein.</summary>
    internal bool IsHiding => _hiding;

    /// <summary>Monitor, auf dem das Panel zuletzt gezeigt wurde.</summary>
    internal MonitorInfo? CurrentMonitor => _monitor;

    private bool _skipNextAnimation;

    /// <summary>
    /// Nach dem Neuaufbau (geänderte Einstellungen) sofort wieder zeigen: ohne Gleiten und ohne Fokus – die Einstellungen
    /// bleiben vorn, das Panel zeigt gleich die Wirkung.
    /// </summary>
    public void ShowInPlace(MonitorInfo monitor, bool openedViaHotkey)
    {
        _skipNextAnimation = true;
        ShowPanel(monitor, viaHotkey: false);
        _skipNextAnimation = false;
        OpenedViaHotkey = openedViaHotkey;
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

    // ---- Aussehen: Farben, Milchglas, Glas (siehe Panel/PanelAppearance.cs und Panel/PanelGlassHost.cs) ----------

    private readonly PanelBackdrop _backdrop;
    private readonly bool _systemWindow;
    private readonly PanelGlassHost _glass;
    private readonly PanelAppearance _appearance;

    /// <summary>Panelfarbe, Deckkraft, Akzente, Milchglas und Weichzeichner anwenden.</summary>
    public void ApplyAppearance() => _appearance.Apply();

    /// <summary>Tatsächlich laufende Variante (nach Rückfall für Kontrastdesign bzw. alte Einstellungen).</summary>
    internal PanelBackdrop EffectiveBackdrop => _backdrop;

    /// <summary>Für den Selbsttest: läuft der eigene Weichzeichner?</summary>
    internal bool GlassActive => _glass.GlassActive;

    /// <summary>Für den Selbsttest: Fehler beim Aufbau des Weichzeichners (null = keiner).</summary>
    internal string? GlassError => _glass.GlassError;

    /// <summary>Für den Selbsttest: liegt der Inhalt im durchsichtigen Kindfenster über dem Weichzeichner?</summary>
    internal bool ContentInChildWindow => _glass.ContentInChildWindow;

    /// <summary>Für den Selbsttest: Größe von Fensterinhalt und Kindfenster in Pixeln (Kindfenster null = keins).</summary>
    internal ((int W, int H) Client, (int W, int H)? Content) SizesForTest() => _glass.Sizes();

    /// <summary>Für den Selbsttest: Tönung des Weichzeichners (Panelfarbe samt Deckkraft).</summary>
    internal Color GlassTint => _appearance.GlassTint;

    /// <summary>Für den Selbsttest: Deckkraft der Helligkeitsschicht (0 = kein Milchglas).</summary>
    internal double FrostBrightnessAlpha => _appearance.FrostBrightnessAlpha;

    /// <summary>Glas: Ereignisse, die bisher am Fenster ankamen, am Inhalt im Kindfenster anhängen.</summary>
    private void WireHostedContent(FrameworkElement root)
    {
        root.PreviewKeyDown += OnPanelPreviewKeyDown;
        root.MouseLeave += (_, _) => WatchCursor(); // der Inhalt liegt im Kindfenster – auch dort das Verlassen melden
        root.AddHandler(ContextMenuOpeningEvent, new ContextMenuEventHandler(OnContextMenuOpening));
    }

    /// <summary>
    /// Windows zeigt Hintergründe eines Systemfensters nur für „aktive“ Fenster, sonst eine schlichte Ersatzfarbe. Das Panel wird oft
    /// ohne Fokus gezeigt (Hover-Zone) – es soll trotzdem wie aktiv aussehen.
    /// </summary>
    private IntPtr SystemWindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_NCACTIVATE && wParam == IntPtr.Zero)
        {
            handled = true;
            return NativeMethods.DefWindowProc(hwnd, msg, 1, lParam);
        }
        // Beim Gleiten liegt das Fenster kurz auf dem Nachbarmonitor (dort weggeschnitten). Hat der eine andere
        // Skalierung, meldet Windows einen DPI-Wechsel – WPF würde Fenster und Inhalt mitten im Gleiten umskalieren.
        // Das Panel gehört aber zu seinem Monitor: Wechsel während des Gleitens übergehen.
        // Eine echte Änderung der Skalierung des eigenen Monitors (neuer Wert = dessen Skalierung, aber nicht die, mit der
        // WPF gerade zeichnet) geht dagegen an WPF; DpiChanged platziert danach neu.
        if (msg == NativeMethods.WM_DPICHANGED && _slide.Clips && _slide.IsSliding)
        {
            var announced = (int)((long)wParam & 0xFFFF);
            var home = _monitor != null ? (int)Math.Round(_monitor.Scale * 96) : announced;
            var current = (int)Math.Round(VisualTreeHelper.GetDpi(this).PixelsPerInchX);
            if (announced != home || announced == current)
            {
                handled = true;
                return IntPtr.Zero;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Einstellungen oder ein Dialog von Power-Exec ist aktiv: das (angeheftete) Panel nicht mehr darüber legen,
    /// danach wieder „immer im Vordergrund“.
    /// </summary>
    public void SetBehindAppWindows(bool behind)
    {
        if (_hwnd == IntPtr.Zero || Topmost == !behind) return;
        Topmost = !behind;
        NativeMethods.SetWindowPos(_hwnd, behind ? NativeMethods.HWND_NOTOPMOST : NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
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
        var dpi = VisualTreeHelper.GetDpi(this);
        _slide.Prepare(_rect, _monitor.WorkArea, Cfg.Edge, ScreenService.GetMonitors().Select(m => m.Bounds),
            cornerRadius: (int)Math.Round(PanelBorder.CornerRadius.TopLeft * dpi.DpiScaleX));
        if (_systemWindow)
        {
            // Panelrahmen so groß wie das Fenster; die Lage beim Ein-/Ausfahren setzt der PanelSlideController
            // Nur bei echter Änderung setzen – sonst stößt jedes Zeigen einen neuen Layoutdurchlauf an
            var targetW = _rect.Width / dpi.DpiScaleX;
            var targetH = _rect.Height / dpi.DpiScaleY;
            if (double.IsNaN(PanelBorder.Width) || Math.Abs(PanelBorder.Width - targetW) > 0.5) PanelBorder.Width = targetW;
            if (double.IsNaN(PanelBorder.Height) || Math.Abs(PanelBorder.Height - targetH) > 0.5) PanelBorder.Height = targetH;
            // Inhalt füllt immer die ganze Fläche (Fenster bzw. Kindfenster) – kein leerer Glas-Bereich daneben
            PanelBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
            PanelBorder.VerticalAlignment = VerticalAlignment.Stretch;
            _slide.Place();
            _glass.Resize(); // Kindfenster (Glas) ausdrücklich auf Fenstergröße – nicht nur über WM_SIZE
        }
        else
        {
            _slide.Place();
            // WPF-Größe angleichen: sonst bliebe der Inhalt bei der Größe aus der XAML
            Width = _rect.Width / dpi.DpiScaleX;
            Height = _rect.Height / dpi.DpiScaleY;
        }
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

    // ---- Ein- und Ausfahren (siehe Panel/PanelSlideController.cs) ----------------------------------------------

    private readonly PanelSlideController _slide;

    /// <summary>Für den Selbsttest: eingefahrenes Fenster für Windows ausgeblendet?</summary>
    internal bool IsCloaked => _slide.IsCloaked;

    /// <summary>
    /// Glas: eingefahren bleibt das Fenster sichtbar hinter dem Rand des Arbeitsbereichs stehen („geparkt“, außerhalb des
    /// Bildschirms bzw. auf einen leeren Fensterbereich zugeschnitten). Würde es versteckt, müsste Windows beim nächsten
    /// Ausfahren Weichzeichner, Composition und Kindfenster neu aufbauen – genau in den ersten, schnellsten Bildern der
    /// Bewegung, und das Ausfahren ruckelte (das Einfahren nicht). Geparkt gleitet es in beide Richtungen gleich.
    /// </summary>
    private bool ParkWhenHidden => _glass.HasContentHost;

    /// <summary>Für den Selbsttest: eingefahren, aber geparkt statt versteckt.</summary>
    internal bool IsParked => ParkWhenHidden && IsVisible && !IsOpen && !_hiding;

    /// <summary>Bildschirme geändert: ein geparktes Fenster könnte plötzlich im Bild stehen – dann lieber verstecken.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => { if (IsParked) Hide(); });

    /// <summary>
    /// Monitore, Arbeitsbereich oder Skalierung haben sich geändert (auch nach Standby oder Entsperren, wenn Windows
    /// Fenster verschoben haben kann): Monitor neu bestimmen und ein offenes bzw. geparktes Panel neu platzieren.
    /// </summary>
    public void OnDisplayChanged()
    {
        if (_monitor == null) return;
        _monitor = ScreenService.Refresh(_monitor, Cfg);
        if (IsParked || (IsOpen && !_hiding))
        {
            Place();
            UpdatePeekWindow();
        }
    }

    /// <summary>Für den Selbsttest: Fenster von außen verschieben, wie es Windows nach Standby oder Monitorwechsel tut.</summary>
    internal void DisplaceForTest(int dx, int dy) => _slide.Displace(dx, dy);

    /// <summary>Für den Selbsttest: steht das ausgefahrene Fenster genau an seiner Ziellage?</summary>
    internal bool IsSettledForTest => _slide.IsSettled;

    private void Animate(bool show)
    {
        var duration = _skipNextAnimation ? TimeSpan.Zero : AnimationDuration;
        _slide.Animate(show, duration, fallbackWidth: Cfg.PanelWidth, completed: show ? OnSlideInFinished : () => { if (_hiding) FinishHide(); });
    }

    private void FinishHide()
    {
        _hiding = false;
        IsOpen = false;
        _peekWindow?.Hide();
        OpenedViaHotkey = false;
        if (ParkWhenHidden)
        {
            // Animation sauber beenden: geparkt steht das Fenster genau bei Reveal = 0
            _slide.SetNow(0);
            // Glas: hinter dem Rand stehen lassen (siehe ParkWhenHidden); den Fokus aber abgeben
            if (IsActive) Services.ForegroundTracker.ReturnFocus();
        }
        else Hide();
        Vm.ResetForNextOpen();
        ListScroll.ScrollToTop();
    }

    /// <summary>Mauszeiger hat das Panel (oder die Vorschau) verlassen: Zeitgeber fürs Auto-Einfahren wecken.</summary>
    private void WatchCursor()
    {
        if (IsOpen && !_hiding && !Cfg.Pinned && !_poll.IsEnabled) _poll.Start();
    }

    private void CheckAutoHide()
    {
        if (!IsOpen || _hiding) { _outsideSince = null; _poll.Stop(); return; }
        // Angeheftet oder per Tastenkürzel geöffnet: Schließen nur über Fokusverlust/Lösen – der Zeitgeber ruht
        if (Cfg.Pinned || OpenedViaHotkey) { _outsideSince = null; _poll.Stop(); return; }
        // Vorübergehende Sperren (Größe ziehen, Dialog, Menü, Tippen): weiter prüfen, sie enden ohne Mausereignis
        if (_resizing || _app.IsDialogOpen || AnyPopupOpen() || (IsActive && Vm.IsSearching)) { _outsideSince = null; return; }

        if (ScreenService.CursorInside(_rect, 2))
        {
            // Im Panel selbst: ruhen, bis MouseLeave meldet
            _outsideSince = null;
            _poll.Stop();
            return;
        }
        // Auslösezone und Vorschau zählen als „drin“, melden das Verlassen aber nicht – dort weiter prüfen
        if (_host.Triggers.Any(t => ScreenService.CursorInside(t.CurrentRect, 2))
            || (_peekWindow is { IsVisible: true } peek && ScreenService.CursorInside(peek.PixelRect, 2)))
        {
            _outsideSince = null;
            return;
        }
        _outsideSince ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - _outsideSince.Value).TotalMilliseconds >= _app.Settings.CollapseDelayMs)
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
}
