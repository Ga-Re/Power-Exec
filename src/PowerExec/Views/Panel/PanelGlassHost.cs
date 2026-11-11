using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using PowerExec.Core.Config;
using PowerExec.Desktop.Infrastructure;
using PowerExec.Desktop.Interop;
using PowerExec.UI.Services;

namespace PowerExec.Desktop.Views;

/// <summary>
/// Glas: eigener Weichzeichner hinter dem Panel und der Panelinhalt in einem durchsichtigen Kindfenster darüber.
///
/// <para>Windows setzt die Ebenen eines Fensters so zusammen (DirectComposition): 1. was direkt ins Fenster gezeichnet
/// wird (GDI oder DirectX – also WPF), 2. Composition-Baum (nicht „topmost“), 3. Kindfenster, 4. Composition-Baum
/// (topmost). Der Weichzeichner (2) läge über dem WPF-Inhalt (1). Deshalb zieht der Inhalt bei Glas in ein
/// durchsichtiges Kindfenster (3) um – ab Windows 8 dürfen Kindfenster Transparenz pro Pixel haben. Es bleibt ein
/// einziges Fenster: Gleiten, Zuschnitt und runde Ecken gelten für Weichzeichner und Inhalt gemeinsam.</para>
///
/// <para>Scheitert der Weichzeichner, bleibt das Fenster durchsichtig und das Milchglas von WPF übernimmt (wie Klarglas).</para>
/// </summary>
internal sealed class PanelGlassHost : IDisposable
{
    private readonly Window _window;
    private readonly PanelBackdrop _backdrop;
    private readonly bool _systemWindow;
    private IntPtr _hwnd;
    private HwndSource? _contentHost;
    /// <summary>Eigener Weichzeichner (nur bei Glas unter Windows 11; als IDisposable, damit ältere Windows den Typ nie anfassen).</summary>
    private IDisposable? _glass;

    /// <param name="window">Das Panelfenster.</param>
    /// <param name="backdrop">Tatsächlich laufende Variante des Hintergrunds.</param>
    /// <param name="systemWindow">Fenster ohne Transparenz-Schicht (Glas).</param>
    public PanelGlassHost(Window window, PanelBackdrop backdrop, bool systemWindow)
    {
        _window = window;
        _backdrop = backdrop;
        _systemWindow = systemWindow;
    }

    /// <summary>Fenster-Handle bekannt geben (entsteht nach dem ersten Anwenden der Farben).</summary>
    public void Attach(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>Läuft der eigene Weichzeichner?</summary>
    public bool GlassActive => _glass != null;

    /// <summary>Fehler beim Aufbau des Weichzeichners (null = keiner).</summary>
    public string? GlassError { get; private set; }

    /// <summary>Gibt es das Kindfenster für den Inhalt?</summary>
    public bool HasContentHost => _contentHost is { IsDisposed: false };

    /// <summary>Liegt der Inhalt im durchsichtigen Kindfenster über dem Weichzeichner?</summary>
    public bool ContentInChildWindow => _contentHost is { IsDisposed: false, RootVisual: not null };

    /// <summary>
    /// Fenster für den Hintergrund vorbereiten und – bei Glas unter Windows 11 – den Weichzeichner anlegen.
    /// <paramref name="update"/> setzt danach dessen Werte (siehe <see cref="Update"/>).
    /// </summary>
    public void ApplyBackdrop(bool dark, Action update)
    {
        if (_hwnd == IntPtr.Zero || !_systemWindow) return;
        PanelWindowNative.PrepareSystemWindow(_hwnd, dark);
        EnsureTransparentBackground();
        if (_backdrop != PanelBackdrop.Glass || GlassError != null || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, PanelBackdrops.GlassBuild)) return;
        try
        {
            _glass ??= new GlassBackdrop(_hwnd);
            update();
        }
        catch (Exception ex)
        {
            // Für Bericht und Fehlersuche: Typ, Meldung und die Stelle, an der es scheiterte
            var frame = new System.Diagnostics.StackTrace(ex).GetFrames().FirstOrDefault(f => f.GetMethod()?.DeclaringType?.Namespace?.StartsWith("PowerExec", StringComparison.Ordinal) == true)
                        ?? new System.Diagnostics.StackTrace(ex).GetFrame(0);
            GlassError = $"{ex.GetType().Name}: {ex.Message} (in {frame?.GetMethod()?.DeclaringType?.Name}.{frame?.GetMethod()?.Name})";
            AppLog.Error("Glass blur not available, falling back to frosted glass", ex);
            DisposeGlass();
        }
    }

