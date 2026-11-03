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
                if (name.Length == 0 || result.Any(p => p.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))) continue;
                result.Add(new Placeholder(name, m.Groups[2].Success ? m.Groups[2].Value : string.Empty));
            }
        }
        return result;
    }

    public static string Apply(string? text, IReadOnlyDictionary<string, string> values, bool urlEncode = false)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        return Pattern.Replace(text, m =>
        {
            var name = m.Groups[1].Value.Trim();
            var value = values.FirstOrDefault(kv => kv.Key.Equals(name, StringComparison.CurrentCultureIgnoreCase)).Value
                        ?? (m.Groups[2].Success ? m.Groups[2].Value : string.Empty);
            return urlEncode ? Uri.EscapeDataString(value) : value;
        });
    }
}
