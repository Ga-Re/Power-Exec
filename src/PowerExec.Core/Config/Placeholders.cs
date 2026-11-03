using System.Text.RegularExpressions;

namespace PowerExec.Core.Config;

/// <summary>Ein Platzhalter wie <c>{{Ticket}}</c> oder <c>{{Rechner:SRV-01}}</c> (mit Vorgabewert).</summary>
public sealed record Placeholder(string Name, string Default);

/// <summary>
/// Platzhalter in eigenen Einträgen: <c>{{Name}}</c> bzw. <c>{{Name:Vorgabe}}</c>. Doppelte geschweifte Klammern,
/// damit PowerShell-Code mit <c>{ … }</c> nicht stört.
/// </summary>
public static class Placeholders
{
    private static readonly Regex Pattern = new(@"\{\{\s*([^{}:]+?)\s*(?::([^{}]*))?\}\}", RegexOptions.Compiled);

    public static IReadOnlyList<Placeholder> Find(params string?[] texts)
    {
        var result = new List<Placeholder>();
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            foreach (Match m in Pattern.Matches(text))
            {
                var name = m.Groups[1].Value.Trim();
                if (IsBuiltin(name)) continue; // {{$Datum}} usw. werden nicht abgefragt
                if (name.Length == 0 || result.Any(p => p.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))) continue;
                result.Add(new Placeholder(name, m.Groups[2].Success ? m.Groups[2].Value : string.Empty));
            }
        }
        return result;
    }

    /// <summary>Eingebaute Variablen ohne Abfrage: {{$Datum}}, {{$Uhrzeit}}, {{$Benutzer}}, {{$Computer}}, {{$Zwischenablage}}.</summary>
    public static readonly IReadOnlyList<string> BuiltinNames = new[] { "$Datum", "$Uhrzeit", "$Benutzer", "$Computer", "$Zwischenablage" };

    /// <summary>Liefert den Text der Zwischenablage (setzt die App).</summary>
    public static Func<string>? ClipboardText { get; set; }

    public static bool IsBuiltin(string name) => name.StartsWith('$');

    public static bool HasBuiltins(params string?[] texts) =>
        texts.Any(t => !string.IsNullOrEmpty(t) && Pattern.Matches(t).Any(m => IsBuiltin(m.Groups[1].Value.Trim())));

    public static string? BuiltinValue(string name, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        return name.ToLowerInvariant() switch
        {
            "$datum" => t.ToString("dd.MM.yyyy"),
            "$uhrzeit" => t.ToString("HH:mm"),
            "$benutzer" => Environment.UserName,
            "$computer" => Environment.MachineName,
            "$zwischenablage" => ClipboardText?.Invoke() ?? string.Empty,
            _ => null
        };
    }

    public static string Apply(string? text, IReadOnlyDictionary<string, string> values, bool urlEncode = false)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        return Pattern.Replace(text, m =>
        {
            var name = m.Groups[1].Value.Trim();
            if (IsBuiltin(name) && BuiltinValue(name) is { } builtin) return urlEncode ? Uri.EscapeDataString(builtin) : builtin;
            var value = values.FirstOrDefault(kv => kv.Key.Equals(name, StringComparison.CurrentCultureIgnoreCase)).Value
                        ?? (m.Groups[2].Success ? m.Groups[2].Value : string.Empty);
            return urlEncode ? Uri.EscapeDataString(value) : value;
        });
    }
}
