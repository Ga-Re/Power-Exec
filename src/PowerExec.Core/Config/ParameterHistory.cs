namespace PowerExec.Core.Config;

/// <summary>Zuletzt verwendete Werte je Script und Parameter (für Vorschläge im Ausführen-Dialog).</summary>
public static class ParameterHistory
{
    public const int MaxValues = 10;

    private static string Key(string path, string parameter) => path.ToLowerInvariant() + "|" + parameter.ToLowerInvariant();

    public static IReadOnlyList<string> Get(AppSettings s, string path, string parameter) =>
        s.ParameterHistory.TryGetValue(Key(path, parameter), out var list) ? list : Array.Empty<string>();

    public static void Add(AppSettings s, string path, string parameter, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var key = Key(path, parameter);
        if (!s.ParameterHistory.TryGetValue(key, out var list)) s.ParameterHistory[key] = list = new List<string>();
        list.RemoveAll(v => v.Equals(value, StringComparison.Ordinal));
        list.Insert(0, value.Trim());
        if (list.Count > MaxValues) list.RemoveRange(MaxValues, list.Count - MaxValues);
    }
}
