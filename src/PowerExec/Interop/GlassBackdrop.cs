using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace PowerExec.Desktop.Interop;

/// <summary>
/// Glas (eigener Weichzeichner): Windows-Composition direkt im Panelfenster. Hinter dem WPF-Inhalt liegt ein
/// SpriteVisual mit der Effektkette Hintergrund des Hosts → Gaußscher Weichzeichner → Sättigung → Helligkeitsschicht → Tönung.
/// Es gibt kein zweites Fenster: der Effekt gehört zum Panelfenster, gleitet mit und wird vom Fensterbereich beschnitten.
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

    private delegate int CreateEffectFactoryFn(IntPtr self, IntPtr effect, out IntPtr factory);

    // ---- Instanz ---------------------------------------------------------------------------------

    private readonly DesktopWindowTarget _target;
    private readonly SpriteVisual _visual;
    private (float Blur, float Saturation, Vector4 Luminosity, Vector4 Tint)? _applied;

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
    /// Sättigung (1 = unverändert), Helligkeitsschicht und Tönung mit der Panelfarbe (je RGBA 0–1, A = Deckkraft).
    /// </summary>
    public void Update(float blur, float saturation, Vector4 luminosity, Vector4 tint)
    {
        if (_applied is { } a && a.Blur == blur && a.Saturation == saturation && a.Luminosity == luminosity && a.Tint == tint) return;
        var compositor = Compositor;

        GlassEffectSource chain = new GlassSourceParameter("Backdrop");
        if (blur > 0) chain = new GaussianBlurGlassEffect(chain, blur);
        GlassEffect effect = new SaturationGlassEffect(chain, saturation);
        if (luminosity.W > 0) effect = new CompositeGlassEffect(effect, new FloodGlassEffect(luminosity.X, luminosity.Y, luminosity.Z, luminosity.W));
        if (tint.W > 0) effect = new CompositeGlassEffect(effect, new FloodGlassEffect(tint.X, tint.Y, tint.Z, tint.W));

        // Die Effektbeschreibung geht direkt an ICompositor::CreateEffectFactory (Platz 11 der Methodentabelle laut
        // Windows-Projektion) – das WinRT-Marshalling von .NET kann eigene COM-Objekte nicht umwickeln.
        var effectPtr = effect.GetEffectPointer();
        CompositionEffectBrush brush;
        try
        {
            var unknown = ((WinRT.IWinRTObject)compositor).NativeObject.ThisPtr;
            var iid = new Guid("B403CA50-7F8C-4E83-985F-CC45060036D8"); // ICompositor
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out var icompositor));
            IntPtr factoryPtr;
            try
            {
                var create = Marshal.GetDelegateForFunctionPointer<CreateEffectFactoryFn>(
                    Marshal.ReadIntPtr(Marshal.ReadIntPtr(icompositor), 11 * IntPtr.Size));
                Marshal.ThrowExceptionForHR(create(icompositor, effectPtr, out factoryPtr));
            }
            finally { Marshal.Release(icompositor); }
            try
            {
                using var factory = CompositionEffectFactory.FromAbi(factoryPtr);
                brush = factory.CreateBrush();
            }
            finally { Marshal.Release(factoryPtr); }
        }
        finally { Marshal.Release(effectPtr); }
        brush.SetSourceParameter("Backdrop", compositor.CreateHostBackdropBrush());

        var old = _visual.Brush;
        _visual.Brush = brush;
        old?.Dispose();
        _applied = (blur, saturation, luminosity, tint);
    }

    public void Dispose()
    {
        _target.Root = null;
        _visual.Brush?.Dispose();
        _visual.Dispose();
        _target.Dispose();
    }
}
