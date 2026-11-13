using System.Globalization;
using PowerExec.Core.Localization;
using System.Text.RegularExpressions;

namespace PowerExec.Core.Config;

/// <summary>Woher der Wert einer eigenen Variable kommt.</summary>
public enum VariableKind
{
    /// <summary>Fester Text, z. B. $CompanyName = „Med360°“.</summary>
    Text,
    /// <summary>Je Arbeitsbereich ein eigener Wert (Kunde A, Kunde B …); ohne eigenen Wert gilt der Standardwert.</summary>
    Workspace,
    /// <summary>Geheim: verschlüsselt gespeichert (Windows-Datenschutz des Benutzers), in der Oberfläche verdeckt.</summary>
    Secret,
    /// <summary>Wert einer Windows-Umgebungsvariable (z. B. USERDNSDOMAIN).</summary>
    Environment,
    /// <summary>Ausgabe eines PowerShell-Befehls (z. B. (Get-ADDomain).DNSRoot).</summary>
    PowerShell
}

/// <summary>Wann der PowerShell-Befehl einer Variable ausgeführt wird.</summary>
public enum VariableRefresh
{
    /// <summary>Bei jedem Start eines Eintrags – immer aktuell, aber jeder Start wartet kurz.</summary>
    EveryUse,
    /// <summary>Ergebnis eine einstellbare Zeit merken.</summary>
    Cached,
    /// <summary>Einmal je Programmsitzung.</summary>
    Session
}

/// <summary>Eine eigene Variable, im Text als <c>{{$Name}}</c> verwendet.</summary>
public sealed class UserVariable
{
    /// <summary>Name ohne $ (z. B. CompanyName).</summary>
    public string Name { get; set; } = string.Empty;
    public VariableKind Kind { get; set; }
    /// <summary>Text, Standardwert (je Arbeitsbereich), verschlüsselter Wert, Name der Umgebungsvariable bzw. PowerShell-Befehl.</summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>Nur <see cref="VariableKind.Workspace"/>: Wert je Arbeitsbereich (Name → Wert).</summary>
    public Dictionary<string, string> WorkspaceValues { get; set; } = new(StringComparer.CurrentCultureIgnoreCase);
    public VariableRefresh Refresh { get; set; } = VariableRefresh.Cached;
    /// <summary>Nur <see cref="VariableRefresh.Cached"/>: wie lange das Ergebnis gilt.</summary>
    public int CacheMinutes { get; set; } = 60;
    public string Description { get; set; } = string.Empty;

    public UserVariable Clone() => new()
    {
        Name = Name,
        Kind = Kind,
        Value = Value,
        Refresh = Refresh,
        CacheMinutes = CacheMinutes,
        Description = Description,
        WorkspaceValues = new Dictionary<string, string>(WorkspaceValues, StringComparer.CurrentCultureIgnoreCase)
    };
}

/// <summary>Eingebaute und eigene Variablen: Namen, Prüfung, Auflösung.</summary>
public static class Variables
{
    public const string DefaultDateFormat = "dd.MM.yyyy";
    public const string DefaultTimeFormat = "HH:mm";

    /// <summary>Eingebaute Variablen (ohne $) mit Beschreibung.</summary>
    public static readonly IReadOnlyList<(string Name, string Description)> Builtins = new[]
    {
        ("Date", L.T("Today's date (format adjustable)")),
        ("Time", L.T("Current time (format adjustable)")),
        ("User", L.T("Signed-in Windows user")),
        ("Computer", L.T("Name of this computer")),
        ("Clipboard", L.T("Text on the clipboard at launch"))
    };

    private static readonly Regex NamePattern = new(@"^[\p{L}_][\p{L}\p{N}_]*$", RegexOptions.Compiled);

    /// <summary>Name ohne führendes $ und Leerzeichen.</summary>
    public static string Normalize(string? name) => (name ?? string.Empty).Trim().TrimStart('$').Trim();

