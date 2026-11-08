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
using PowerExec.Infrastructure;
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
        // Weichzeichner-Varianten: Fenster ohne Transparenz-Schicht, Windows zeichnet dahinter und rundet die Ecken
        _backdrop = PanelBackdrops.Effective(host.Config.EffectiveBackdropSetting, Environment.OSVersion.Version.Build, ThemeService.IsHighContrast,
            ThemeService.TransparencyEffects());
        _systemWindow = PanelBackdrops.NeedsSystemWindow(_backdrop);
        if (_systemWindow)
        {
            AllowsTransparency = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
            });
            // Größe bestimmt allein das Fenster (immer so groß wie das Panel, beim Ein-/Ausfahren wird es nur verschoben)
            ClearValue(WidthProperty);
            ClearValue(HeightProperty);
        }
        DataContext = host.ViewModel;
        DetachPeekCard();
        // Geteilte Kontextmenüs (ItemMenu, GroupMenu): beim Rechtsklick an das angeklickte Element binden
        AddHandler(ContextMenuOpeningEvent, new ContextMenuEventHandler(OnContextMenuOpening));
        host.ViewModel.ScrollToGroupRequested += ScrollToGroup;
        // Der Abgleich beim Tippen meldet viele kleine Änderungen: Sticky-Header nur einmal danach neu bestimmen
        host.ViewModel.Groups.CollectionChanged += (_, _) => ScheduleStickyUpdate();
        host.ViewModel.Rebuilt += ScheduleStickyUpdate;
        ApplyAppearance();

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.MakeToolWindow(_hwnd, noActivate: false);
        Interop.PanelWindowNative.DisableTransitions(_hwnd);
        if (_systemWindow) Interop.NativeMethods.RemoveCaptionButtons(_hwnd); // keine Fensterknöpfe von Windows (Schließen-X)
        if (_systemWindow && HwndSource.FromHwnd(_hwnd) is { } src)
        {
            if (src.CompositionTarget != null)
            {
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
            src.AddHook(SystemWindowHook);
        }
        ApplyBackdrop();
        if (GlassActive)
        {
            HostContentInChildWindow();
            ApplyFrost(Cfg.Frost); // Weichzeichner läuft erst mit dem Fenster-Handle: Schichten neu verteilen
        }

        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _poll.Tick += (_, _) => CheckAutoHide();

        Deactivated += (_, _) => OnDeactivated();
        PreviewKeyDown += OnPanelPreviewKeyDown;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (IsOpen) Place(); });
    }

    /// <summary>Tastenkürzel des Panels (am Fenster bzw. bei Glas am Inhalt im Kindfenster).</summary>
    private void OnPanelPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        {
            if (HandleShortcut(e)) e.Handled = true;
            else if (e.Key == Key.Escape)
            {
                if (Vm.ShowGroupJump) { Vm.ShowGroupJump = false; SearchBox.Focus(); }
                else if (Vm.HasMultiSelection && !Vm.ShowPeek && !Vm.ShowHistory) Vm.ClearChecked();
                else if (Vm.ShowPeek) Vm.ClosePeek();
                else if (Vm.ShowHistory) Vm.ShowHistory = false;
                else if (Vm.ShowShortcuts) Vm.ShowShortcuts = false;
                else if (Vm.IsSearching) Vm.SearchText = string.Empty;
                else HidePanel();
                e.Handled = true;
            }
            // Strg+Z: letzte Änderung im Panel zurücknehmen (im Suchfeld mit Text gilt das Rückgängig des Textfelds)
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control &&
                     !(Keyboard.FocusedElement is TextBox { Text.Length: > 0 }) && Vm.TryUndo())
            {
                e.Handled = true;
            }
            // Strg+V außerhalb eines Textfelds bzw. Strg+Umschalt+V: Zwischenablage als neuen Eintrag übernehmen
            else if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                     (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.FocusedElement is not TextBox))
            {
                e.Handled = true;
                Vm.AddFromClipboardCommand.Execute(null);
            }
        }
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
        if (_systemWindow && !IsVisible)
        {
            // Fenster beginnt eingefahren hinter dem Rand des Arbeitsbereichs
            BeginAnimation(RevealProperty, null);
            Reveal = 0;
        }
        Place();
        var wasVisible = IsVisible;
        if (!wasVisible) Show();
        EnsureTransparentBackground(); // die Zeichenfläche entsteht erst mit dem ersten Anzeigen
        Place(); // WPF kann beim ersten Anzeigen die Größe zurücksetzen
        if (!wasVisible && _contentHost is { IsDisposed: false })
        {
            // Glas: das Kindfenster erst fertig zeichnen lassen – sonst fällt das Zeichnen in den schnellen Anfang des Ausfahrens
            PanelBorder.UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        }
        if (_systemWindow) Interop.PanelWindowNative.SendMessage(_hwnd, 0x0086, (IntPtr)1, IntPtr.Zero); // Hintergrund wie aktiv
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

    // ---- Vorschau im eigenen Fenster ----------------------------------------------------------

    private PeekWindow? _peekWindow;

    /// <summary>Vorschau-Karte aus dem Panel lösen: sie erscheint in einem eigenen Fenster neben dem Panel.</summary>
    private void DetachPeekCard()
    {
        if (PeekCard.Parent is Panel parent) parent.Children.Remove(PeekCard);
        PeekCard.Margin = new Thickness(12);
        PeekCloseButton.Command = Vm.ClosePeekCommand;
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModels.PanelViewModel.ShowPeek)) UpdatePeekWindow();
        };
    }

    private void UpdatePeekWindow()
    {
        if (Vm.ShowPeek && IsOpen && !_hiding && _monitor != null)
        {
            _peekWindow ??= new PeekWindow(PeekCard, Resources, this) { DataContext = Vm };
            _peekWindow.PlaceBeside(_rect, _monitor.WorkArea, _monitor.Scale, Cfg.Edge);
            if (!_peekWindow.IsVisible) _peekWindow.Show();
            ScreenService.Place(new WindowInteropHelper(_peekWindow).Handle, _peekWindow.PixelRect);
        }
        else _peekWindow?.Hide();
    }

    /// <summary>Für den Selbsttest: ist das Vorschaufenster sichtbar?</summary>
    internal bool PeekWindowVisible => _peekWindow?.IsVisible == true;

    /// <summary>Fenster endgültig schließen (Panel entfernt oder Einstellungen neu angewendet).</summary>
    public void CloseForGood()
    {
        _poll.Stop();
        IsOpen = false;
        _peekWindow?.Close();
        _peekWindow = null;
        DisposeGlass();
        DisposeContentHost();
        Close();
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

    private static readonly string[] AppearanceKeys =
        ["Bg.Panel", "Bg.Hover", "Bg.Selected", "Bg.Subtle", "Bg.Input", "Bg.Chip", "Border.Panel", "Border.Default"];

    /// <summary>
    /// Panelfarbe und Deckkraft anwenden. Bei einer hellen Farbe im dunklen Design (oder umgekehrt)
    /// wird für das Panel das passende Farbschema für Schrift und Symbole verwendet.
    /// </summary>
    private static bool IsOwnPalette(ResourceDictionary d) =>
        d.Source?.OriginalString is { } src && (src.EndsWith("/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) || src.EndsWith("/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase));

    public void ApplyAppearance()
    {
        var s = Cfg;
        if (PanelBorder.Child is FrameworkElement panelContent) WindowHelper.ApplyContentScale(panelContent);
        // Nur das eigene Gegen-Farbschema entfernen – Themes/Fluent.xaml und das Fluent-Design bleiben eingebunden
        foreach (var d in Resources.MergedDictionaries.Where(IsOwnPalette).ToList()) Resources.MergedDictionaries.Remove(d);
        foreach (var key in AppearanceKeys) Resources.Remove(key);
        double opacity = s.PanelOpacity / 100.0;
        // Deckkraft wie am Regler eingestellt (auch bei hellen Panels)
        if (ThemeService.IsHighContrast) opacity = 1; // Kontrastdesign: deckend, Systemfarben
        ApplyBackdrop();

        if (!ThemeService.IsHighContrast && PanelColors.TryParse(s.PanelColor, out var rgb))
        {
            bool light = PanelColors.IsLight(rgb);
            if (light == ThemeService.IsDark)
            {
                Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(light ? "pack://application:,,,/Themes/Light.xaml" : "pack://application:,,,/Themes/Dark.xaml")
                });
                // Windows-Akzentfarbe auch im Panel mit Gegen-Design behalten (passend aufgehellt bzw. abgedunkelt)
                if (_app.Settings.UseWindowsAccent && ThemeService.WindowsAccent() is { } accent)
                {
                    var a = Appearance.Derive(accent, dark: !light);
                    Resources.MergedDictionaries[^1]["Accent"] = Freeze(new SolidColorBrush(Color.FromRgb(a.Accent.R, a.Accent.G, a.Accent.B)));
                    Resources.MergedDictionaries[^1]["Accent.Hover"] = Freeze(new SolidColorBrush(Color.FromRgb(a.Hover.R, a.Hover.G, a.Hover.B)));
                    Resources.MergedDictionaries[^1]["OnAccent"] = Freeze(new SolidColorBrush(Color.FromRgb(a.OnAccent.R, a.OnAccent.G, a.OnAccent.B)));
                }
            }
            var d = PanelColors.Derive(rgb);
            Resources["Bg.Panel"] = Solid(rgb, opacity);
            Resources["Bg.Hover"] = Solid(d.Hover, HoverAlpha(opacity));
            Resources["Bg.Selected"] = Solid(d.Selected, SelectedAlpha(opacity));
            Resources["Bg.Chip"] = Solid(d.Selected, 1);
            Resources["Bg.Input"] = Solid(d.Input, 1);
            Resources["Border.Panel"] = Solid(d.Border, 1);
            Resources["Border.Default"] = Solid(d.Border, 1);
        }
        else if (opacity < 1 && Application.Current.Resources["Bg.Panel"] is SolidColorBrush themeBrush)
        {
            var c = themeBrush.Color;
            Resources["Bg.Panel"] = Solid(new Rgb(c.R, c.G, c.B), opacity);
            // Glas: auch Auswahl und Hover lassen den Weichzeichner durchscheinen
            if (_backdrop == PanelBackdrop.Glass)
            {
                if (Application.Current.Resources["Bg.Hover"] is SolidColorBrush h)
                    Resources["Bg.Hover"] = Solid(new Rgb(h.Color.R, h.Color.G, h.Color.B), HoverAlpha(opacity));
                if (Application.Current.Resources["Bg.Selected"] is SolidColorBrush sel)
                    Resources["Bg.Selected"] = Solid(new Rgb(sel.Color.R, sel.Color.G, sel.Color.B), SelectedAlpha(opacity));
            }
        }
        // Glas: auch Flächen in den Details (Parameter) lassen den Weichzeichner durchscheinen
        if (_backdrop == PanelBackdrop.Glass && !ThemeService.IsHighContrast && TryFindResource("Bg.Subtle") is SolidColorBrush subtle)
            Resources["Bg.Subtle"] = Solid(new Rgb(subtle.Color.R, subtle.Color.G, subtle.Color.B), HoverAlpha(opacity));
        // Fluent-Bedienelemente (WPF UI): Hell/Dunkel nach der Panelfläche, Flächenfarben setzt das Panel selbst
        Infrastructure.FluentTheme.Attach(this, dark: () => Vm.IsDarkSurface, mapPalette: false);
        ApplyFrost(s.Frost);
    }

    /// <summary>
    /// Deckkraft von Hover- und Auswahlflächen: bei Klarglas fast deckend (Lesbarkeit über unruhigem Desktop), bei Glas
    /// etwas über der Panel-Deckkraft – der Weichzeichner scheint auch durch ausgeklappte Einträge.
    /// </summary>
    private double HoverAlpha(double opacity) => _backdrop == PanelBackdrop.Glass ? Math.Min(1, opacity + 0.15) : Math.Max(opacity, 0.85);

    private double SelectedAlpha(double opacity) => _backdrop == PanelBackdrop.Glass ? Math.Min(1, opacity + 0.25) : Math.Max(opacity, 0.9);

    /// <summary>
    /// Fast unsichtbare Grundfläche (Deckkraft 1/255) für Glas: Windows lässt Klicks und Ablegen in einem durchsichtigen
    /// Kindfenster dort durchfallen, wo die Deckkraft 0 ist – leere Flächen und Gruppenköpfe ohne Farbe wären taub.
    /// </summary>
    private static readonly SolidColorBrush HitSurface = CreateHitSurface();

    private static SolidColorBrush CreateHitSurface()
    {
        var b = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        b.Freeze();
        return b;
    }

    // ---- Hintergrund: Klarglas mit Milchglas ------------------------------------------------

    private readonly PanelBackdrop _backdrop;
    private readonly bool _systemWindow;

    /// <summary>Tatsächlich laufende Variante (nach Rückfall für Kontrastdesign bzw. alte Einstellungen).</summary>
    internal PanelBackdrop EffectiveBackdrop => _backdrop;

    /// <summary>
    /// Klarglas zeichnet WPF selbst (transparentes Fenster, Milchglas in <see cref="ApplyFrost"/>). Glas läuft in einem
    /// Fenster ohne Transparenz-Schicht: dahinter hängt der eigene Weichzeichner (<see cref="Interop.GlassBackdrop"/>).
    /// Scheitert er, bleibt das Fenster durchsichtig und das Milchglas von WPF übernimmt (wie Klarglas).
    /// </summary>
    private void ApplyBackdrop()
    {
        if (_hwnd == IntPtr.Zero || !_systemWindow) return;
        Interop.PanelWindowNative.PrepareSystemWindow(_hwnd, Vm.IsDarkSurface);
        EnsureTransparentBackground();
        if (_backdrop != PanelBackdrop.Glass || GlassError != null || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, PanelBackdrops.GlassBuild)) return;
        try
        {
            _glass ??= new GlassBackdrop(_hwnd);
            UpdateGlass();
        }
        catch (Exception ex)
        {
            // Für Bericht und Fehlersuche: Typ, Meldung und die Stelle, an der es scheiterte
            var frame = new System.Diagnostics.StackTrace(ex).GetFrames().FirstOrDefault(f => f.GetMethod()?.DeclaringType?.Namespace?.StartsWith("PowerExec") == true)
                        ?? new System.Diagnostics.StackTrace(ex).GetFrame(0);
            GlassError = $"{ex.GetType().Name}: {ex.Message} (in {frame?.GetMethod()?.DeclaringType?.Name}.{frame?.GetMethod()?.Name})";
            AppLog.Error("Glas-Weichzeichner nicht verfügbar, Rückfall auf Milchglas", ex);
            DisposeGlass();
        }
    }

    /// <summary>
    /// Werte des Weichzeichners nachziehen: Blur, Sättigung, Helligkeitsschicht (dunkle Fläche Schwarz, helle Weiß) und
    /// Tönung mit der Panelfarbe samt Deckkraft (eigene Farbe bzw. Farbe des hellen oder dunklen Designs).
    /// </summary>
    private void UpdateGlass()
    {
        if (_glass is not GlassBackdrop glass || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, PanelBackdrops.GlassBuild)) return;
        var s = Cfg;
        var dark = Vm.IsDarkSurface;
        var shade = dark ? 0f : 1f;
        var panel = GlassTint;
        glass.Update(PanelBackdrops.GlassBlurRadius(s.GlassBlur), PanelBackdrops.GlassSaturation(s.GlassSaturation),
            new System.Numerics.Vector4(shade, shade, shade, (float)PanelBackdrops.FrostBrightness(s.Frost, dark)),
            new System.Numerics.Vector4(panel.R / 255f, panel.G / 255f, panel.B / 255f, panel.A / 255f));
    }

    /// <summary>Für den Selbsttest: Tönung des Weichzeichners (Panelfarbe samt Deckkraft).</summary>
    internal Color GlassTint => TryFindResource("Bg.Panel") is SolidColorBrush b
        ? Color.FromArgb((byte)Math.Round(b.Color.A * Math.Clamp(b.Opacity, 0, 1)), b.Color.R, b.Color.G, b.Color.B)
        : Colors.Transparent;

    /// <summary>Fenster ohne Transparenz-Schicht: Hintergrund der WPF-Zeichenfläche durchsichtig (entsteht erst beim Anzeigen).</summary>
    private void EnsureTransparentBackground()
    {
        if (!_systemWindow || _hwnd == IntPtr.Zero) return;
        if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
    }

    // ---- Glas: Inhalt im Kindfenster ----------------------------------------------------------
    //
    // Windows setzt die Ebenen eines Fensters so zusammen (DirectComposition): 1. was direkt ins Fenster gezeichnet wird
    // (GDI oder DirectX – also WPF), 2. Composition-Baum (nicht „topmost“), 3. Kindfenster, 4. Composition-Baum (topmost).
    // Der Weichzeichner (2) läge über dem WPF-Inhalt (1). Deshalb zieht der Inhalt bei Glas in ein durchsichtiges
    // Kindfenster (3) um – ab Windows 8 dürfen Kindfenster Transparenz pro Pixel haben. Es bleibt ein einziges Fenster:
    // Gleiten, Zuschnitt und runde Ecken gelten für Weichzeichner und Inhalt gemeinsam.

    private HwndSource? _contentHost;

    /// <summary>Für den Selbsttest: liegt der Inhalt im durchsichtigen Kindfenster über dem Weichzeichner?</summary>
    internal bool ContentInChildWindow => _contentHost is { IsDisposed: false, RootVisual: not null };

    private void HostContentInChildWindow()
    {
        if (_contentHost != null || Content is not FrameworkElement root) return;
        NativeMethods.GetClientRect(_hwnd, out var rc);
        const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
        var parameters = new HwndSourceParameters("PowerExecPanelInhalt")
        {
            ParentWindow = _hwnd,
            WindowStyle = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
            PositionX = 0,
            PositionY = 0,
            Width = Math.Max(1, rc.Right - rc.Left),
            Height = Math.Max(1, rc.Bottom - rc.Top),
            UsesPerPixelTransparency = true
        };
        // Vom Fenster geerbte Werte (Datenkontext, Ressourcen, Schrift) gehen beim Umzug verloren – mitnehmen
        Content = null;
        root.DataContext = DataContext;
        if (root.Resources.Count == 0 && root.Resources.MergedDictionaries.Count == 0) root.Resources = Resources;
        else root.Resources.MergedDictionaries.Insert(0, Resources);
        root.SetBinding(TextElement.FontFamilyProperty, new System.Windows.Data.Binding(nameof(FontFamily)) { Source = this });
        root.SetBinding(TextElement.FontSizeProperty, new System.Windows.Data.Binding(nameof(FontSize)) { Source = this });
        root.SetBinding(TextElement.ForegroundProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = this });
        root.PreviewKeyDown += OnPanelPreviewKeyDown;
        root.AddHandler(ContextMenuOpeningEvent, new ContextMenuEventHandler(OnContextMenuOpening));

        _contentHost = new HwndSource(parameters) { SizeToContent = SizeToContent.Manual, RootVisual = root };
        if (_contentHost.CompositionTarget != null) _contentHost.CompositionTarget.BackgroundColor = Colors.Transparent;
        if (HwndSource.FromHwnd(_hwnd) is { } src) src.AddHook(ChildHostHook);
    }

    /// <summary>Kindfenster immer so groß wie der Fensterinhalt.</summary>
    private IntPtr ChildHostHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SIZE = 0x0005;
        if (msg == WM_SIZE && _contentHost is { IsDisposed: false } host)
        {
            var w = (int)((long)lParam & 0xFFFF);
            var h = (int)(((long)lParam >> 16) & 0xFFFF);
            const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
            NativeMethods.SetWindowPos(host.Handle, IntPtr.Zero, 0, 0, Math.Max(1, w), Math.Max(1, h), SWP_NOZORDER | SWP_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    private void DisposeContentHost()
    {
        if (_contentHost == null) return;
        _contentHost.RootVisual = null;
        _contentHost.Dispose();
        _contentHost = null;
    }

    /// <summary>Eigener Weichzeichner (nur bei Glas unter Windows 11; als IDisposable, damit ältere Windows den Typ nie anfassen).</summary>
    private IDisposable? _glass;

    private void DisposeGlass()
    {
        _glass?.Dispose();
        _glass = null;
    }

    /// <summary>Für den Selbsttest: läuft der eigene Weichzeichner?</summary>
    internal bool GlassActive => _glass != null;

    /// <summary>Für den Selbsttest: Fehler beim Aufbau des Weichzeichners (null = keiner).</summary>
    internal string? GlassError { get; private set; }

    /// <summary>
    /// Milchglas: von unten Helligkeitsschicht (dunkle Fläche Schwarz, helle Weiß; Stärke vom Milchglas-Regler),
    /// darüber die Panelfarbe als Tönung (mit der eingestellten Deckkraft). Ohne Milchglas
    /// (Regler 0, Kontrastdesign) trägt der Panelrahmen die Panelfarbe wie bisher.
    /// </summary>
    private void ApplyFrost(int frost)
    {
        if (GlassActive)
        {
            // Glas: Helligkeitsschicht und Tönung liegen im Weichzeichner
            PanelBorder.Background = HitSurface;
            FrostBrightness.Background = null;
            FrostTint.Background = null;
            FrostLayer.Visibility = Visibility.Collapsed;
            UpdateGlass();
            return;
        }
        var alpha = ThemeService.IsHighContrast || !PanelBackdrops.SupportsFrost(_backdrop)
            ? 0 : PanelBackdrops.FrostBrightness(frost, Vm.IsDarkSurface);
        if (alpha <= 0)
        {
            PanelBorder.SetResourceReference(Border.BackgroundProperty, "Bg.Panel");
            FrostBrightness.Background = null;
            FrostTint.ClearValue(Border.BackgroundProperty);
            FrostLayer.Visibility = Visibility.Collapsed;
            return;
        }
        PanelBorder.Background = Brushes.Transparent;
        FrostBrightness.Background = Solid(Vm.IsDarkSurface ? new Rgb(0, 0, 0) : new Rgb(255, 255, 255), alpha);
        FrostTint.SetResourceReference(Border.BackgroundProperty, "Bg.Panel");
        FrostLayer.Visibility = Visibility.Visible;
    }

    /// <summary>Für den Selbsttest: Deckkraft der Helligkeitsschicht (0 = kein Milchglas).</summary>
    internal double FrostBrightnessAlpha => FrostLayer.Visibility == Visibility.Visible && FrostBrightness.Background is SolidColorBrush b ? b.Color.A / 255.0 : 0;

    /// <summary>
    /// Windows zeigt Hintergründe eines Systemfensters nur für „aktive“ Fenster, sonst eine schlichte Ersatzfarbe. Das Panel wird oft
    /// ohne Fokus gezeigt (Hover-Zone) – es soll trotzdem wie aktiv aussehen.
    /// </summary>
    private IntPtr SystemWindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCACTIVATE = 0x0086, WM_DPICHANGED = 0x02E0;
        if (msg == WM_NCACTIVATE && wParam == IntPtr.Zero)
        {
            handled = true;
            return Interop.PanelWindowNative.DefWindowProc(hwnd, msg, (IntPtr)1, lParam);
        }
        // Beim Gleiten liegt das Fenster kurz auf dem Nachbarmonitor (dort weggeschnitten). Hat der eine andere
        // Skalierung, meldet Windows einen DPI-Wechsel – WPF würde Fenster und Inhalt mitten im Gleiten umskalieren.
        // Das Panel gehört aber zu seinem Monitor: Wechsel während des Gleitens übergehen.
        if (msg == WM_DPICHANGED && _clipSlide && IsSliding)
        {
            handled = true;
            return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    private static SolidColorBrush Solid(Rgb c, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(opacity, 0, 1)), c.R, c.G, c.B));
        brush.Freeze();
        return brush;
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
        if (_systemWindow)
        {
            // Panelrahmen so groß wie das Fenster; die Lage beim Ein-/Ausfahren setzt ApplyReveal
            PanelBorder.Width = _rect.Width / dpi.DpiScaleX;
            PanelBorder.Height = _rect.Height / dpi.DpiScaleY;
            PanelBorder.HorizontalAlignment = Cfg.Edge == ScreenEdge.Left ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            PanelBorder.VerticalAlignment = Cfg.Edge == ScreenEdge.Top ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            PrepareSlide();
            ApplyReveal();
        }
        else
        {
            ScreenService.Place(_hwnd, _rect);
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

    // ---- Weichzeichner-Varianten: Fenster gleitet als Ganzes vom Rand (Hintergrund bleibt deckungsgleich) -------
    //
    // Windows zeichnet den Hintergrund, WPF den Inhalt – zwei getrennte Abläufe. Ändert sich beim Ein-/Ausfahren die
    // Fenstergröße oder ein Fensterbereich, passt Windows den Hintergrund sofort an, WPF den Inhalt ein bis zwei Bilder
    // später (und muss dafür jedes Bild neu zeichnen). Verschiebt man dagegen nur das Fenster, bewegt Windows Inhalt und
    // Hintergrund in einem Stück – wie beim Ziehen eines Fensters. WPF muss dabei nichts neu zeichnen.

    /// <summary>0 = eingefahren (hinter dem Rand des Arbeitsbereichs), 1 = ganz ausgefahren.</summary>
    public static readonly DependencyProperty RevealProperty = DependencyProperty.Register(
        nameof(Reveal), typeof(double), typeof(PanelWindow), new PropertyMetadata(1.0, (d, _) => ((PanelWindow)d).ApplyReveal()));

    public double Reveal { get => (double)GetValue(RevealProperty); set => SetValue(RevealProperty, value); }

    /// <summary>Eingefahrene Lage des Fensters (gleiche Größe wie das Panel).</summary>
    private System.Drawing.Rectangle _hidden;
    /// <summary>Auf dem Weg auf den Arbeitsbereich zuschneiden: Taskleiste oder ein anderer Monitor liegt an dieser Kante.</summary>
    private bool _clipSlide;
    private System.Drawing.Size _appliedSize;
    /// <summary>Gesetzter Fensterbereich (sichtbarer Teil relativ zum Fenster); null = keiner, Windows rundet die Ecken selbst.</summary>
    private System.Drawing.Rectangle? _region;
    private bool _regionApplied;

    /// <summary>Weg zum Ein-/Ausfahren für die aktuelle Panelgröße und -lage bestimmen (bei jedem Platzieren).</summary>
    private void PrepareSlide()
    {
        if (_monitor == null) return;
        _hidden = PanelSlide.Hidden(_rect, _monitor.WorkArea, Cfg.Edge);
        _clipSlide = PanelSlide.NeedsClip(_rect, _monitor.WorkArea, Cfg.Edge, ScreenService.GetMonitors().Select(m => m.Bounds));
        _appliedSize = System.Drawing.Size.Empty; // Größe und Fensterbereich neu setzen (Panel kann größer/kleiner geworden sein)
        _regionApplied = false;
    }

    /// <summary>Fenster auf die Lage zum aktuellen <see cref="Reveal"/> setzen; die Größe bleibt die des Panels.</summary>
    private void ApplyReveal()
    {
        if (!_systemWindow || _hwnd == IntPtr.Zero || _rect.Width <= 0 || _monitor == null) return;
        var r = PanelSlide.At(_rect, _hidden, Reveal);
        var visible = _clipSlide ? PanelSlide.Visible(r, _monitor.WorkArea) : new System.Drawing.Rectangle(0, 0, r.Width, r.Height);
        bool full = visible.Size == r.Size;
        // Fensterbereich nur, solange zugeschnitten wird (Taskleiste oder Nachbarmonitor an der Kante)
        System.Drawing.Rectangle? region = !full ? visible : null;

        // Wird der sichtbare Teil kleiner, zuerst zuschneiden, dann verschieben (sonst umgekehrt) –
        // so ragt das Panel auch für ein einzelnes Bild nicht über die Taskleiste oder auf den Nachbarmonitor.
        static long Area(System.Drawing.Rectangle? v, System.Drawing.Rectangle win) => v is { } x ? (long)x.Width * x.Height : (long)win.Width * win.Height;
        bool shrinking = !_regionApplied || Area(region, r) < Area(_region, r);
        if (shrinking) SetSlideRegion(region, r.Size);
        uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER;
        if (r.Size == _appliedSize) flags |= NativeMethods.SWP_NOSIZE;
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, flags);
        _appliedSize = r.Size;
        if (!shrinking) SetSlideRegion(region, r.Size);
    }

    private void SetSlideRegion(System.Drawing.Rectangle? region, System.Drawing.Size size)
    {
        if (_regionApplied && Nullable.Equals(region, _region)) return; // unverändert: kein Fensterbereich pro Bild
        _region = region;
        _regionApplied = true;
        if (region is not { } v) { Interop.PanelWindowNative.ClearRegion(_hwnd); return; }
        var radius = (int)Math.Round(PanelBorder.CornerRadius.TopLeft * VisualTreeHelper.GetDpi(this).DpiScaleX);
        Interop.PanelWindowNative.SetClippedRoundedRegion(_hwnd, size.Width, size.Height, radius, v.Left, v.Top, v.Right, v.Bottom);
    }

    /// <summary>Fenster gleitet gerade (oder steht eingefahren am Rand).</summary>
    private bool IsSliding => _systemWindow && IsVisible && Reveal < 1;

    private void AnimateReveal(bool show, TimeSpan duration)
    {
        Slide.BeginAnimation(TranslateTransform.XProperty, null);
        Slide.BeginAnimation(TranslateTransform.YProperty, null);
        Slide.X = 0;
        Slide.Y = 0;
        PanelBorder.BeginAnimation(OpacityProperty, null);
        PanelBorder.Opacity = 1;
        if (duration == TimeSpan.Zero)
        {
            BeginAnimation(RevealProperty, null);
            Reveal = show ? 1 : 0;
            if (!show) FinishHide();
            return;
        }
        var anim = new DoubleAnimation(show ? 1.0 : 0.0, show ? duration : TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.7))
        {
            EasingFunction = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn }
        };
        if (!show) anim.Completed += (_, _) => { if (_hiding) FinishHide(); };
        BeginAnimation(RevealProperty, anim);
    }

    private void Animate(bool show)
    {
        var duration = _skipNextAnimation ? TimeSpan.Zero : AnimationDuration;
        if (_systemWindow)
        {
            AnimateReveal(show, duration);
            return;
        }
        // Klarglas: Fenster mit Transparenz-Schicht, der Inhalt gleitet im Fenster
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
        _peekWindow?.Hide();
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

        bool inside = ScreenService.CursorInside(_rect, 2) || _host.Triggers.Any(t => ScreenService.CursorInside(t.CurrentRect, 2))
                      || (_peekWindow is { IsVisible: true } peek && ScreenService.CursorInside(peek.PixelRect, 2));
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
        if (sender is Button { ContextMenu: { } menu } button) OpenMenuBelow(menu, button);
    }

    /// <summary>
    /// Kontextmenü unter einem Element öffnen (⋯-Knopf, Tastatur). ItemMenu und GroupMenu gibt es nur einmal je Fenster:
    /// Ziel und Lage werden beim Schließen zurückgesetzt, damit der nächste Rechtsklick wieder am Mauszeiger öffnet.
    /// Der DataContext folgt dort per Bindung dem Ziel; nur Menüs ohne diese Bindung bekommen ihn direkt.
    /// </summary>
    private static void OpenMenuBelow(ContextMenu menu, FrameworkElement target, double horizontalOffset = 0)
    {
        menu.PlacementTarget = target;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.HorizontalOffset = horizontalOffset;
        if (System.Windows.Data.BindingOperations.GetBindingExpression(menu, DataContextProperty) == null)
            menu.DataContext = target.DataContext;
        void Reset(object? s, RoutedEventArgs a)
        {
            menu.Closed -= Reset;
            menu.ClearValue(ContextMenu.PlacementTargetProperty);
            menu.ClearValue(ContextMenu.PlacementProperty);
            menu.ClearValue(ContextMenu.HorizontalOffsetProperty);
        }
        menu.Closed += Reset;
        menu.IsOpen = true;
    }

    /// <summary>Rechtsklick: geteiltes Menü an das Element hängen, dem es gehört (Zeile, Kachel, Gruppenkopf).</summary>
    private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { ContextMenu: { } menu } owner)
            {
                if (!ReferenceEquals(menu.PlacementTarget, owner)) menu.PlacementTarget = owner;
                return;
            }
        }
    }

    // ---- Drag & Drop: Reihenfolge innerhalb einer Gruppe ----------------------

    private const string DragFormat = "PowerExec.Item";
    private Point _dragStart;
    private ViewModels.ItemViewModel? _dragCandidate;

    private void Row_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Strg+Klick: zur Mehrfachauswahl hinzufügen bzw. entfernen (statt aufklappen oder starten)
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && (sender as FrameworkElement)?.DataContext is ViewModels.ItemViewModel checkedItem)
        {
            Vm.ToggleChecked(checkedItem);
            e.Handled = true;
            return;
        }
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
        _groupDragCandidate = (sender as FrameworkElement)?.DataContext is ViewModels.GroupViewModel { CanDrag: true } g ? g : null;
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
        _groupDragStartX = e.GetPosition(this).X;
        Vm.BeginGroupDrag(group);
        var settings = _app.Settings;
        _dragFeedback = new DragFeedback(this, Vm.DragLabel, WpfColors.ToBrush(Vm.DragColor), settings.GroupDragGhost, settings.GroupDragExplain);
        var source = (UIElement)sender;
        GiveFeedbackEventHandler follow = (_, _) => _dragFeedback?.Move();
        source.GiveFeedback += follow;
        try
        {
            DragDrop.DoDragDrop(source, new DataObject(GroupDragFormat, group), DragDropEffects.Move);
        }
        finally
        {
            source.GiveFeedback -= follow;
            _dragFeedback?.Dispose();
            _dragFeedback = null;
            Vm.EndGroupDrag(); // abgebrochen oder außerhalb losgelassen
        }
    }

    private double _groupDragStartX;
    private DragFeedback? _dragFeedback;

    /// <summary>Einrückung je Ebene im Panel (Rand der Untergruppen-Liste): seitliches Ziehen um so viel wechselt die Ebene.</summary>
    private const double GroupIndent = 14;

    /// <summary>Oberes Viertel = davor, unteres Viertel = dahinter, Mitte = hinein (Untergruppe).</summary>
    private static Core.Config.GroupDropZone GroupZone(FrameworkElement header, DragEventArgs e)
    {
        var y = e.GetPosition(header).Y;
        var h = Math.Max(1, header.ActualHeight);
        return y < h * 0.28 ? Core.Config.GroupDropZone.Before
             : y > h * 0.72 ? Core.Config.GroupDropZone.After
             : Core.Config.GroupDropZone.Into;
    }

    private int LevelOffset(DragEventArgs e) => (int)Math.Round((e.GetPosition(this).X - _groupDragStartX) / GroupIndent);

    /// <summary>Effekt und Schild für den aktuellen Plan (bleibt über Lücke und Gruppeninhalt stehen).</summary>
    private void ApplyGroupPlan(Core.Config.GroupDropPlan? plan, DragEventArgs e)
    {
        e.Effects = plan is { IsValid: true } ? DragDropEffects.Move : DragDropEffects.None;
        _dragFeedback?.Update(plan);
        e.Handled = true;
    }

    private void GroupHeader_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border header || !e.Data.GetDataPresent(GroupDragFormat)) return; // Einträge/Dateien: übernimmt die Gruppe
        if (header.DataContext is not ViewModels.GroupViewModel target) return;
        ApplyGroupPlan(Vm.PreviewGroupDrop(target, GroupZone(header, e), LevelOffset(e)), e);
    }

    private void GroupHeader_DragLeave(object sender, DragEventArgs e)
    {
        // Lücke bleibt offen, bis ein anderes Ziel gefunden ist (sonst springt die Liste)
    }

    private void GroupHeader_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        e.Handled = true;
        Vm.CommitGroupDrop();
    }

    /// <summary>Über der offenen Lücke: Ziel beibehalten.</summary>
    private void DropGap_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        ApplyGroupPlan(Vm.CurrentGroupDropPlan, e);
    }

    private void DropGap_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        e.Handled = true;
        Vm.CommitGroupDrop();
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
            ApplyGroupPlan(Vm.CurrentGroupDropPlan, e); // Inhalt einer Gruppe: Ziel vom letzten Gruppenkopf behalten
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
        if (e.Data.GetDataPresent(GroupDragFormat))
        {
            Vm.CommitGroupDrop();
            return;
        }
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
        if (e.Data.GetDataPresent(GroupDragFormat))
        {
            ApplyGroupPlan(Vm.CurrentGroupDropPlan, e);
            return;
        }
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
        if (e.Data.GetDataPresent(GroupDragFormat))
        {
            Vm.CommitGroupDrop(); // Lücke zeigt, wo die Gruppe landet
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(DragFormat)) return;
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

    /// <summary>
    /// Tastenkürzel im Panel (Übersicht mit F1 oder „?“). Gelten im Suchfeld und auf Einträgen,
    /// nicht in anderen Textfeldern.
    /// </summary>
    private bool HandleShortcut(KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var inSearch = ReferenceEquals(Keyboard.FocusedElement, SearchBox);
        if (Keyboard.FocusedElement is TextBox && !inSearch) return false;

        // Strg+Leertaste im Suchfeld: Filterarten anbieten (wie ein Klick ins Feld)
        if (inSearch && key == Key.Space && mods == ModifierKeys.Control) { Vm.ShowFilterCategories(SearchBox.Text, SearchBox.CaretIndex); return true; }

        // Vorschläge unter dem Suchfeld: Tab übernimmt; bei „#…“/„typ:…“ auch ↑ ↓ und Enter
        if (inSearch && Vm.HasSuggestions)
        {
            if (key == Key.Tab && mods == ModifierKeys.None) { AcceptSuggestion(); return true; }
            if (key == Key.Escape) { Vm.CloseSuggestions(); return true; }
            if (Vm.SuggestionsTakeKeys && mods == ModifierKeys.None)
            {
                if (key == Key.Down) { Vm.SuggestionIndex++; SuggestionList.ScrollIntoView(Vm.SelectedSuggestion); return true; }
                if (key == Key.Up) { Vm.SuggestionIndex--; SuggestionList.ScrollIntoView(Vm.SelectedSuggestion); return true; }
                if (key == Key.Enter) { AcceptSuggestion(); return true; }
            }
        }

        // Alt+1 … 9: Favoriten starten
        if (mods == ModifierKeys.Alt && key is >= Key.D1 and <= Key.D9) return Vm.RunQuickItem(key - Key.D0);
        if (mods == ModifierKeys.Alt && key is >= Key.NumPad1 and <= Key.NumPad9) return Vm.RunQuickItem(key - Key.NumPad0);

        if (key == Key.F1 && mods == ModifierKeys.None) { Vm.ShowShortcuts = !Vm.ShowShortcuts; return true; }
        if (key == Key.K && mods == ModifierKeys.Control) { Vm.ShowGroupJump = !Vm.ShowGroupJump; return true; }
        if (key == Key.H && mods == ModifierKeys.Control) { Vm.ShowHistory = !Vm.ShowHistory; return true; }

        // Leertaste: Vorschau (im Suchfeld nur, solange es leer ist – sonst ist es ein Leerzeichen)
        if (key == Key.Space && mods == ModifierKeys.None)
        {
            var onItem = Keyboard.FocusedElement is FrameworkElement { DataContext: ViewModels.ItemViewModel };
            if (Vm.ShowPeek || onItem || (inSearch && SearchBox.Text.Length == 0)) return Vm.TogglePeek();
        }
        if (Vm.ShowPeek && mods == ModifierKeys.None && key is Key.Up or Key.Down)
        {
            Vm.MovePeek(key == Key.Down ? 1 : -1);
            return true;
        }

        var item = Vm.CurrentItem;
        if (item == null) return false;
        bool Exec(ICommand command)
        {
            if (!command.CanExecute(null)) return false;
            command.Execute(null);
            return true;
        }
        switch (key)
        {
            case Key.Enter when mods == (ModifierKeys.Control | ModifierKeys.Shift): return Exec(item.RunAsUserCommand);
            case Key.Enter when mods == ModifierKeys.Control: return Exec(item.RunAdminCommand);
            case Key.R when mods == ModifierKeys.Control: return Exec(item.RunRemoteCommand);
            case Key.E when mods == ModifierKeys.Control: return Exec(item.EditCommand);
            case Key.D when mods == ModifierKeys.Control: return Exec(item.ToggleFavoriteCommand);
            case Key.C when mods == (ModifierKeys.Control | ModifierKeys.Shift): return Exec(item.CopyPathCommand);
            case Key.F2 when mods == ModifierKeys.None: return Exec(item.ManageProfilesCommand);
            // → am Ende des Suchtexts oder Tab: Aktionen des Eintrags als Menü
            case Key.Right when mods == ModifierKeys.None && (!inSearch || SearchBox.CaretIndex == SearchBox.Text.Length) && Vm.SelectedItem != null:
            case Key.Tab when mods == ModifierKeys.None && inSearch && Vm.SelectedItem != null:
                return OpenItemMenu(Vm.SelectedItem!);
        }
        return false;
    }

    // ---- Fixierter Gruppenkopf, Sprungleiste, Strg+K ---------------------------

    private bool _stickyUpdatePending;

    private void ScheduleStickyUpdate()
    {
        _groupContainers = null;
        if (_stickyUpdatePending) return;
        _stickyUpdatePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _stickyUpdatePending = false;
            UpdateStickyGroup();
        });
    }

    private List<(ViewModels.GroupViewModel Group, FrameworkElement Element)>? _groupContainers;

    private List<(ViewModels.GroupViewModel Group, FrameworkElement Element)> GroupContainers()
    {
        if (_groupContainers != null) return _groupContainers;
        var list = new List<(ViewModels.GroupViewModel Group, FrameworkElement Element)>();
        void Walk(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ContentPresenter { Content: ViewModels.GroupViewModel g } cp) list.Add((g, cp));
                Walk(child);
            }
        }
        Walk(ListScroll);
        return _groupContainers = list;
    }

    private void ListScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateStickyGroup();

    /// <summary>Innerste Gruppe, deren Kopf oben herausgescrollt ist, deren Inhalt aber noch sichtbar ist.</summary>
    private void UpdateStickyGroup()
    {
        if (!IsLoaded || ListScroll.VerticalOffset < 1) { Vm.StickyGroup = null; return; }
        const double headerHeight = 34;
        ViewModels.GroupViewModel? best = null;
        double bestTop = double.MinValue;
        foreach (var (group, element) in GroupContainers())
        {
            if (!element.IsVisible) continue; // eingeklappte Untergruppe
            if (!element.IsDescendantOf(ListScroll)) { _groupContainers = null; continue; }
            var top = element.TranslatePoint(new Point(0, 0), ListScroll).Y;
            if (top < 0 && top + element.ActualHeight > headerHeight && top > bestTop && group.IsExpanded)
            {
                best = group;
                bestTop = top;
            }
        }
        Vm.StickyGroup = best;
    }

    private void ScrollToGroup(ViewModels.GroupViewModel group)
    {
        // Nach dem Aufklappen erst das Layout abwarten
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _groupContainers = null;
            var element = GroupContainers().FirstOrDefault(x => ReferenceEquals(x.Group, group)).Element;
            if (element == null) return;
            var top = element.TranslatePoint(new Point(0, 0), ListScroll).Y;
            ListScroll.ScrollToVerticalOffset(Math.Max(0, ListScroll.VerticalOffset + top));
            if (!Vm.ShowGroupJump && !SearchBox.IsKeyboardFocusWithin) SearchBox.Focus();
        });
    }

    private void GroupJump_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { GroupJumpBox.Focus(); Keyboard.Focus(GroupJumpBox); });
        else if (IsOpen) SearchBox.Focus();
    }

    private void GroupJumpBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: Vm.MoveGroupJumpSelection(1); GroupJumpList.ScrollIntoView(Vm.GroupJumpSelected); e.Handled = true; break;
            case Key.Up: Vm.MoveGroupJumpSelection(-1); GroupJumpList.ScrollIntoView(Vm.GroupJumpSelected); e.Handled = true; break;
            case Key.Enter: Vm.ConfirmGroupJump(); e.Handled = true; break;
        }
    }

    private void GroupJumpList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && Vm.GroupJumpSelected != null) Vm.ConfirmGroupJump();
    }

    /// <summary>„?“ im leeren Suchfeld zeigt die Übersicht der Tastenkürzel statt danach zu suchen (unabhängig vom Tastaturlayout).</summary>
    private void SearchBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == "?" && SearchBox.Text.Length == 0)
        {
            Vm.ShowShortcuts = !Vm.ShowShortcuts;
            e.Handled = true;
        }
    }

    /// <summary>Das geteilte Menü aller Einträge dieses Fensters (für den Selbsttest).</summary>
    internal ContextMenu ItemMenu => (ContextMenu)FindResource("ItemMenu");

    /// <summary>Kontextmenü eines Eintrags per Tastatur öffnen (erster Menüpunkt hat den Fokus).</summary>
    internal bool OpenItemMenu(ViewModels.ItemViewModel item)
    {
        var container = FindContainer(ListScroll, item) ?? FindContainer(this, item);
        var button = container == null ? null : FindDescendant<Button>(container, b => b.ContextMenu != null);
        if (button?.ContextMenu is not { } menu) return false;
        OpenMenuBelow(menu, button, horizontalOffset: 40);
        menu.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.IsVisible && m.IsEnabled) is { } first) first.Focus();
        });
        return true;
    }

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool> match) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && match(t)) return t;
            var found = FindDescendant(child, match);
            if (found != null) return found;
        }
        return null;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Vm.UpdateSuggestions(SearchBox.Text, SearchBox.CaretIndex);
    private void SearchBox_SelectionChanged(object sender, RoutedEventArgs e) { if (SearchBox.IsKeyboardFocused) Vm.UpdateSuggestions(SearchBox.Text, SearchBox.CaretIndex); }
    private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Vm.CloseSuggestions();

    /// <summary>Klick ins Suchfeld: Filterarten anbieten (nach dem Setzen des Cursors, daher verzögert).</summary>
    private void SearchBox_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (SearchBox.SelectionLength > 0) return; // Text markiert: nicht dazwischenfunken
        Dispatcher.BeginInvoke(() =>
        {
            if (SearchBox.IsKeyboardFocused && SearchBox.SelectionLength == 0) Vm.ShowFilterCategories(SearchBox.Text, SearchBox.CaretIndex);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SuggestionList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(SuggestionList, d) is ListBoxItem { DataContext: Core.Search.SearchSuggestion s })
        {
            Vm.SuggestionIndex = Vm.Suggestions.ToList().IndexOf(s);
            AcceptSuggestion();
            e.Handled = true;
        }
    }

    /// <summary>Gewählten Vorschlag ins Suchfeld übernehmen (ersetzt das Wort vor dem Cursor).</summary>
    private void AcceptSuggestion()
    {
        if (Vm.SelectedSuggestion is not { } s) return;
        var (text, caret) = Core.Search.SearchSuggestions.Apply(SearchBox.Text, SearchBox.CaretIndex, s);
        SearchBox.Text = text;
        SearchBox.CaretIndex = caret;
        SearchBox.Focus();
        Vm.UpdateSuggestions(text, caret); // nach „typ:“ gleich die Werte zeigen
    }

    private void UndoToast_MouseEnter(object sender, MouseEventArgs e) => Vm.PauseUndoToast(true);
    private void UndoToast_MouseLeave(object sender, MouseEventArgs e) => Vm.PauseUndoToast(false);

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
