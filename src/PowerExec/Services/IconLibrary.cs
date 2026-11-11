using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PowerExec.Core.Config;
using PowerExec.UI.Services;

namespace PowerExec.Desktop.Services;

/// <summary>Ein Symbol in der Auswahl.</summary>
public sealed record IconChoice(IconRef Ref, string Name, string Search);

/// <summary>
/// Eingebettete Symbole: Fluent UI System Icons (24 regular) und Heroicons (24 outline), beide MIT-Lizenz,
/// dazu eine Auswahl an Segoe-Fluent-Glyphen und Emoji. Geometrien werden einmalig erzeugt und eingefroren.
/// </summary>
public static class IconLibrary
{
    private static readonly Lazy<(Dictionary<string, string> Fluent, Dictionary<string, string> Hero)> Data = new(Load);
    private static readonly Dictionary<string, Geometry?> GeometryCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ImageSource?> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    private static (Dictionary<string, string>, Dictionary<string, string>) Load()
    {
        try
        {
            using var stream = typeof(IconLibrary).Assembly.GetManifestResourceStream("PowerExec.icons.json");
            if (stream == null) return (new(), new());
            using var doc = JsonDocument.Parse(stream);
            Dictionary<string, string> Read(string name) =>
                doc.RootElement.TryGetProperty(name, out var e)
                    ? e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
                    : new Dictionary<string, string>();
            return (Read("fluent"), Read("hero"));
        }
        catch (Exception ex)
        {
            AppLog.Error("Symbole konnten nicht geladen werden", ex);
            return (new(), new());
        }
    }

    public static int FluentCount => Data.Value.Fluent.Count;
    public static int HeroCount => Data.Value.Hero.Count;

    /// <summary>Pfad-Geometrie für Fluent (gefüllt) bzw. Heroicons (Linien); null, wenn unbekannt.</summary>
    public static Geometry? GeometryOf(IconRef icon)
    {
        if (icon.Source is not (IconSource.Fluent or IconSource.Hero)) return null;
        var key = icon.ToString();
        lock (GeometryCache)
        {
            if (GeometryCache.TryGetValue(key, out var g)) return g;
            var dict = icon.Source == IconSource.Fluent ? Data.Value.Fluent : Data.Value.Hero;
            Geometry? geometry = null;
            if (dict.TryGetValue(icon.Value, out var data))
            {
                try
                {
                    geometry = Geometry.Parse(data);
                    geometry.Freeze();
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Symbol {key} ist ungültig", ex);
                }
            }
            GeometryCache[key] = geometry;
            return geometry;
        }
    }

