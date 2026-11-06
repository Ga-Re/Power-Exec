using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Media;

namespace PowerExec.Services;

/// <summary><see cref="IIconProvider"/> über den <see cref="IconService"/>; je Bild genau ein Griff.</summary>
public sealed class WpfIconProvider(IconService icons) : IIconProvider
{
    private static readonly ConditionalWeakTable<ImageSource, IconImage> Handles = new();

    public static IconImage? Wrap(ImageSource? image) => image == null ? null : Handles.GetValue(image, static i => new IconImage(i));

    public static ImageSource? Unwrap(IconImage? image) => image?.Native as ImageSource;

    private static Action<ImageSource> Forward(Action<IconImage> onLoaded) => img => onLoaded(Wrap(img)!);

    public IconImage? Get(string path, DateTime lastWriteUtc, Action<IconImage> onLoaded) => Wrap(icons.Get(path, lastWriteUtc, Forward(onLoaded)));
    public IconImage? GetFolder(Action<IconImage> onLoaded) => Wrap(icons.GetFolder(Forward(onLoaded)));
    public IconImage? GetProgram(string program, Action<IconImage> onLoaded) => Wrap(icons.GetProgram(program, Forward(onLoaded)));
    public IconImage? GetFavicon(string url, Action<IconImage> onLoaded) => Wrap(icons.GetFavicon(url, Forward(onLoaded)));
}

/// <summary><see cref="IconImage"/> → <see cref="ImageSource"/> (null bleibt null).</summary>
public sealed class IconImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => WpfIconProvider.Unwrap(value as IconImage);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
