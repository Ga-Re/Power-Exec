using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace PowerExec.Core.Localization;

/// <summary>
/// Übersetzung der Oberfläche. Basis ist Englisch: der englische Text im Quelltext (<c>L.T("…")</c>, <c>{l:T '…'}</c>) ist
/// zugleich der Schlüssel. Weitere Sprachen liegen im Repository unter <c>Translations/&lt;sprache&gt;.json</c>
/// (z. B. <c>de.json</c>, englischer Text → Übersetzung) und werden eingebettet. Fehlt eine Übersetzung, erscheint Englisch.
/// </summary>
public static class L
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Tables = new();
    private static readonly ConcurrentDictionary<string, byte> MissingKeys = new();

    /// <summary>Aktive Sprache (zweibuchstabig, z. B. „de“ oder „en“).</summary>
    public static string Language { get; private set; } = "de";
    public static bool IsEnglish => Language == "en";

    /// <summary>Verfügbare Sprachen: Englisch plus jede eingebettete Übersetzung.</summary>
    public static IReadOnlyList<string> Available { get; } =
        new[] { "en" }.Concat(typeof(L).Assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith("PowerExec.Localization.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
                .Select(n => n["PowerExec.Localization.".Length..^".json".Length]))
            .Distinct().ToList();

    /// <summary>Einstellung "" (wie Windows) oder ein Sprachkürzel anwenden.</summary>
    public static void Use(string? setting, CultureInfo? windows = null) =>
        Language = Resolve(setting, windows ?? CultureInfo.CurrentUICulture);

    /// <summary>Gewählte Sprache, sonst die von Windows, wenn es eine Übersetzung gibt – sonst Englisch.</summary>
    public static string Resolve(string? setting, CultureInfo windows)
    {
        if (!string.IsNullOrEmpty(setting) && Available.Contains(setting)) return setting;
        var os = windows.TwoLetterISOLanguageName;
        return Available.Contains(os) ? os : "en";
    }

    /// <summary>Text (englisch) in der aktiven Sprache.</summary>
    public static string T(string english)
    {
        if (Language == "en" || string.IsNullOrEmpty(english)) return english;
        if (Table(Language).TryGetValue(english, out var text) && text.Length > 0) return text;
        MissingKeys.TryAdd(english, 0);
        return english;
    }

    /// <summary>Formatierter Text (Platzhalter {0}, {1} …) in der aktiven Sprache.</summary>
    public static string F(string english, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(english), args);

    /// <summary>Übersetzungen einer Sprache (englischer Text → Text), leer für Englisch.</summary>
    public static IReadOnlyDictionary<string, string> Table(string language) =>
        Tables.GetOrAdd(language, LoadEmbedded);

    /// <summary>Deutsche Texte (für Tests).</summary>
    public static IReadOnlyDictionary<string, string> GermanTable => Table("de");

    /// <summary>In der aktiven Sprache angefragte, aber nicht übersetzte Texte (Diagnose).</summary>
    public static IReadOnlyCollection<string> Missing => MissingKeys.Keys.ToList();

    /// <summary>Name der Sprache in ihr selbst („Deutsch“, „English“).</summary>
    public static string NativeName(string language)
    {
        try
        {
            var n = CultureInfo.GetCultureInfo(language).NativeName;
            return n.Length > 0 ? char.ToUpper(n[0], CultureInfo.InvariantCulture) + n[1..] : language;
        }
        catch (CultureNotFoundException) { return language; }
    }

    private static IReadOnlyDictionary<string, string> LoadEmbedded(string language)
    {
        if (language == "en") return new Dictionary<string, string>();
        using var stream = typeof(L).Assembly.GetManifestResourceStream($"PowerExec.Localization.{language}.json");
        if (stream == null) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
    }
}
