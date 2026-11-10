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
                if (IsBuiltin(name)) continue; // {{$Date}} usw. werden nicht abgefragt
                if (name.Length == 0 || result.Any(p => p.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))) continue;
                result.Add(new Placeholder(name, m.Groups[2].Success ? m.Groups[2].Value : string.Empty));
            }
        }
        return result;
    }

    /// <summary>Eingebaute Variablen ohne Abfrage: {{$Date}}, {{$Time}}, {{$User}}, {{$Computer}}, {{$Clipboard}}.</summary>
    public static readonly IReadOnlyList<string> BuiltinNames = Variables.Builtins.Select(b => "$" + b.Name).ToList();

    /// <summary>Liefert den Text der Zwischenablage (setzt die App).</summary>
    public static Func<string>? ClipboardText { get; set; }

    /// <summary>
    /// Löst {{$Variablen}} auf – eingebaute und eigene (setzt die App, siehe <see cref="VariableResolver"/>). Liefert null
    /// für unbekannte Variablen. Ohne Resolver gelten nur die eingebauten mit Standardformat.
    /// </summary>
    public static Func<string, string?>? Resolver { get; set; }

    /// <summary>{{$Name}} ist eine Variable (eingebaut oder eigen) – sie wird nicht abgefragt, sondern eingesetzt.</summary>
    public static bool IsBuiltin(string name) => name.StartsWith('$');

    /// <summary>Namen aller {{$Variablen}} in den Texten (mit $).</summary>
    public static IReadOnlyList<string> VariableNames(params string?[] texts) =>
        texts.Where(t => !string.IsNullOrEmpty(t))
             .SelectMany(t => Pattern.Matches(t!).Select(m => m.Groups[1].Value.Trim()))
             .Where(IsBuiltin).ToList();

    public static bool HasBuiltins(params string?[] texts) => VariableNames(texts).Count > 0;

    /// <summary>Wert einer Variable: über den Resolver der App, sonst die eingebauten mit Standardformat.</summary>
    public static string? BuiltinValue(string name, DateTime? now = null)
    {
        if (Resolver != null && now == null) return Resolver(name);
        var t = now ?? DateTime.Now;
        return Variables.Normalize(name).ToLowerInvariant() switch
        {
            "date" => t.ToString(Variables.DefaultDateFormat),
            "time" => t.ToString(Variables.DefaultTimeFormat),
            "user" => Environment.UserName,
            "computer" => Environment.MachineName,
            "clipboard" => ClipboardText?.Invoke() ?? string.Empty,
            _ => null
        };
    }

    /// <summary>
    /// Platzhalter und Variablen einsetzen. <paramref name="powerShell"/>: Variablenwerte als sicheren String für
    /// PowerShell-Code (Anführungszeichen verdoppelt). Unbekannte Variablen bleiben stehen.
    /// </summary>
    public static string Apply(string? text, IReadOnlyDictionary<string, string> values, bool urlEncode = false, bool powerShell = false)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        // Variablen vor dem ? einer URL (Server, Basisadresse) bleiben unverändert, dahinter werden sie codiert
        var query = urlEncode ? text.IndexOf('?') : -1;
        return Pattern.Replace(text, m =>
        {
            var name = m.Groups[1].Value.Trim();
            if (IsBuiltin(name))
            {
                if (BuiltinValue(name) is not { } variable) return m.Value;
                if (powerShell) variable = Variables.EscapeForPowerShell(variable);
                return urlEncode && query >= 0 && m.Index > query ? Uri.EscapeDataString(variable) : variable;
            }
            var value = values.FirstOrDefault(kv => kv.Key.Equals(name, StringComparison.CurrentCultureIgnoreCase)).Value
                        ?? (m.Groups[2].Success ? m.Groups[2].Value : string.Empty);
            return urlEncode ? Uri.EscapeDataString(value) : value;
        });
    }

    /// <summary>{{$Variablen}} in Script-Parameterwerten einsetzen (Texte und Textlisten; andere Werte unverändert).</summary>
    public static IReadOnlyDictionary<string, object> ApplyVariables(IReadOnlyDictionary<string, object> parameters)
    {
        if (!parameters.Values.Any(v => v is string s && s.Contains("{{$") || v is IEnumerable<string> l && l.Any(x => x.Contains("{{$"))))
            return parameters;
        return parameters.ToDictionary(kv => kv.Key, kv => kv.Value switch
        {
            string s => (object)ApplyVariables(s),
            string[] a => a.Select(x => ApplyVariables(x)).ToArray(),
            IEnumerable<string> l => l.Select(x => ApplyVariables(x)).ToList(),
            var other => other
        });
    }

    /// <summary>Vorschau: {{$Variablen}} durch Anzeigewerte ersetzen (Geheimes verdeckt, nichts wird ausgeführt).</summary>
    public static string PreviewVariables(string? text, Func<string, string> preview)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        return Pattern.Replace(text, m => IsBuiltin(m.Groups[1].Value.Trim()) ? preview(m.Groups[1].Value.Trim()) : m.Value);
    }

    /// <summary>Nur Variablen einsetzen (Platzhalter bleiben) – für Vorschau und Parameterwerte.</summary>
    public static string ApplyVariables(string? text, bool powerShell = false)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        return Pattern.Replace(text, m =>
        {
            var name = m.Groups[1].Value.Trim();
            if (!IsBuiltin(name) || BuiltinValue(name) is not { } v) return m.Value;
            return powerShell ? Variables.EscapeForPowerShell(v) : v;
        });
    }
}
