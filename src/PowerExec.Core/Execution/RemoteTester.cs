using PowerExec.Core.Localization;
using System.Text;
using System.Text.Json;
using PowerExec.Core.Config;

namespace PowerExec.Core.Execution;

public sealed class RemoteTestResult
{
    public string Computer { get; set; } = string.Empty;
    public bool WsMan { get; set; }
    public bool Auth { get; set; }
    public bool Ps7 { get; set; }
    public string Version { get; set; } = string.Empty;
    public int LatencyMs { get; set; }
    public string Error { get; set; } = string.Empty;

    public bool Ok => WsMan && Auth;

    public string Summary => Ok
        ? $"OK · PowerShell {Version}{(Ps7 ? " · PS 7 verfügbar" : string.Empty)} · {LatencyMs} ms"
        : (Error.Length > 0 ? Error : L.T("Nicht erreichbar"));
}

/// <summary>Vorbereitung und Auswertung des Verbindungstests (RemoteTest.ps1).</summary>
public static class RemoteTester
{
    private static readonly Lazy<string> ScriptText = new(() =>
    {
        using var s = typeof(RemoteTester).Assembly.GetManifestResourceStream("PowerExec.RemoteTest.ps1")
                      ?? throw new InvalidOperationException("RemoteTest.ps1 fehlt als Ressource.");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    });

    public static string Script => ScriptText.Value;

    public static string ProcessArguments =>
        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + CommandBuilder.Encode(Script);

    public static string BuildConfigLine(IReadOnlyList<string> targets, AppSettings settings, bool useCredential)
    {
        var cfg = new Dictionary<string, object?>
        {
            ["Targets"] = targets,
            ["UseCredential"] = useCredential,
            ["UseSsl"] = settings.RemoteUseSsl,
            ["ThrottleLimit"] = settings.ThrottleLimit,
            ["OpenTimeoutSec"] = Math.Min(30, settings.RemoteTimeoutSeconds)
        };
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cfg)));
    }

    private static readonly JsonSerializerOptions ParseOptions = new() { PropertyNameCaseInsensitive = true };

    public static RemoteTestResult? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith('{')) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var t) || t.GetString() != "test") return null;
            return JsonSerializer.Deserialize<RemoteTestResult>(line, ParseOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
