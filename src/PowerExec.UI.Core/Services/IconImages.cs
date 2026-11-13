namespace PowerExec.UI.Services;

/// <summary>
/// Geladenes Symbolbild der Oberfläche als undurchsichtiger Griff: Das ViewModel reicht es nur weiter,
/// die Oberfläche packt es per Converter wieder aus (WPF: <c>ImageSource</c>). So bleiben ViewModels
/// ohne UI-Framework und die Bilder werden trotzdem nur einmal erzeugt und zwischengespeichert.
/// </summary>
public sealed class IconImage(object native)
{
    public object Native { get; } = native;
}

/// <summary>
/// Lädt Symbole (Shell-Symbol, Ordner, Programm, Favicon) im Hintergrund. Liefert ein schon bekanntes Bild sofort,
/// sonst null und später <c>onLoaded</c> auf dem UI-Thread.
/// </summary>
public interface IIconProvider
{
    IconImage? Get(string path, DateTime lastWriteUtc, Action<IconImage> onLoaded);
    IconImage? GetFolder(Action<IconImage> onLoaded);
    IconImage? GetProgram(string program, Action<IconImage> onLoaded);
    IconImage? GetFavicon(string url, Action<IconImage> onLoaded);
}
