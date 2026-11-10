using PowerExec.Core.Localization;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Sockets;
using PowerExec.Core.Config;
using PowerExec.Infrastructure;

namespace PowerExec.Services;

public sealed record HealthResult(bool Ok, string Message, DateTime CheckedUtc);

/// <summary>
/// Prüft eigene Einträge im Hintergrund: Webseite antwortet, Ordner/Datei vorhanden, RDP-/SSH-Port offen,
/// Programm auffindbar. Ergebnisse gelten 10 Minuten. Einträge mit Platzhaltern werden übersprungen.
/// </summary>
public sealed class HealthService
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, HealthResult> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly IUiDispatcher _dispatcher;
    private readonly HttpClient _http;

    public HealthService(IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _http = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true, AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(8) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Power-Exec");
    }

    /// <summary>Wird auf dem UI-Thread ausgelöst, wenn sich ein Ergebnis geändert hat.</summary>
    public event Action? Changed;

    public HealthResult? Get(string key) => _results.TryGetValue(key, out var r) ? r : null;

    public void Forget(string key) => _results.TryRemove(key, out _);

    public static bool CanCheck(CustomEntry e) =>
        e.Kind is not CustomEntryKind.Snippet && e.Placeholders.Count == 0 && e.Target.Trim().Length > 0;

    public async Task CheckAsync(IEnumerable<CustomEntry> entries, bool force = false)
    {
        var todo = entries.Where(CanCheck)
            .Where(e => force || Get(e.Key) is not { } r || DateTime.UtcNow - r.CheckedUtc > MaxAge)
            .Where(e => _running.TryAdd(e.Key, 0))
            .ToList();
        if (todo.Count == 0) return;
        bool changed = false;
        using var limit = new SemaphoreSlim(6);
        await Task.WhenAll(todo.Select(async e =>
        {
            await limit.WaitAsync();
            try
            {
                var result = await CheckOneAsync(e);
                var old = Get(e.Key);
                _results[e.Key] = result;
                if (old == null || old.Ok != result.Ok || old.Message != result.Message) changed = true;
            }
            catch (Exception ex)
            {
                _results[e.Key] = new HealthResult(false, ex.Message, DateTime.UtcNow);
                changed = true;
            }
            finally
            {
                _running.TryRemove(e.Key, out _);
                limit.Release();
            }
        }));
        if (changed) _dispatcher.Post(() => Changed?.Invoke());
    }

    private async Task<HealthResult> CheckOneAsync(CustomEntry e)
    {
        var now = DateTime.UtcNow;
        var target = e.Target.Trim();
        switch (e.Kind)
        {
            case CustomEntryKind.Url:
                if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    return new HealthResult(true, L.T("Wird von Windows geöffnet"), now);
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Head, uri);
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    var code = (int)resp.StatusCode;
                    if (code == 405 || code == 501)
                    {
                        using var get = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                        code = (int)get.StatusCode;
                    }
                    // 401/403: Seite antwortet, verlangt aber Anmeldung – gilt als erreichbar
                    return code < 400 || code is 401 or 403
                        ? new HealthResult(true, L.F("Erreichbar (HTTP {0})", code), now)
                        : new HealthResult(false, L.F("Seite antwortet mit HTTP {0}", code), now);
                }
                catch (TaskCanceledException) { return new HealthResult(false, L.T("Keine Antwort (Zeitüberschreitung)"), now); }
                catch (HttpRequestException ex) { return new HealthResult(false, L.T("Nicht erreichbar: ") + ex.Message, now); }
            case CustomEntryKind.Folder:
                return await WithTimeout(() => Directory.Exists(target), 5000)
                    ? new HealthResult(true, L.T("Ordner vorhanden"), now)
                    : new HealthResult(false, L.T("Ordner nicht gefunden oder nicht erreichbar"), now);
            case CustomEntryKind.File:
                return await WithTimeout(() => File.Exists(target), 5000)
                    ? new HealthResult(true, L.T("Datei vorhanden"), now)
                    : new HealthResult(false, L.T("Datei nicht gefunden"), now);
            case CustomEntryKind.Rdp when e.UsesRdpFile:
                return await WithTimeout(() => File.Exists(e.RdpFile.Trim()), 5000)
                    ? new HealthResult(true, L.T(".rdp-Datei vorhanden"), now)
                    : new HealthResult(false, L.T(".rdp-Datei nicht gefunden"), now);
            case CustomEntryKind.Rdp:
            case CustomEntryKind.Ssh:
            {
                var host = target;
                int port = e.Port > 0 ? e.Port : e.Kind == CustomEntryKind.Rdp ? 3389 : 22;
                var colon = host.LastIndexOf(':');
                if (colon > 0 && int.TryParse(host[(colon + 1)..], out var p)) { port = p; host = host[..colon]; }
                using var tcp = new TcpClient();
                var connect = tcp.ConnectAsync(host, port);
                var done = await Task.WhenAny(connect, Task.Delay(3000));
                if (done == connect && !connect.IsFaulted && tcp.Connected) return new HealthResult(true, L.F("Port {0} erreichbar", port), now);
                return new HealthResult(false, connect.IsFaulted
                    ? L.F("{0}:{1} nicht erreichbar ({2})", host, port, connect.Exception?.GetBaseException().Message)
                    : L.F("{0}:{1} antwortet nicht", host, port), now);
            }
            case CustomEntryKind.Command:
            case CustomEntryKind.Tool:
                if (target.Contains(':') && !Path.IsPathRooted(target)) return new HealthResult(true, L.T("Wird von Windows geöffnet"), now);
                var exe = ProgramResolver.Resolve(target) ?? (target.EndsWith(".msc", StringComparison.OrdinalIgnoreCase)
                    ? ProgramResolver.Resolve(Path.Combine(Environment.SystemDirectory, target)) : null);
                return exe != null ? new HealthResult(true, exe, now) : new HealthResult(false, L.F("{0} wurde nicht gefunden (nicht installiert?)", target), now);
            default:
                return new HealthResult(true, string.Empty, now);
        }
    }

    private static async Task<bool> WithTimeout(Func<bool> check, int ms)
    {
        var task = Task.Run(check);
        return await Task.WhenAny(task, Task.Delay(ms)) == task && task.Result;
    }
}