    public static bool IsBuiltin(string name) => Builtins.Any(b => b.Name.Equals(Normalize(name), StringComparison.OrdinalIgnoreCase));

    /// <summary>Fehlermeldung für einen neuen bzw. geänderten Namen, sonst null.</summary>
    public static string? ValidateName(AppSettings s, string name, UserVariable? self = null)
    {
        var n = Normalize(name);
        if (n.Length == 0) return L.T("Please enter a name.");
        if (!NamePattern.IsMatch(n)) return L.T("Only letters, digits and _ – no spaces, not starting with a digit.");
        if (IsBuiltin(n)) return $"${n} ist eine eingebaute Variable.";
        if (s.Variables.Any(v => !ReferenceEquals(v, self) && v.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
            return $"${n} gibt es schon.";
        return null;
    }

    public static UserVariable? Find(AppSettings s, string name)
    {
        var n = Normalize(name);
        return s.Variables.FirstOrDefault(v => v.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Gibt es die Variable (eingebaut oder eigen)?</summary>
    public static bool Exists(AppSettings s, string name) => IsBuiltin(name) || Find(s, name) != null;

    /// <summary>Alle Variablen als Text zum Einfügen: {{$Date}} …, dann die eigenen.</summary>
    public static IReadOnlyList<string> Tokens(AppSettings s) =>
        Builtins.Select(b => "{{$" + b.Name + "}}").Concat(s.Variables.Select(v => "{{$" + v.Name + "}}")).ToList();

    /// <summary>Unbekannte {{$Variablen}} in den Texten (für die Warnung im Editor).</summary>
    public static IReadOnlyList<string> Unknown(AppSettings s, params string?[] texts) =>
        Placeholders.VariableNames(texts).Where(n => !Exists(s, n)).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>
    /// Wert für PowerShell-Code als sicheren String: Anführungszeichen (auch typografische) werden verdoppelt, damit der
    /// Wert in '…' nichts kaputt macht.
    /// </summary>
    public static string EscapeForPowerShell(string value) =>
        value.Replace("'", "''").Replace("‘", "‘‘").Replace("’", "’’")
             .Replace("‚", "‚‚").Replace("‛", "‛‛");

    public static string Label(VariableKind k) => k switch
    {
        VariableKind.Workspace => L.T("Per workspace"),
        VariableKind.Secret => L.T("Secret (encrypted)"),
        VariableKind.Environment => L.T("Environment variable"),
        VariableKind.PowerShell => L.T("PowerShell command"),
        _ => L.T("Fixed text")
    };

    public static string Label(VariableRefresh r) => r switch
    {
        VariableRefresh.EveryUse => L.T("At every launch"),
        VariableRefresh.Session => L.T("Once per session"),
        _ => L.T("Cache")
    };
}

/// <summary>
/// Löst {{$Variablen}} auf: eingebaute (Datum, Uhrzeit …) und eigene. PowerShell-Variablen werden je nach Einstellung
/// zwischengespeichert. Thread-sicher (Vorwärmen im Hintergrund).
/// </summary>
public sealed class VariableResolver
{
    private readonly Func<AppSettings> _settings;
    private readonly Func<string, string> _runPowerShell;
    private readonly Func<string, string> _unprotect;
    private readonly Func<string> _clipboard;
    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, (string Command, string Value, DateTime At)> _cache = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly object _lock = new();

    /// <param name="runPowerShell">Führt einen Befehl aus und liefert die Ausgabe (wirft bei Fehler).</param>
    /// <param name="unprotect">Entschlüsselt einen geheimen Wert.</param>
    public VariableResolver(Func<AppSettings> settings, Func<string, string> runPowerShell, Func<string, string> unprotect,
        Func<string> clipboard, Func<DateTime>? now = null)
    {
        _settings = settings;
        _runPowerShell = runPowerShell;
        _unprotect = unprotect;
        _clipboard = clipboard;
        _now = now ?? (() => DateTime.Now);
    }

    /// <summary>Wert der Variable oder null, wenn es sie nicht gibt. Fehler eines PowerShell-Befehls werden geworfen.</summary>
    public string? Resolve(string name)
    {
        var s = _settings();
        var n = Variables.Normalize(name);
        var now = _now();
        switch (n.ToLowerInvariant())
        {
            case "date": return Format(now, s.DateFormat, Variables.DefaultDateFormat);
            case "time": return Format(now, s.TimeFormat, Variables.DefaultTimeFormat);
            case "user": return Environment.UserName;
            case "computer": return Environment.MachineName;
            case "clipboard": return _clipboard();
        }
        if (Variables.Find(s, n) is not { } v) return null;
        return v.Kind switch
        {
            VariableKind.Workspace => WorkspaceValue(v, s.ActiveWorkspace) is { Length: > 0 } w ? w : v.Value,
            VariableKind.Secret => v.Value.Length == 0 ? string.Empty : _unprotect(v.Value),
            VariableKind.Environment => Environment.GetEnvironmentVariable(v.Value.Trim().Trim('%')) ?? string.Empty,
            VariableKind.PowerShell => RunCached(v),
            _ => v.Value
        };
    }

    /// <summary>Wert für die Anzeige (Einstellungen, Vorschau): Geheimes verdeckt, PowerShell nur aus dem Zwischenspeicher.</summary>
    public string Preview(string name)
    {
        var s = _settings();
        if (Variables.Find(s, name) is { } v)
        {
            if (v.Kind == VariableKind.Secret) return v.Value.Length == 0 ? L.T("(empty)") : "••••••••";
            if (v.Kind == VariableKind.PowerShell)
                lock (_lock)
                    return _cache.TryGetValue(v.Name, out var c) && c.Command == v.Value ? c.Value : L.T("(determined at launch)");
        }
        try { return Resolve(name) ?? L.T("(unknown)"); }
        catch (Exception ex) { return L.T("Error: ") + ex.Message; }
    }

    /// <summary>PowerShell-Variablen mit Zwischenspeicher bzw. „je Sitzung“ im Voraus ermitteln (im Hintergrund).</summary>
    public void Warm()
    {
        foreach (var v in _settings().Variables.Where(v => v.Kind == VariableKind.PowerShell && v.Refresh != VariableRefresh.EveryUse).ToList())
        {
            try { RunCached(v); } catch { /* Fehler zeigt der Start bzw. die Einstellungsseite */ }
        }
    }

    /// <summary>Zwischenspeicher leeren (z. B. „Jetzt neu ermitteln“).</summary>
    public void Invalidate(string? name = null)
    {
        lock (_lock)
        {
            if (name == null) _cache.Clear();
            else _cache.Remove(Variables.Normalize(name));
        }
    }

    private string RunCached(UserVariable v)
    {
        var now = _now();
        lock (_lock)
        {
            if (v.Refresh != VariableRefresh.EveryUse && _cache.TryGetValue(v.Name, out var c) && c.Command == v.Value &&
                (v.Refresh == VariableRefresh.Session || now - c.At < TimeSpan.FromMinutes(Math.Max(1, v.CacheMinutes))))
                return c.Value;
        }
        var value = _runPowerShell(v.Value).Trim();
        lock (_lock) _cache[v.Name] = (v.Value, value, now);
        return value;
    }

    /// <summary>Wert für einen Arbeitsbereich (Groß-/Kleinschreibung egal – auch nach dem Einlesen aus JSON).</summary>
    public static string? WorkspaceValue(UserVariable v, string? workspace) =>
        v.WorkspaceValues.FirstOrDefault(kv => kv.Key.Equals(workspace ?? string.Empty, StringComparison.OrdinalIgnoreCase)).Value;

    private static string Format(DateTime t, string? format, string fallback)
    {
        try { return t.ToString(string.IsNullOrWhiteSpace(format) ? fallback : format, CultureInfo.CurrentCulture); }
        catch (FormatException) { return t.ToString(fallback, CultureInfo.CurrentCulture); }
    }
}
