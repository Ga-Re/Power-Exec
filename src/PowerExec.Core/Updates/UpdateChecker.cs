using System.Net.Http.Headers;
using System.Text.Json;

namespace PowerExec.Core.Updates;

public sealed record ReleaseInfo(Version Version, string Tag, string Name, bool IsPrerelease, string HtmlUrl,
    string? ExeUrl, string? ChecksumUrl, string Notes);

/// <summary>Prüft die GitHub Releases von Ga-Re/Power-Exec auf neuere Versionen.</summary>
public static class UpdateChecker
{
    public const string Owner = "Ga-Re";
    public const string Repo = "Power-Exec";
    public const string ExeAssetName = "PowerExec.exe";
    public const string ChecksumAssetName = "PowerExec.exe.sha256";
    public static string ReleasesPage => $"https://github.com/{Owner}/{Repo}/releases";

    public static async Task<ReleaseInfo?> CheckAsync(Version current, bool includePrereleases, HttpClient http, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=20");
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Power-Exec", current.ToString()));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return FindNewer(json, current, includePrereleases);
    }

    /// <summary>Sucht im JSON der Releases-API die höchste Version, die neuer ist als <paramref name="current"/>.</summary>
    public static ReleaseInfo? FindNewer(string releasesJson, Version current, bool includePrereleases)
    {
        using var doc = JsonDocument.Parse(releasesJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        ReleaseInfo? best = null;
        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            if (rel.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var pre = rel.TryGetProperty("prerelease", out var p) && p.GetBoolean();
            if (pre && !includePrereleases) continue;
            var tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!TryParseVersion(tag, out var version)) continue;
            if (version <= Normalize(current)) continue;
            if (best != null && version <= best.Version) continue;

            string? exe = null, sum = null;
            if (rel.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString();
                    var url = a.GetProperty("browser_download_url").GetString();
                    if (string.Equals(name, ExeAssetName, StringComparison.OrdinalIgnoreCase)) exe = url;
                    else if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase)) sum = url;
                }
            }
            best = new ReleaseInfo(version, tag,
                rel.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag,
                pre,
                rel.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage,
                exe, sum,
                rel.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "");
        }
        return best;
    }

    /// <summary>"v1.4.0", "1.4", "v1.4.0-beta.2" → 1.4.0</summary>
    public static bool TryParseVersion(string tag, out Version version)
    {
        var s = tag.Trim().TrimStart('v', 'V');
        var dash = s.IndexOfAny(['-', '+']);
        if (dash >= 0) s = s[..dash];
        if (!Version.TryParse(s, out var v))
        {
            if (!int.TryParse(s, out var major) || major < 0)
            {
                version = new Version(0, 0, 0);
                return false;
            }
            v = new Version(major, 0);
        }
        version = Normalize(v);
        return true;
    }

    public static Version Normalize(Version v) =>
        new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>Liest die erwartete SHA-256-Prüfsumme aus dem Inhalt einer .sha256-Datei.</summary>
    public static string? ParseChecksum(string content)
    {
        var token = content.Trim().Split(new[] { ' ', '\t', '\r', '\n', '*' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token != null && token.Length == 64 && token.All(Uri.IsHexDigit) ? token.ToLowerInvariant() : null;
    }
}