    public static ImageSource? ImageOf(string path)
    {
        lock (ImageCache)
        {
            if (ImageCache.TryGetValue(path, out var cached)) return cached;
            ImageSource? img = null;
            try
            {
                if (File.Exists(path))
                {
                    if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 64;
                        bmp.UriSource = new Uri(path);
                        bmp.EndInit();
                        bmp.Freeze();
                        img = bmp;
                    }
                }
            }
            catch { }
            ImageCache[path] = img;
            return img;
        }
    }

    // ---- Auswahl ----------------------------------------------------------------

    public static IEnumerable<IconChoice> Fluent() =>
        Data.Value.Fluent.Keys.Select(k => new IconChoice(new IconRef(IconSource.Fluent, k), Pretty(k), k.Replace('_', ' ')));

    public static IEnumerable<IconChoice> Hero() =>
        Data.Value.Hero.Keys.Select(k => new IconChoice(new IconRef(IconSource.Hero, k), Pretty(k), k.Replace('-', ' ')));

    public static IEnumerable<IconChoice> Glyphs() => SegoeGlyphs.Select(g =>
        new IconChoice(new IconRef(IconSource.Glyph, g.Code), g.Name, g.Name));

    public static IEnumerable<IconChoice> Emoji() => EmojiList.Select(e =>
        new IconChoice(new IconRef(IconSource.Emoji, e.Char), PowerExec.Core.Localization.L.T(e.Name), e.Name + " " + PowerExec.Core.Localization.L.T(e.Name)));

    private static string Pretty(string key)
    {
        var s = key.Replace('_', ' ').Replace('-', ' ');
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
    }

    /// <summary>Segoe Fluent Icons / MDL2 Assets (auf Windows 10 und 11 vorhanden).</summary>
    private static readonly (string Code, string Name)[] SegoeGlyphs =
    {
        ("E8B7", "Ordner folder"), ("E838", "Ordner öffnen folder open"), ("E8A5", "Dokument document"), ("E756", "Konsole terminal command"),
        ("E943", "Code"), ("E713", "Einstellungen settings"), ("E90F", "Werkzeug wrench repair"), ("E7EF", "Admin shield"),
        ("E72E", "Schloss lock"), ("E8D7", "Berechtigung permissions"), ("E77B", "Person user"), ("E716", "Personen people group"),
        ("E7F4", "Server desktop pc"), ("E977", "PC monitor"), ("E8AF", "Remote verbinden connect"), ("E774", "Welt globe web"),
        ("E71B", "Link"), ("E968", "Netzwerk network"), ("E701", "WLAN wifi"), ("E839", "Ethernet"),
        ("EDA2", "Festplatte hard drive"), ("E8F1", "Datenbank library"), ("E753", "Cloud"), ("E896", "Download"),
        ("E898", "Upload"), ("E72C", "Aktualisieren refresh"), ("E768", "Start play"), ("E71A", "Stopp stop"),
        ("E7BA", "Warnung warning"), ("E946", "Info"), ("E897", "Hilfe help"), ("E734", "Stern favorite"),
        ("E8EC", "Etikett tag"), ("E8F4", "Neuer Ordner new folder"), ("E710", "Hinzufügen add"), ("E74D", "Löschen delete"),
        ("E70F", "Bearbeiten edit"), ("E8C8", "Kopieren copy"), ("E721", "Suchen search"), ("E715", "Mail"),
        ("E717", "Telefon phone"), ("E787", "Kalender calendar"), ("E823", "Uhr clock"), ("E81C", "Verlauf history"),
        ("E9D9", "Diagramm chart"), ("E9F9", "Bericht report"), ("E8F2", "Chat"), ("E8BD", "Nachricht message"),
        ("E749", "Drucker print"), ("E7F8", "Gerät device"), ("E772", "Kamera camera"), ("E767", "Lautstärke volume"),
        ("E83F", "Batterie battery"), ("E945", "Blitz lightning"), ("E706", "Sonne brightness"), ("E7E8", "Energie power"),
        ("E8CB", "Sortieren sort"), ("E71C", "Filter"), ("E8FD", "Liste list"), ("E80A", "Raster grid"),
        ("E8A7", "App öffnen open"), ("E7C3", "Seite page"), ("E8E5", "Datei öffnen open file"), ("E8B5", "Import"),
        ("EDE1", "Export"), ("E7B8", "Paket package"), ("E9CE", "Teamarbeit"), ("E8A1", "Vorschau preview"),
        ("E7AD", "Drehen rotate"), ("E8D2", "Schrift font"), ("E790", "Farbe color"), ("E8B9", "Bild picture"),
        ("E714", "Video"), ("E8D6", "Musik music"), ("E7FC", "Spiel game"), ("E8A0", "Zuhause home"),
        ("E80F", "Haus home"), ("E7C1", "Markierung flag"), ("E707", "Ort map pin"), ("E909", "Karte map"),
        ("E7BE", "Bildung education"), ("E8F3", "Buch book"), ("E73E", "Häkchen check"), ("E711", "Schließen close"),
        ("E8BB", "Abbrechen cancel"), ("E9E9", "Zahnrad settings gear"), ("EA86", "Puzzle"), ("E95E", "Rakete rocket"),
    };

    private static readonly (string Char, string Name)[] EmojiList =
    {
        ("🖥️", "Computer"), ("💻", "Laptop"), ("🖧", "Network"), ("🌐", "Web globe"), ("🔗", "Link"), ("📁", "Folder"), ("📂", "Folder open"),
        ("📄", "Document"), ("📝", "Note"), ("📋", "Clipboard"), ("📊", "Chart"), ("📈", "Statistics"), ("🗄️", "Archive"),
        ("🛠️", "Tool"), ("🔧", "Wrench"), ("⚙️", "Settings"), ("🔒", "Lock"), ("🔑", "Key"), ("🛡️", "Shield"),
        ("👤", "User"), ("👥", "User group"), ("🏢", "Company building"), ("🏠", "Home"), ("☁️", "Cloud"), ("🗃️", "Card index"),
        ("💾", "Save floppy disk"), ("🖨️", "Printer"), ("📞", "Phone"), ("✉️", "Mail"), ("📅", "Calendar"), ("⏰", "Alarm clock"),
        ("🚀", "Rocket launch"), ("⚡", "Lightning"), ("🔥", "Fire"), ("⭐", "Star"), ("❤️", "Heart"), ("✅", "Done"), ("❌", "Error"),
        ("⚠️", "Warning"), ("ℹ️", "Info"), ("❓", "Question"), ("🔍", "Search magnifier"), ("🧪", "Test lab"), ("🐞", "Bug"),
        ("📦", "Package"), ("🧩", "Puzzle"), ("🎯", "Target"), ("🏷️", "Label"), ("📌", "Pin"), ("🗑️", "Recycle bin"), ("🔄", "Refresh"),
        ("▶️", "Start"), ("⏹️", "Stop"), ("🎮", "Game"), ("🎵", "Music"), ("📷", "Camera"), ("🎬", "Video"), ("🧰", "Toolbox"),
        ("🐧", "Linux"), ("🪟", "Windows window"), ("🐳", "Docker"), ("🧑‍💻", "Developer"), ("📡", "Antenna"), ("🔌", "Plug"),
    };
}
