using System.Windows;

namespace PowerExec.Desktop.Infrastructure;

/// <summary>
/// Die Design-Tokens aus <c>Themes/Tokens.xaml</c> für C#: Controls und Fenster, die Teile ihrer Oberfläche im Code
/// aufbauen, beziehen Radien, Schriftgrößen und Dauern hierüber statt über Zahlen. Wo es geht, ist
/// <c>SetResourceReference(…, Tokens.Keys.…)</c> vorzuziehen – dann folgt das Element auch späteren Änderungen
/// (Einstellung „Ecken“).
/// </summary>
public static class Tokens
{
    /// <summary>Schlüssel der Ressourcen (für <c>SetResourceReference</c>).</summary>
    public static class Keys
    {
        public const string RadiusBar = "Radius.Bar";
        public const string RadiusControl = "Radius.Control";
        public const string RadiusCard = "Radius.Card";
        public const string RadiusTile = "Radius.Tile";
        public const string RadiusWindow = "Radius.Window";
        public const string RadiusPill = "Radius.Pill";
        public const string InsetCard = "Inset.Card";
        public const string InsetInfoBar = "Inset.InfoBar";
    }

    public static CornerRadius RadiusBar => Get(Keys.RadiusBar, new CornerRadius(2));
    public static CornerRadius RadiusControl => Get(Keys.RadiusControl, new CornerRadius(4));
    public static CornerRadius RadiusCard => Get(Keys.RadiusCard, new CornerRadius(6));
    public static CornerRadius RadiusTile => Get(Keys.RadiusTile, new CornerRadius(8));
    public static CornerRadius RadiusWindow => Get(Keys.RadiusWindow, new CornerRadius(12));
    public static CornerRadius RadiusPill => Get(Keys.RadiusPill, new CornerRadius(100));

    public static double FontMicro => Get("Font.Micro", 10.0);
    public static double FontCaption => Get("Font.Caption", 11.0);
    public static double FontBody => Get("Font.Body", 12.0);
    public static double FontBodyStrong => Get("Font.BodyStrong", 13.0);
    public static double FontSubtitle => Get("Font.Subtitle", 14.0);
    public static double FontHeader => Get("Font.Header", 20.0);

    public static double IconXs => Get("Icon.XS", 9.0);
    public static double IconM => Get("Icon.M", 12.0);
    public static double IconL => Get("Icon.L", 14.0);
    public static double IconCard => Get("Icon.Card", 20.0);

    public static double HeightChip => Get("Height.Chip", 24.0);
    public static double HeightControl => Get("Height.Control", 32.0);

    /// <summary>Auf- und Zuklappen, Hover, kleine Wechsel.</summary>
    public static TimeSpan MotionFast => Get("Motion.Fast", new Duration(TimeSpan.FromMilliseconds(150))).TimeSpan;
    /// <summary>Wechsel von Ansichten, Ein- und Ausblenden.</summary>
    public static TimeSpan MotionNormal => Get("Motion.Normal", new Duration(TimeSpan.FromMilliseconds(250))).TimeSpan;
    /// <summary>Panel ein- und ausfahren.</summary>
    public static TimeSpan MotionSlow => Get("Motion.Slow", new Duration(TimeSpan.FromMilliseconds(320))).TimeSpan;

    /// <summary>Wert aus den App-Ressourcen; ohne laufende App (Tests, Designer) die Vorgabe.</summary>
    private static T Get<T>(string key, T fallback) =>
        Application.Current?.TryFindResource(key) is T value ? value : fallback;
}
