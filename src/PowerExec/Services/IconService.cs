using System.Collections.Concurrent;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PowerExec.UI.Services;
using PowerExec.Desktop.Interop;

namespace PowerExec.Desktop.Services;

/// <summary>
/// Lädt Symbole: Programmsymbole und Dateisymbole über die Windows-Shell, das Ordnersymbol und Favicons von Webseiten.
/// Shell-Symbole laufen auf einem eigenen STA-Thread, Favicons im Hintergrund – langsame Netzwerkpfade
/// oder Webseiten blockieren das Panel nicht.
/// </summary>
public sealed class IconService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<Action<ImageSource>>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<string> _queue = new();
    private readonly Thread _thread;
    private readonly SemaphoreSlim _webLimit = new(4);

    public IconService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _thread = new Thread(Worker) { IsBackground = true, Name = "PowerExec.Icons" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Symbol einer Datei; sofort aus dem Cache oder später über <paramref name="onLoaded"/> (UI-Thread).</summary>
    public ImageSource? Get(string path, DateTime lastWriteUtc, Action<ImageSource> onLoaded) =>
        Request("file:" + path + "|" + lastWriteUtc.Ticks, onLoaded, web: false);

    /// <summary>Das Ordnersymbol von Windows (ohne Zugriff auf den Ordner – auch bei offline Freigaben schnell).</summary>
    public ImageSource? GetFolder(Action<ImageSource> onLoaded) => Request("dir:", onLoaded, web: false);

    /// <summary>Symbol eines Programms über seinen Namen („mstsc.exe“, „wt“) oder vollen Pfad.</summary>
    public ImageSource? GetProgram(string program, Action<ImageSource> onLoaded)
    {
        var path = ResolveProgram(program);
        return path == null ? null : Request("file:" + path + "|0", onLoaded, web: false);
    }

    /// <summary>Favicon einer Webseite (Zwischenspeicher auf der Platte, 14 Tage gültig).</summary>
    public ImageSource? GetFavicon(string url, Action<ImageSource> onLoaded)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return null;
        return Request("web:" + uri.GetLeftPart(UriPartial.Authority) + "|" + uri.AbsoluteUri, onLoaded, web: true);
    }

    private ImageSource? Request(string key, Action<ImageSource> onLoaded, bool web)
    {
        var cacheKey = web ? key[..key.IndexOf('|')] : key;
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        bool first = false;
        var list = _pending.GetOrAdd(cacheKey, _ => { first = true; return new List<Action<ImageSource>>(); });
        lock (list) list.Add(onLoaded);
        if (!first) return null;
        if (web) _ = Task.Run(() => LoadWebAsync(cacheKey, key[(key.IndexOf('|') + 1)..]));
        else if (!_queue.IsAddingCompleted) _queue.Add(key);
        return null;
    }

    private void Complete(string key, ImageSource? image)
    {
        _cache[key] = image;
        if (!_pending.TryRemove(key, out var callbacks) || image == null) return;
        Action<ImageSource>[] copy;
        lock (callbacks) copy = callbacks.ToArray();
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            foreach (var cb in copy) cb(image);
        });
    }

    private void Worker()
    {
        foreach (var key in _queue.GetConsumingEnumerable())
        {
            ImageSource? image = null;
            try
            {
                if (key == "dir:") image = LoadShell("folder", FILE_ATTRIBUTE_DIRECTORY, SHGFI_USEFILEATTRIBUTES);
                else if (key.StartsWith("file:")) image = LoadShell(key[5..key.LastIndexOf('|')], 0, 0);
            }
            catch { }
            Complete(key, image);
        }
    }

    // ---- Favicons -----------------------------------------------------------

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        // Angemeldetes Konto für Intranetseiten; Proxy aus den Windows-Einstellungen
        var handler = new HttpClientHandler { UseDefaultCredentials = true, AllowAutoRedirect = true };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Power-Exec");
        return c;
    }

    private static string FaviconFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec", "favicons");

    private async Task LoadWebAsync(string key, string pageUrl)
    {
        ImageSource? image = null;
        await _webLimit.WaitAsync();
        try
        {
            var origin = key[4..];
            var file = Path.Combine(FaviconFolder, Regex.Replace(new Uri(origin).Authority, @"[^\w\.-]", "_") + ".icon");
            byte[]? bytes = null;
            try
            {
                if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromDays(14))
                    bytes = await File.ReadAllBytesAsync(file);
            }
            catch { }
            image = bytes is { Length: > 0 } ? Decode(bytes) : null;
            if (image == null)
            {
                bytes = await DownloadFaviconAsync(origin, pageUrl);
                image = bytes != null ? Decode(bytes) : null;
                if (image != null && bytes != null)
                {
                    try
                    {
                        Directory.CreateDirectory(FaviconFolder);
                        await File.WriteAllBytesAsync(file, bytes);
                    }
                    catch { }
                }
            }
        }
        catch { }
        finally
        {
            _webLimit.Release();
        }
        Complete(key, image);
    }

    private static async Task<byte[]?> DownloadFaviconAsync(string origin, string pageUrl)
    {
        // 1. Im HTML angegebenes Symbol (<link rel="icon" …>), 2. /favicon.ico
        try
        {
            using var resp = await Http.GetAsync(pageUrl, HttpCompletionOption.ResponseHeadersRead);
            if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentType?.MediaType?.Contains("html") == true)
            {
                var html = await ReadLimitedAsync(resp, 512 * 1024);
                var baseUri = resp.RequestMessage?.RequestUri ?? new Uri(pageUrl);
                foreach (var href in IconLinks(System.Text.Encoding.UTF8.GetString(html)))
                {
                    if (!Uri.TryCreate(baseUri, System.Net.WebUtility.HtmlDecode(href), out var iconUri)) continue;
                    if (iconUri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue; // WPF kann kein SVG
                    var data = await TryGetBytesAsync(iconUri.AbsoluteUri);
                    if (data != null && Decode(data) != null) return data;
                }
            }
        }
        catch { }
        var ico = await TryGetBytesAsync(origin.TrimEnd('/') + "/favicon.ico");
        return ico != null && Decode(ico) != null ? ico : null;
    }

    private static IEnumerable<string> IconLinks(string html)
    {
        var links = new List<(string Href, int Rank)>();
        foreach (Match m in Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = m.Value;
            var rel = Regex.Match(tag, @"rel\s*=\s*[""']?([^""'>]+)", RegexOptions.IgnoreCase).Groups[1].Value.ToLowerInvariant();
            if (!rel.Contains("icon")) continue;
            var href = Regex.Match(tag, @"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Groups[1].Value;
            if (href.Length == 0 || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            int rank = rel.Contains("apple-touch") ? 2 : rel.Contains("mask") ? 9 : 1;
            links.Add((href, rank));
        }
        return links.OrderBy(l => l.Rank).Select(l => l.Href).Take(4);
    }

    private static async Task<byte[]?> TryGetBytesAsync(string url)
    {
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return null;
            if (resp.Content.Headers.ContentType?.MediaType?.Contains("html") == true) return null;
            var data = await ReadLimitedAsync(resp, 1024 * 1024);
            return data.Length > 0 ? data : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage resp, int limit)
    {
        await using var s = await resp.Content.ReadAsStreamAsync();
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await s.ReadAsync(buffer)) > 0 && ms.Length < limit) ms.Write(buffer, 0, read);
        return ms.ToArray();
    }

    /// <summary>ICO/PNG/JPG/GIF/BMP dekodieren; bei mehreren Größen die passendste (≈32 px) wählen.</summary>
    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            var frame = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 32) + (f.PixelWidth < 16 ? 100 : 0)).First();
            if (frame.PixelWidth < 8) return null;
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    // ---- Shell ----------------------------------------------------------------

    /// <summary>Programmnamen über Pfad, System32 und PATH auflösen.</summary>
    public static string? ResolveProgram(string program) => ProgramResolver.Resolve(program);

    private static ImageSource? LoadShell(string path, uint attributes, uint extraFlags)
    {
        // Großes Symbol (32 px), ohne Verknüpfungspfeil
        var icon = NativeMethods.GetShellIcon(path, attributes, SHGFI_ICON | SHGFI_LARGEICON | extraFlags);
        if (icon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DestroyIcon(icon);
        }
    }

    public void Dispose() => _queue.CompleteAdding();

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
}
