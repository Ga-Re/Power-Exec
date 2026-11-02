using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerExec.Core.Config;

namespace PowerExec.Core.Execution;

public enum RemoteStatus { Running, Success, Error, Unreachable }

public sealed class RemoteResult
{
    public string Computer { get; set; } = string.Empty;
    public RemoteStatus Status { get; set; }
    public int DurationMs { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
}

/// <summary>Bereitet den Remote-Runner vor und wertet seine Ausgabe aus.</summary>
public static class RemoteRunner
{
    private static readonly Lazy<string> ScriptText = new(() =>
    {
        using var s = typeof(RemoteRunner).Assembly.GetManifestResourceStream("PowerExec.RemoteRunner.ps1")
                      ?? throw new InvalidOperationException("RemoteRunner.ps1 fehlt als Ressource.");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    });

    public static string Script => ScriptText.Value;

    /// <summary>Argumente für powershell.exe, um den Runner zu starten.</summary>
    public static string ProcessArguments =>
        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + CommandBuilder.Encode(Script);

    /// <summary>Erste stdin-Zeile: Konfiguration als Base64(UTF-8-JSON). Enthält keine Anmeldedaten.</summary>
    public static string BuildConfigLine(ExecutionRequest r, AppSettings settings, bool useCredential)
    {
        var path = r.EffectivePath;
        var modules = r.Item.RequiredModules.ToList();
        if (r.CopyFolder)
        {
            // Module, die im Scriptordner liegen, werden mitkopiert und müssen nicht auf dem Ziel vorhanden sein.
            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            modules = modules.Where(m => !Directory.Exists(Path.Combine(dir, m)) &&
                                         !Directory.Exists(Path.Combine(dir, "Modules", m))).ToList();
        }

        var cfg = new Dictionary<string, object?>
        {
            ["Targets"] = r.Targets,
            ["ScriptPath"] = path,
            ["ScriptDirectory"] = Path.GetDirectoryName(path),
            ["ScriptFileName"] = Path.GetFileName(path),
            ["Parameters"] = r.Parameters,
            ["UseCredential"] = useCredential,
            ["Engine"] = r.Engine.ToString(),
            ["CopyFolder"] = r.CopyFolder,
            ["RequiredModules"] = modules,
            ["ThrottleLimit"] = settings.ThrottleLimit,
            ["UseSsl"] = settings.RemoteUseSsl,
            ["OpenTimeoutSec"] = settings.RemoteTimeoutSeconds
        };
        var json = JsonSerializer.Serialize(cfg);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static readonly JsonSerializerOptions ParseOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Wertet eine Ausgabezeile aus. Gibt null zurück, wenn die Zeile kein Ergebnis ist.</summary>
    public static RemoteResult? ParseResultLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith('{')) return null;
        try
        {
            var raw = JsonSerializer.Deserialize<RawLine>(line, ParseOptions);
            if (raw == null || !string.Equals(raw.Type, "result", StringComparison.OrdinalIgnoreCase)) return null;
            return new RemoteResult
            {
                Computer = raw.Computer ?? string.Empty,
                Status = raw.Status?.ToLowerInvariant() switch
                {
                    "success" => RemoteStatus.Success,
                    "unreachable" => RemoteStatus.Unreachable,
                    _ => RemoteStatus.Error
                },
                DurationMs = raw.DurationMs,
                Output = raw.Output ?? string.Empty,
                Error = raw.Error ?? string.Empty,
                Note = raw.Note ?? string.Empty,
                Engine = raw.Engine ?? string.Empty
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsDoneLine(string line) =>
        line.Contains("\"type\":\"done\"", StringComparison.OrdinalIgnoreCase);

    private sealed class RawLine
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("computer")] public string? Computer { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("durationMs")] public int DurationMs { get; set; }
        [JsonPropertyName("output")] public string? Output { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("engine")] public string? Engine { get; set; }
    }
}
