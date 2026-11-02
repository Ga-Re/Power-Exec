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
    private readonly List<Regex> _patterns = new();
    private readonly HashSet<string> _exact = new(StringComparer.OrdinalIgnoreCase);

    public ExclusionMatcher(IEnumerable<string> exclusions)
    {
        foreach (var raw in exclusions)
        {
            var e = Normalize(raw);
            if (e.Length == 0) continue;
            if (e.IndexOfAny(['*', '?']) < 0)
                _exact.Add(e.TrimStart('\\'));
            else
                _patterns.Add(new Regex("^" + Regex.Escape(e).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }
    }

    public bool IsEmpty => _exact.Count == 0 && _patterns.Count == 0;

    /// <param name="relativePath">Pfad relativ zum Stammordner, z. B. <c>Archiv\alt.ps1</c>.</param>
    public bool IsExcluded(string relativePath)
    {
        var rel = Normalize(relativePath).TrimStart('\\');
        var name = rel.Contains('\\') ? rel[(rel.LastIndexOf('\\') + 1)..] : rel;
        if (_exact.Contains(name) || _exact.Contains(rel)) return true;
        var withSlash = "\\" + rel;
        foreach (var p in _patterns)
            if (p.IsMatch(name) || p.IsMatch(withSlash)) return true;
        return false;
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
