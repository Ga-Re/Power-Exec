namespace PowerExec.Core.Config;

/// <summary>Dateiendungen in den Einstellungen: mit Semikolon getrennt, z. B. „.ps1; .cmd; .exe“.</summary>
public static class ExtensionList
{
    public static string Format(IEnumerable<string> extensions) => string.Join("; ", extensions);

    /// <summary>Endungen lesen (Punkt optional, Groß-/Kleinschreibung egal); ungültige Teile werden übergangen.</summary>
    public static List<string> Parse(string? text) =>
        (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsValid)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .Distinct()
            .ToList();

    private static bool IsValid(string part)
    {
        var p = part.TrimStart('.');
        return p.Length > 0 && p.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');
    }

    /// <summary>Hinweis für das Eingabefeld, null = in Ordnung.</summary>
    public static string? Error(string? text)
    {
        var parts = (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bad = parts.Where(p => !IsValid(p)).ToList();
        if (bad.Count == 0) return null;
        return bad.Any(b => b.Contains(' ') || b.Contains(','))
            ? "Endungen bitte mit Semikolon trennen, z. B. .ps1; .cmd; .exe"
            : $"Ungültige Endung: {string.Join(", ", bad)}";
    }
}
