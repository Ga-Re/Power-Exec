using System.Text.RegularExpressions;
using PowerExec.Core.Config;

namespace PowerExec.Core.Profiles;

/// <summary>
/// Variablen eines Profils einsetzen, bevor gestartet bzw. gefragt wird: <c>{{$Name}}</c> mit dem Profilwert (vor dem
/// globalen), feste Abfragen <c>{{Name}}</c> mit ihrem Wert; gefragte Abfragen bekommen Vorgabe bzw. Auswahlliste als
/// <c>{{Name:Vorgabe}}</c> bzw. <c>{{Name:A;B;C}}</c> – den Rest erledigt die normale Abfrage.
/// </summary>
public static class ProfileVariables
{
    private static readonly Regex Pattern = new(@"\{\{\s*([^{}:]+?)\s*(?::([^{}]*))?\}\}", RegexOptions.Compiled);

    /// <summary>Profilvariablen in einen Text einsetzen (<paramref name="urlEncode"/> wie bei der normalen Auflösung).</summary>
    public static string Apply(string? text, IReadOnlyDictionary<string, ProfileVariable> variables, bool urlEncode = false, bool powerShell = false)
    {
        if (string.IsNullOrEmpty(text) || variables.Count == 0) return text ?? string.Empty;
        var query = urlEncode ? text.IndexOf('?') : -1;
        return Pattern.Replace(text, m =>
        {
            var name = m.Groups[1].Value.Trim();
            if (!variables.TryGetValue(name, out var v)) return m.Value;
            bool encode = urlEncode && (!Placeholders.IsBuiltin(name) || (query >= 0 && m.Index > query));
            if (Placeholders.IsBuiltin(name) || v.Kind == ProfileVariableKind.Fixed)
            {
                var value = v.Value;
                if (powerShell && Placeholders.IsBuiltin(name)) value = Variables.EscapeForPowerShell(value);
                return encode ? Uri.EscapeDataString(value) : value;
            }
            // Gefragt: Vorgabe bzw. Auswahl im Platzhalter selbst (der erste Wert ist vorausgewählt)
            var choices = v.Kind == ProfileVariableKind.Choice
                ? new[] { v.Value }.Concat(v.Choices).Where(c => c.Trim().Length > 0).Select(c => c.Trim()).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList()
                : new List<string>();
            var preset = choices.Count > 0 ? string.Join(";", choices) : v.Value;
            return preset.Length == 0 ? m.Value : "{{" + name + ":" + preset + "}}";
        });
    }

    /// <summary>Eigener Eintrag mit Ziel, Code und Variablen des Profils.</summary>
    public static CustomEntry ApplyTo(CustomEntry entry, LaunchProfile profile)
    {
        if (profile.TargetText != null) entry.Target = profile.TargetText;
        if (profile.Code != null) entry.Code = profile.Code;
        var v = profile.Variables;
        if (v.Count == 0) return entry;
        entry.Target = Apply(entry.Target, v, urlEncode: entry.Kind == CustomEntryKind.Url);
        entry.Arguments = Apply(entry.Arguments, v);
        entry.Code = Apply(entry.Code, v, powerShell: entry.Kind == CustomEntryKind.Snippet && entry.Language == SnippetLanguage.PowerShell);
        entry.User = Apply(entry.User, v);
        entry.RemoteCommand = Apply(entry.RemoteCommand, v);
        entry.WorkingDirectory = Apply(entry.WorkingDirectory, v);
        return entry;
    }

    /// <summary>Script-Dateien: {{$Variablen}} des Profils in Parameterwerten und Argumenten einsetzen.</summary>
    public static LaunchProfile ApplyToValues(LaunchProfile profile)
    {
        if (!profile.Variables.Keys.Any(Placeholders.IsBuiltin)) return profile;
        var p = profile.Clone();
        var own = p.Variables.Where(kv => Placeholders.IsBuiltin(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var setting in p.Parameters.Values) setting.Value = Apply(setting.Value, own);
        p.Arguments = Apply(p.Arguments, own);
        return p;
    }
}