    /// <summary>
    /// Werte des Weichzeichners nachziehen: Blur, Sättigung, Helligkeitsschicht (dunkle Fläche Schwarz, helle Weiß) und
    /// Tönung mit der Panelfarbe samt Deckkraft (eigene Farbe bzw. Farbe des hellen oder dunklen Designs).
    /// </summary>
    public void Update(PanelConfig config, bool dark, Color tint)
    {
        if (_glass is not GlassBackdrop glass || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, PanelBackdrops.GlassBuild)) return;
        var shade = dark ? 0f : 1f;
        glass.Update(PanelBackdrops.GlassBlurRadius(config.GlassBlur), PanelBackdrops.GlassSaturation(config.GlassSaturation),
            new System.Numerics.Vector4(shade, shade, shade, (float)PanelBackdrops.FrostBrightness(config.Frost, dark)),
            new System.Numerics.Vector4(tint.R / 255f, tint.G / 255f, tint.B / 255f, tint.A / 255f));
    }

    /// <summary>Fenster ohne Transparenz-Schicht: Hintergrund der WPF-Zeichenfläche durchsichtig (entsteht erst beim Anzeigen).</summary>
    public void EnsureTransparentBackground()
    {
        if (!_systemWindow || _hwnd == IntPtr.Zero) return;
        if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
    }

    /// <summary>
    /// Inhalt des Fensters in ein durchsichtiges Kindfenster umziehen. <paramref name="wire"/> hängt die Ereignisse an,
    /// die bisher am Fenster ankamen (Tastatur, Verlassen mit der Maus, Kontextmenüs).
    /// </summary>
    public void HostContent(Action<FrameworkElement> wire)
    {
        if (_contentHost != null || _window.Content is not FrameworkElement root) return;
        NativeMethods.GetClientRect(_hwnd, out var rc);
        var parameters = new HwndSourceParameters("PowerExecPanelInhalt")
        {
            ParentWindow = _hwnd,
            WindowStyle = NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPSIBLINGS,
            PositionX = 0,
            PositionY = 0,
            Width = Math.Max(1, rc.Right - rc.Left),
            Height = Math.Max(1, rc.Bottom - rc.Top),
            UsesPerPixelTransparency = true
        };
        // Vom Fenster geerbte Werte (Datenkontext, Ressourcen, Schrift) gehen beim Umzug verloren – mitnehmen
        _window.Content = null;
        root.DataContext = _window.DataContext;
        if (root.Resources.Count == 0 && root.Resources.MergedDictionaries.Count == 0) root.Resources = _window.Resources;
        else root.Resources.MergedDictionaries.Insert(0, _window.Resources);
        root.SetBinding(TextElement.FontFamilyProperty, new System.Windows.Data.Binding(nameof(Window.FontFamily)) { Source = _window });
        root.SetBinding(TextElement.FontSizeProperty, new System.Windows.Data.Binding(nameof(Window.FontSize)) { Source = _window });
        root.SetBinding(TextElement.ForegroundProperty, new System.Windows.Data.Binding(nameof(Window.Foreground)) { Source = _window });
        wire(root);

        FluentTheme.RegisterRoot(root); // Fluent-Stile im Kindfenster festhalten (Kontrollkästchen, Animationen)
        _contentHost = new HwndSource(parameters) { SizeToContent = SizeToContent.Manual, RootVisual = root };
        if (_contentHost.CompositionTarget != null) _contentHost.CompositionTarget.BackgroundColor = Colors.Transparent;
        if (HwndSource.FromHwnd(_hwnd) is { } src) src.AddHook(ParentHook);
    }

    /// <summary>
    /// Kindfenster bedingungslos bei (0, 0) mit dieser Größe verankern. Eine Prüfung über GetClientRect sah die Lage im
    /// Elternfenster nicht (immer 0/0) – ein verrutschtes Kindfenster blieb verrutscht.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (_contentHost is not { IsDisposed: false } host || host.Handle == IntPtr.Zero || width <= 0 || height <= 0) return;
        NativeMethods.SetWindowPos(host.Handle, IntPtr.Zero, 0, 0, width, height, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>Kindfenster auf die Größe des Fensterinhalts.</summary>
    public void Resize()
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.GetClientRect(_hwnd, out var client);
        Resize(client.Right - client.Left, client.Bottom - client.Top);
    }

    /// <summary>Für den Selbsttest: Größe von Fensterinhalt und Kindfenster in Pixeln (Kindfenster null = keins).</summary>
    public ((int W, int H) Client, (int W, int H)? Content) Sizes()
    {
        NativeMethods.GetClientRect(_hwnd, out var client);
        (int, int)? content = null;
        if (_contentHost is { IsDisposed: false } host)
        {
            NativeMethods.GetClientRect(host.Handle, out var c);
            content = (c.Right - c.Left, c.Bottom - c.Top);
        }
        return ((client.Right - client.Left, client.Bottom - client.Top), content);
    }

    /// <summary>Kindfenster immer so groß wie der Fensterinhalt.</summary>
    private IntPtr ParentHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_SIZE && _contentHost is { IsDisposed: false })
        {
            var w = (int)((long)lParam & 0xFFFF);
            var h = (int)(((long)lParam >> 16) & 0xFFFF);
            Resize(Math.Max(1, w), Math.Max(1, h));
        }
        return IntPtr.Zero;
    }

    private void DisposeGlass()
    {
        _glass?.Dispose();
        _glass = null;
    }

    public void Dispose()
    {
        DisposeGlass();
        if (_contentHost == null) return;
        _contentHost.RootVisual = null;
        _contentHost.Dispose();
        _contentHost = null;
    }
}
