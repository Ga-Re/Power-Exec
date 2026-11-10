using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace PowerExec.Core.Localization;

/// <summary>
/// Übersetzung der Oberfläche. Schlüssel ist der deutsche Text im Quelltext; die Texte je Sprache stehen im Ordner
/// <c>Translations</c> des Repositorys (<c>de.json</c>, <c>en.json</c>, eingebettet). Fehlt eine Übersetzung, erscheint der Schlüssel.
/// </summary>
public static class L
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> English = new(() => LoadEmbedded("en.json"));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> German = new(() => LoadEmbedded("de.json"));
    private static readonly ConcurrentDictionary<string, byte> MissingKeys = new();

    /// <summary>Aktive Sprache: "de" oder "en".</summary>
    public static string Language { get; private set; } = "de";
    public static bool IsEnglish => Language == "en";

    /// <summary>Einstellung "" (wie Windows), "de" oder "en" anwenden.</summary>
    public static void Use(string? setting, CultureInfo? windows = null)
    {
        Language = Resolve(setting, windows ?? CultureInfo.CurrentUICulture);
    }

    /// <summary>Sprache aus Einstellung und Windows-Sprache: Deutsch nur bei deutschem Windows, sonst Englisch.</summary>
    public static string Resolve(string? setting, CultureInfo windows) => setting switch
    {
        "de" => "de",
        "en" => "en",
        _ => windows.TwoLetterISOLanguageName == "de" ? "de" : "en"
    };

    /// <summary>Text in der aktiven Sprache.</summary>
    public static string T(string german)
    {
        if (string.IsNullOrEmpty(german)) return german;
        if (Language != "en") return German.Value.TryGetValue(german, out var de) && de.Length > 0 ? de : german;
        if (English.Value.TryGetValue(german, out var en)) return en;
        MissingKeys.TryAdd(german, 0);
        return german;
    }

    /// <summary>Formatierter Text (Platzhalter {0}, {1} …) in der aktiven Sprache.</summary>
    public static string F(string german, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(german), args);

    /// <summary>Alle englischen Übersetzungen (für Tests).</summary>
    public static IReadOnlyDictionary<string, string> EnglishTable => English.Value;

    /// <summary>Alle deutschen Texte (für Tests).</summary>
    public static IReadOnlyDictionary<string, string> GermanTable => German.Value;

    /// <summary>Im Englischen angefragte, aber nicht übersetzte Texte (Diagnose).</summary>
    public static IReadOnlyCollection<string> Missing => MissingKeys.Keys.ToList();

    private static IReadOnlyDictionary<string, string> LoadEmbedded(string name)
    {
        var asm = typeof(L).Assembly;
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("Localization." + name, StringComparison.Ordinal));
        if (res == null) return new Dictionary<string, string>();
        using var stream = asm.GetManifestResourceStream(res)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
    }
}
