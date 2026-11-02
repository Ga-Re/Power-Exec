using System.Text.RegularExpressions;

namespace PowerExec.Core.Scanning;

public enum ExclusionKind { File, Type, Pattern }

/// <summary>
/// Prüft Ausschlüsse. Ein Eintrag kann sein:
/// <list type="bullet">
/// <item>Dateiname ohne Platzhalter, z. B. <c>Blah.ps1</c> (oder relativer Pfad <c>Unterordner\Blah.ps1</c>)</item>
/// <item>Typ, z. B. <c>*.exe</c></item>
/// <item>Muster mit * und ?, z. B. <c>*\Archiv\*</c> oder <c>_*</c></item>
/// </list>
/// Muster werden gegen den Dateinamen und gegen den relativen Pfad (mit führendem \) geprüft.
/// </summary>
public sealed class ExclusionMatcher
{
    private readonly List<(Regex Regex, string Source)> _patterns = new();
    private readonly Dictionary<string, string> _exact = new(StringComparer.OrdinalIgnoreCase);

    public ExclusionMatcher(IEnumerable<string> exclusions)
    {
        foreach (var raw in exclusions)
        {
            var e = Normalize(raw);
            if (e.Length == 0) continue;
            if (e.IndexOfAny(['*', '?']) < 0)
                _exact[e.TrimStart('\\')] = raw.Trim();
            else
                _patterns.Add((new Regex("^" + Regex.Escape(e).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), raw.Trim()));
        }
    }

    public bool IsEmpty => _exact.Count == 0 && _patterns.Count == 0;


    /// <param name="relativePath">Pfad relativ zum Stammordner, z. B. <c>Archiv\alt.ps1</c>.</param>
    public bool IsExcluded(string relativePath) => Match(relativePath) != null;

    /// <summary>Der Ausschluss, der auf die Datei zutrifft, oder null.</summary>
    public string? Match(string relativePath)
    {
        var rel = Normalize(relativePath).TrimStart('\\');
        var name = rel.Contains('\\') ? rel[(rel.LastIndexOf('\\') + 1)..] : rel;
        if (_exact.TryGetValue(name, out var e1)) return e1;
        if (_exact.TryGetValue(rel, out var e2)) return e2;
        var withSlash = "\\" + rel;
        foreach (var (regex, source) in _patterns)
            if (regex.IsMatch(name) || regex.IsMatch(withSlash)) return source;
        return null;
    }

    public static ExclusionKind KindOf(string exclusion)
    {
        var e = Normalize(exclusion);
        if (e.IndexOfAny(['*', '?']) < 0) return ExclusionKind.File;
        if (e.StartsWith("*.") && e.IndexOfAny(['\\', '*', '?'], 2) < 0) return ExclusionKind.Type;
        return ExclusionKind.Pattern;
    }

    private static string Normalize(string s) => s.Trim().Replace('/', '\\');
}
