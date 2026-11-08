using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace PowerExec.Interop;

/// <summary>
/// Glas (eigener Weichzeichner): Windows-Composition direkt im Panelfenster. Hinter dem WPF-Inhalt liegt ein
/// SpriteVisual mit der Effektkette Hintergrund des Hosts → Gaußscher Weichzeichner → Sättigung → Helligkeitsschicht.
/// Es gibt kein zweites Fenster: der Effekt gehört zum Panelfenster, gleitet mit und wird vom Fensterbereich beschnitten.
/// Panelfarbe (Tönung) und Körnung zeichnet WPF darüber.
/// </summary>
[SupportedOSPlatform("windows10.0.22000.0")]
internal sealed class GlassBackdrop : IDisposable
{
    // ---- Composition auf dem UI-Thread ----------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;     // DQTYPE_THREAD_CURRENT = 2
        public int ApartmentType;  // DQTAT_COM_STA = 2
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

    /// <summary>Hält den DispatcherQueueController des UI-Threads am Leben (Voraussetzung für den Compositor).</summary>
    private static IntPtr _queueController;
    private static Compositor? _compositor;

    private static Compositor Compositor
    {
        get
        {
            if (_compositor != null) return _compositor;
            if (_queueController == IntPtr.Zero)
            {
                var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 2 };
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _queueController));
            }
            return _compositor = new Compositor();
        }
    }

    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        [PreserveSig] int CreateDesktopWindowTarget(IntPtr hwndTarget, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out IntPtr target);
        [PreserveSig] int EnsureOnThread(uint threadId);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_HOSTBACKDROPBRUSH = 17;

    // ---- Instanz ---------------------------------------------------------------------------------

    private readonly DesktopWindowTarget _target;
    private readonly SpriteVisual _visual;
    private (float Blur, float Saturation, Vector4 Luminosity)? _applied;

    /// <summary>Hängt den Effekt hinter den Inhalt des Fensters (isTopmost: false).</summary>
    public GlassBackdrop(IntPtr hwnd)
    {
        int on = 1;
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, ref on, sizeof(int)));
        var compositor = Compositor;

        var unknown = ((WinRT.IWinRTObject)compositor).NativeObject.ThisPtr;
        var iid = typeof(ICompositorDesktopInterop).GUID;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out var interopPtr));
        IntPtr targetPtr;
        try
        {
            var interop = (ICompositorDesktopInterop)Marshal.GetObjectForIUnknown(interopPtr);
            Marshal.ThrowExceptionForHR(interop.CreateDesktopWindowTarget(hwnd, false, out targetPtr));
        }
        finally { Marshal.Release(interopPtr); }
        try { _target = DesktopWindowTarget.FromAbi(targetPtr); }
        finally { Marshal.Release(targetPtr); }

        _visual = compositor.CreateSpriteVisual();
        _visual.RelativeSizeAdjustment = Vector2.One;
        _target.Root = _visual;
    }

    /// <summary>
    /// Effekt mit neuen Werten aufbauen (nur bei Änderung): Radius des Weichzeichners (Standardabweichung in Pixeln),
    /// Sättigung (1 = unverändert) und Helligkeitsschicht (RGBA 0–1, A = Deckkraft).
    /// </summary>
    public void Update(float blur, float saturation, Vector4 luminosity)
    {
        if (_applied is { } a && a.Blur == blur && a.Saturation == saturation && a.Luminosity == luminosity) return;
        var compositor = Compositor;

        GlassEffectSource chain = new GlassSourceParameter("Backdrop");
        if (blur > 0) chain = new GaussianBlurGlassEffect(chain, blur);
        GlassEffect effect = new SaturationGlassEffect(chain, saturation);
        if (luminosity.W > 0) effect = new CompositeGlassEffect(effect, new FloodGlassEffect(luminosity.X, luminosity.Y, luminosity.Z, luminosity.W));

        var effectPtr = effect.GetEffectPointer();
        CompositionEffectBrush brush;
        try
        {
            var description = WinRT.MarshalInterface<Windows.Graphics.Effects.IGraphicsEffect>.FromAbi(effectPtr);
            using var factory = compositor.CreateEffectFactory(description);
            brush = factory.CreateBrush();
        }
        finally { Marshal.Release(effectPtr); }
        brush.SetSourceParameter("Backdrop", compositor.CreateHostBackdropBrush());

        var old = _visual.Brush;
        _visual.Brush = brush;
        old?.Dispose();
        _applied = (blur, saturation, luminosity);
    }

    public void Dispose()
    {
        _target.Root = null;
        _visual.Brush?.Dispose();
        _visual.Dispose();
        _target.Dispose();
    }
}
