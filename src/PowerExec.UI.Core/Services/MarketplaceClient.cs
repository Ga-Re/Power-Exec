namespace PowerExec.Services;

/// <summary>Lädt Index und Dateien des Marktplatzes (HTTPS, mit Zeitlimit).</summary>
public static class MarketplaceClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static MarketplaceClient() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("Power-Exec-Marktplatz");

    public static async Task<string> DownloadTextAsync(Uri uri)
    {
        using var response = await Http.GetAsync(uri).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Nicht gefunden: {uri} – ist das Repository öffentlich und der Pfad richtig?");
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿');
    }
}
