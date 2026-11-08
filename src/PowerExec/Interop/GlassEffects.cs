using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Foundation;

namespace PowerExec.Interop;

// Eigene Composition-Effekte ohne Win2D. Die Windows-Composition nimmt eine Effektbeschreibung als
// Windows.Graphics.Effects.IGraphicsEffect entgegen und fragt über IGraphicsEffectD2D1Interop nach dem Direct2D-Effekt
// (CLSID), seinen Eigenschaften und Quellen. Beide Schnittstellen stellt hier die COM-Brücke von .NET bereit
// (IInspectable wird mit drei Platzhalter-Methoden nachgebildet).

/// <summary>Zuordnung einer benannten Eigenschaft (für Animationen – hier nicht genutzt).</summary>
internal enum GraphicsEffectPropertyMapping { Unknown = 0, Direct = 1 }

/// <summary>IInspectable + Windows.Graphics.Effects.IGraphicsEffectSource (ohne eigene Methoden).</summary>
[ComVisible(true), Guid("2D8F9DDC-4339-4EB9-9216-F66DEB6B4D7A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsEffectSourceAbi
{
    [PreserveSig] int GetIids(out int count, out IntPtr iids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);
}

/// <summary>IInspectable + Windows.Graphics.Effects.IGraphicsEffect (Name lesen/schreiben).</summary>
[ComVisible(true), Guid("CB51C0CE-8FE6-4636-B202-861FAA07D8F3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsEffectAbi
{
    [PreserveSig] int GetIids(out int count, out IntPtr iids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);
    [PreserveSig] int get_Name(out IntPtr name);
    [PreserveSig] int put_Name(IntPtr name);
}

/// <summary>Windows.Graphics.Effects.IGraphicsEffectD2D1Interop (windows.graphics.effects.interop.h).</summary>
[ComVisible(true), Guid("2FC57384-A068-44D7-A331-30982FCF7177"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsEffectD2D1Interop
{
    [PreserveSig] int GetEffectId(out Guid id);
    [PreserveSig] int GetNamedPropertyMapping([MarshalAs(UnmanagedType.LPWStr)] string name, out uint index, out GraphicsEffectPropertyMapping mapping);
    [PreserveSig] int GetPropertyCount(out uint count);
    [PreserveSig] int GetProperty(uint index, out IntPtr value);
    [PreserveSig] int GetSource(uint index, out IntPtr source);
    [PreserveSig] int GetSourceCount(out uint count);
}

/// <summary>Quelle eines Effekts: ein anderer eigener Effekt oder ein Objekt der Composition (z. B. Effektparameter).</summary>
internal abstract class GlassEffectSource
{
    /// <summary>Zeiger auf IGraphicsEffectSource (mit AddRef; der Aufrufer gibt ihn frei).</summary>
    public abstract IntPtr GetSourcePointer();
}

/// <summary>Benannter Platzhalter, dem später eine Composition-Pinsel zugeordnet wird (SetSourceParameter).</summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal sealed class GlassSourceParameter(string name) : GlassEffectSource
{
    private readonly Windows.UI.Composition.CompositionEffectSourceParameter _parameter = new(name);

    public override IntPtr GetSourcePointer()
    {
        var inspectable = WinRT.MarshalInspectable<object>.FromManaged(_parameter);
        try
        {
            var iid = typeof(IGraphicsEffectSourceAbi).GUID;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(inspectable, in iid, out var source));
            return source;
        }
        finally { Marshal.Release(inspectable); }
    }
}

/// <summary>Grundlage aller eigenen Effekte: Direct2D-CLSID, Eigenschaften (als IPropertyValue) und Quellen.</summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal abstract class GlassEffect : GlassEffectSource, IGraphicsEffectAbi, IGraphicsEffectSourceAbi, IGraphicsEffectD2D1Interop
{
    protected abstract Guid EffectId { get; }
    protected abstract object[] Properties { get; }
    protected virtual GlassEffectSource[] Sources => Array.Empty<GlassEffectSource>();

    public override IntPtr GetSourcePointer() => Marshal.GetComInterfaceForObject(this, typeof(IGraphicsEffectSourceAbi));

    /// <summary>Zeiger auf IGraphicsEffect (mit AddRef) – für Compositor.CreateEffectFactory.</summary>
    public IntPtr GetEffectPointer() => Marshal.GetComInterfaceForObject(this, typeof(IGraphicsEffectAbi));

    // ---- IInspectable (Platzhalter) ----
    public int GetIids(out int count, out IntPtr iids) { count = 0; iids = IntPtr.Zero; return 0; }
    public int GetRuntimeClassName(out IntPtr className) { className = IntPtr.Zero; return 0; }
    public int GetTrustLevel(out int trustLevel) { trustLevel = 0; return 0; }

    // ---- IGraphicsEffect ----
    public int get_Name(out IntPtr name) { name = IntPtr.Zero; return 0; } // leerer HSTRING
    public int put_Name(IntPtr name) => 0;

    // ---- IGraphicsEffectD2D1Interop ----
    public int GetEffectId(out Guid id) { id = EffectId; return 0; }

    public int GetNamedPropertyMapping(string name, out uint index, out GraphicsEffectPropertyMapping mapping)
    {
        index = 0;
        mapping = GraphicsEffectPropertyMapping.Unknown;
        return unchecked((int)0x80070057); // E_INVALIDARG: keine animierbaren Eigenschaften
    }

    public int GetPropertyCount(out uint count) { count = (uint)Properties.Length; return 0; }

    public int GetProperty(uint index, out IntPtr value)
    {
        value = IntPtr.Zero;
        try
        {
            if (index >= Properties.Length) return unchecked((int)0x8000000B); // E_BOUNDS
            value = ToPropertyValue(Properties[index]);
            return 0;
        }
        catch (Exception ex) { return ex.HResult; }
    }

    public int GetSource(uint index, out IntPtr source)
    {
        source = IntPtr.Zero;
        try
        {
            if (index >= Sources.Length) return unchecked((int)0x8000000B);
            source = Sources[index].GetSourcePointer();
            return 0;
        }
        catch (Exception ex) { return ex.HResult; }
    }

    public int GetSourceCount(out uint count) { count = (uint)Sources.Length; return 0; }

    /// <summary>Wert als Windows.Foundation.IPropertyValue (float, uint oder float[] für Vektoren/Farben).</summary>
    private static IntPtr ToPropertyValue(object value)
    {
        var boxed = value switch
        {
            float f => PropertyValue.CreateSingle(f),
            uint u => PropertyValue.CreateUInt32(u),
            float[] a => PropertyValue.CreateSingleArray(a),
            _ => throw new NotSupportedException(value.GetType().Name)
        };
        var inspectable = WinRT.MarshalInspectable<object>.FromManaged(boxed);
        try
        {
            var iid = new Guid("4BD682DD-7554-40E9-9A9B-82654EDE7E62"); // IPropertyValue
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(inspectable, in iid, out var pv));
            return pv;
        }
        finally { Marshal.Release(inspectable); }
    }
}

/// <summary>Direct2D „Gaussian blur“: StandardDeviation, Optimization (ausgewogen), BorderMode (hart).</summary>
internal sealed class GaussianBlurGlassEffect(GlassEffectSource source, float standardDeviation) : GlassEffect
{
    protected override Guid EffectId { get; } = new("1FEB6D69-2FE6-4AC9-8C58-1D7F93E7A6A5");
    protected override object[] Properties => new object[] { standardDeviation, 1u, 1u };
    protected override GlassEffectSource[] Sources => new[] { source };
}

/// <summary>Direct2D „Saturation“: 0 = grau, 1 = unverändert, 2 = doppelt.</summary>
internal sealed class SaturationGlassEffect(GlassEffectSource source, float saturation) : GlassEffect
{
    protected override Guid EffectId { get; } = new("5CB2D9CF-327D-459F-A0CE-40C0B2086BF7");
    protected override object[] Properties => new object[] { saturation };
    protected override GlassEffectSource[] Sources => new[] { source };
}

/// <summary>Direct2D „Flood“: einfarbige Fläche (RGBA 0–1, vormultipliziert wird von Direct2D erledigt).</summary>
internal sealed class FloodGlassEffect(float r, float g, float b, float a) : GlassEffect
{
    protected override Guid EffectId { get; } = new("61C23C20-AE69-4D8E-94CF-50078DF638F2");
    protected override object[] Properties => new object[] { new[] { r, g, b, a } };
}

/// <summary>Direct2D „Composite“ (Quelle über Ziel): legt die zweite Quelle über die erste.</summary>
internal sealed class CompositeGlassEffect(GlassEffectSource bottom, GlassEffectSource top) : GlassEffect
{
    protected override Guid EffectId { get; } = new("48FC9F51-F6AC-48F1-8B58-3B28AC46F76D");
    protected override object[] Properties => new object[] { 0u }; // D2D1_COMPOSITE_MODE_SOURCE_OVER
    protected override GlassEffectSource[] Sources => new[] { bottom, top };
}
