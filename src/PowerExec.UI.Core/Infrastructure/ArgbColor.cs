using PowerExec.Core.Config;

namespace PowerExec.Infrastructure;

/// <summary>
/// Farbe mit Deckkraft, ohne UI-Framework. Die Oberfläche wandelt sie per Converter in ihren Pinsel um
/// (WPF: <c>ArgbBrushConverter</c>).
/// </summary>
public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public static readonly ArgbColor Transparent = new(0, 0, 0, 0);

    public static ArgbColor From(Rgb color, byte alpha = 255) => new(alpha, color.R, color.G, color.B);

    /// <summary>„#RRGGBB“ (wie in den Einstellungen) mit der angegebenen Deckkraft; null bei leer/ungültig.</summary>
    public static ArgbColor? FromHex(string? hex, byte alpha = 255) =>
        PanelColors.TryParse(hex, out var c) ? From(c, alpha) : null;

    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}