/// <summary>Anzahl Dateien und Größe eines Ordners (im Hintergrund, mit Zeitlimit, 5 Minuten zwischengespeichert).</summary>
public static class FolderStats
{
    private static readonly ConcurrentDictionary<string, (DateTime At, string Text)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string? Cached(string path) =>
        Cache.TryGetValue(path, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(5) ? c.Text : null;

    public static async Task<string> GetAsync(string path)
    {
        if (Cached(path) is { } text) return text;
        text = await Task.Run(() => Compute(path, TimeSpan.FromSeconds(6), 300_000));
        Cache[path] = (DateTime.UtcNow, text);
        return text;
    }

    private static string Compute(string path, TimeSpan limit, int maxFiles)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return L.T("Ordner nicht gefunden oder nicht erreichbar");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            long files = 0, dirs = 0, bytes = 0;
            bool partial = false;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var info in dir.EnumerateFileSystemInfos("*", options))
            {
                if (info is FileInfo f) { files++; bytes += f.Length; }
                else dirs++;
                if (files >= maxFiles || (files % 500 == 0 && watch.Elapsed > limit)) { partial = true; break; }
            }
            var text = $"{files:N0} {(files == 1 ? "Datei" : "Dateien")} · {dirs:N0} {(dirs == 1 ? "Unterordner" : "Unterordner")} · {Size(bytes)}";
            if (partial) text = L.T("mindestens ") + text + L.T(" (Zählung abgebrochen)");
            text += L.F(" · geändert {0:dd.MM.yyyy HH:mm}", dir.LastWriteTime);
            return text;
        }
        catch (Exception ex)
        {
            return L.T("Ordnerinfo nicht verfügbar: ") + ex.Message;
        }
    }

    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v:0.#} {units[u]}";
    }
}
