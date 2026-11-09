using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PowerExec.Infrastructure;

/// <summary>Das Programmsymbol (Assets/PowerExec.ico) in passender Auflösung für die Oberfläche.</summary>
public static class AppIcon
{
    private static readonly Lazy<IReadOnlyList<BitmapFrame>> Frames = new(() =>
    {
        try
        {
            var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/PowerExec.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames.OrderBy(f => f.PixelWidth).ToList();
        }
        catch
        {
            return Array.Empty<BitmapFrame>();
        }
    });

    /// <summary>Kleinstes Bild, das mindestens <paramref name="pixels"/> breit ist (sonst das größte); null, wenn es fehlt.</summary>
    public static ImageSource? Frame(int pixels) =>
        Frames.Value.FirstOrDefault(f => f.PixelWidth >= pixels) ?? Frames.Value.LastOrDefault();
}
