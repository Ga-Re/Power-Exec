using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using PowerExec.Core.Localization;

namespace PowerExec.Core.Tests;

/// <summary>Englische Oberfläche: jeder Text aus XAML ({l:T '…'}) und Code (L.T/L.F) hat eine Übersetzung mit denselben Platzhaltern.</summary>
public class LocalizationTests
{
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PowerExec.sln"))) return Path.Combine(dir.FullName, "src");
        throw new DirectoryNotFoundException("src");
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Root(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string UnescapeCs(string s) => Regex.Replace(s, @"\\(u[0-9a-fA-F]{4}|.)", m => m.Groups[1].Value switch
    {
        "n" => "\n", "t" => "\t", "r" => "\r", "0" => "\0", "\\" => "\\", "\"" => "\"", "'" => "'",
        var u when u.Length == 5 && u[0] == 'u' => ((char)Convert.ToInt32(u[1..], 16)).ToString(),
        var x => "\\" + x
    });

    private static string UnescapeXaml(string s) => WebUtility.HtmlDecode(Regex.Replace(s, @"\\(.)", "$1"));

    /// <summary>Alle Schlüssel aus dem Quelltext (Datei, Text).</summary>
    public static IEnumerable<(string File, string Key)> Keys()
    {
        var xaml = new Regex(@"\{l:T '((?:[^'\\]|\\.)*)'\}");
        foreach (var f in SourceFiles("*.xaml"))
            foreach (Match m in xaml.Matches(File.ReadAllText(f)))
                yield return (Path.GetFileName(f), UnescapeXaml(m.Groups[1].Value));
        var cs = new Regex(@"\bL\.[TF]\(""((?:[^""\\]|\\.)*)""");
        foreach (var f in SourceFiles("*.cs").Where(f => !Path.GetFileName(f).StartsWith("SelfTest")))
            foreach (Match m in cs.Matches(File.ReadAllText(f)))
                yield return (Path.GetFileName(f), UnescapeCs(m.Groups[1].Value));
    }

    [Fact]
    public void EveryTextHasAnEnglishTranslation()
    {
        var keys = Keys().ToList();
        Assert.True(keys.Count > 2000, $"Nur {keys.Count} Texte gefunden – stimmt die Suche noch?");
        var missing = keys.Where(k => !L.EnglishTable.ContainsKey(k.Key)).Select(k => $"{k.File}: {k.Key}").Distinct().ToList();
        Assert.True(missing.Count == 0, "Ohne englische Übersetzung (Translations/en.json):\n" + string.Join("\n", missing.Take(50)));
        var missingDe = keys.Where(k => !L.GermanTable.ContainsKey(k.Key)).Select(k => $"{k.File}: {k.Key}").Distinct().ToList();
        Assert.True(missingDe.Count == 0, "Fehlt in Translations/de.json:\n" + string.Join("\n", missingDe.Take(50)));
    }

    [Fact]
    public void TranslationsKeepPlaceholders()
    {
        static string Holes(string s) => string.Join(",", Regex.Matches(s, @"(?<!\{)\{(\d+)[^{}]*\}(?!\})").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));
        var wrong = L.EnglishTable.Concat(L.GermanTable)
            .Where(kv => Holes(kv.Key) != Holes(kv.Value))
            .Select(kv => $"{kv.Key} → {kv.Value}").ToList();
        Assert.True(wrong.Count == 0, "Platzhalter passen nicht:\n" + string.Join("\n", wrong.Take(30)));
        foreach (var kv in L.EnglishTable.Concat(L.GermanTable).Where(kv => Regex.IsMatch(kv.Key, @"(?<!\{)\{\d")))
            _ = string.Format(CultureInfo.InvariantCulture, kv.Value, Enumerable.Range(0, 10).Select(_ => (object)new AnyFormat()).ToArray());
    }

    [Fact]
    public void LanguageFollowsWindowsUnlessChosen()
    {
        Assert.Equal("de", L.Resolve("", new CultureInfo("de-AT")));
        Assert.Equal("en", L.Resolve("", new CultureInfo("fr-FR")));
        Assert.Equal("en", L.Resolve("en", new CultureInfo("de-DE")));
        Assert.Equal("de", L.Resolve("de", new CultureInfo("en-US")));
    }

    /// <summary>Nimmt jedes Formatkürzel an – prüft nur, dass das Formatmuster gültig ist.</summary>
    private sealed class AnyFormat : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => "x";
    }
}
