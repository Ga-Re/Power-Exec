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
            AppLog.Error("Icons could not be loaded", ex);
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
                    AppLog.Error($"Icon {key} is invalid", ex);
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
            catch { /* kein lesbares Bild: der Eintrag zeigt das Standardsymbol */ }
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
        new IconChoice(new IconRef(IconSource.Glyph, g.Code), PowerExec.Core.Localization.L.T(g.Name), g.Name + " " + PowerExec.Core.Localization.L.T(g.Name)));

    public static IEnumerable<IconChoice> Emoji() => EmojiList.Select(e =>
        new IconChoice(new IconRef(IconSource.Emoji, e.Char), PowerExec.Core.Localization.L.T(e.Name), e.Name + " " + PowerExec.Core.Localization.L.T(e.Name)));

    private static string Pretty(string key)
    {
        var s = key.Replace('_', ' ').Replace('-', ' ');
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
    }

    /// <summary>
    /// Segoe Fluent Icons / MDL2 Assets (auf Windows 10 und 11 vorhanden). Der Name ist englisch und wird wie bei den
    /// Emoji zur Laufzeit übersetzt; gesucht wird im englischen und im übersetzten Namen.
    /// </summary>
    private static readonly (string Code, string Name)[] SegoeGlyphs =
    {
        ("E8B7", "Folder"), ("E838", "Folder open"), ("E8A5", "Document"), ("E756", "Terminal command"),
        ("E943", "Code"), ("E713", "Settings"), ("E90F", "Wrench repair"), ("E7EF", "Admin shield"),
        ("E72E", "Lock"), ("E8D7", "Permissions"), ("E77B", "User"), ("E716", "People group"),
        ("E7F4", "Server desktop pc"), ("E977", "PC monitor"), ("E8AF", "Remote connect"), ("E774", "Globe web"),
        ("E71B", "Link"), ("E968", "Network"), ("E701", "Wifi"), ("E839", "Ethernet"),
        ("EDA2", "Hard drive"), ("E8F1", "Database library"), ("E753", "Cloud"), ("E896", "Download"),
        ("E898", "Upload"), ("E72C", "Refresh"), ("E768", "Play"), ("E71A", "Stop"),
        ("E7BA", "Warning"), ("E946", "Info"), ("E897", "Help"), ("E734", "Favorite star"),
        ("E8EC", "Tag"), ("E8F4", "New folder"), ("E710", "Add"), ("E74D", "Delete"),
        ("E70F", "Edit"), ("E8C8", "Copy"), ("E721", "Search"), ("E715", "Mail"),
        ("E717", "Phone"), ("E787", "Calendar"), ("E823", "Clock"), ("E81C", "History"),
        ("E9D9", "Chart"), ("E9F9", "Report"), ("E8F2", "Chat"), ("E8BD", "Message"),
        ("E749", "Printer"), ("E7F8", "Device"), ("E772", "Camera"), ("E767", "Volume"),
        ("E83F", "Battery"), ("E945", "Lightning"), ("E706", "Sun brightness"), ("E7E8", "Power"),
        ("E8CB", "Sort"), ("E71C", "Filter"), ("E8FD", "List"), ("E80A", "Grid"),
        ("E8A7", "Open app"), ("E7C3", "Page"), ("E8E5", "Open file"), ("E8B5", "Import"),
        ("EDE1", "Export"), ("E7B8", "Package"), ("E9CE", "Teamwork"), ("E8A1", "Preview"),
        ("E7AD", "Rotate"), ("E8D2", "Font"), ("E790", "Color"), ("E8B9", "Picture"),
        ("E714", "Video"), ("E8D6", "Music"), ("E7FC", "Game"), ("E8A0", "Home"),
        ("E80F", "House home"), ("E7C1", "Flag"), ("E707", "Map pin"), ("E909", "Map"),
        ("E7BE", "Education"), ("E8F3", "Book"), ("E73E", "Check"), ("E711", "Close"),
        ("E8BB", "Cancel"), ("E9E9", "Settings gear"), ("EA86", "Puzzle"), ("E95E", "Rocket"),
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
