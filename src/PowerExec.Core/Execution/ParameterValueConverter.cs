using PowerExec.Core.Localization;
using System.Globalization;
using System.Text;
using PowerExec.Core.Models;

namespace PowerExec.Core.Execution;

/// <summary>Wandelt Eingaben aus dem Dialog in typisierte Werte und PowerShell-Literale um.</summary>
public static class ParameterValueConverter
{
    /// <summary>
    /// Wandelt die Formularwerte in typisierte Werte um. Leere optionale Werte werden weggelassen,
    /// ausgeschaltete Switches ebenso.
    /// </summary>
    /// <returns>Liste von Fehlermeldungen (leer = alles gültig).</returns>
    public static IReadOnlyList<string> TryConvert(
        IReadOnlyList<ScriptParameter> parameters,
        IReadOnlyDictionary<string, string?> input,
        out Dictionary<string, object> values)
    {
        values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var p in parameters)
        {
            input.TryGetValue(p.Name, out var raw);
            raw = raw?.Trim();

            if (p.Kind == ParameterKind.Secure)
                continue; // nie übergeben – das Script fragt bei Bedarf selbst

            if (p.Kind == ParameterKind.Switch)
            {
                if (IsTrueText(raw)) values[p.Name] = true;
                continue;
            }

            if (string.IsNullOrEmpty(raw))
            {
                if (p.IsMandatory) errors.Add(L.F("{0} is a required field.", p.Name));
                continue;
            }

            switch (p.Kind)
            {
                case ParameterKind.Boolean:
                    values[p.Name] = IsTrueText(raw);
                    break;
                case ParameterKind.Number:
                    if (TryParseNumber(raw, p.NormalizedType, out var num)) values[p.Name] = num;
                    else errors.Add(L.F("{0}: \"{1}\" is not a valid number.", p.Name, raw));
                    break;
                case ParameterKind.List:
                    var items = raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var elementType = p.NormalizedType.TrimEnd('[', ']');
                    if (ScriptParameter.IsNumericType(elementType))
                    {
                        var nums = new List<object>();
                        foreach (var it in items)
                        {
                            if (TryParseNumber(it, elementType, out var n)) nums.Add(n);
                            else errors.Add(L.F("{0}: \"{1}\" is not a valid number.", p.Name, it));
                        }
                        values[p.Name] = nums.ToArray();
                    }
                    else
                    {
                        values[p.Name] = items;
                    }
                    break;
                case ParameterKind.Choice:
                    if (p.ValidValues.Count > 0 && !p.ValidValues.Contains(raw, StringComparer.OrdinalIgnoreCase))
                        errors.Add(L.F("{0}: \"{1}\" is not an allowed value.", p.Name, raw));
                    else values[p.Name] = raw;
                    break;
                default:
                    values[p.Name] = raw;
                    break;
            }
        }
        return errors;
    }

    /// <summary>Baut die Parameter als PowerShell-Quelltext: <c>-Name 'Wert' -Switch</c>.</summary>
    public static string ToPowerShellArguments(IReadOnlyDictionary<string, object> values)
    {
        var sb = new StringBuilder();
        foreach (var (name, value) in values)
        {
            sb.Append(" -").Append(name);
            if (value is bool b)
            {
                // Switches und bool-Parameter: -Name:$true / -Name:$false
                sb.Append(b ? ":$true" : ":$false");
                continue;
            }
            sb.Append(' ').Append(ToLiteral(value));
        }
        return sb.ToString();
    }

    public static string ToLiteral(object value) => value switch
    {
        bool b => b ? "$true" : "$false",
        string s => Quote(s),
        string[] arr => arr.Length == 0 ? "@()" : string.Join(",", arr.Select(Quote)),
        object[] arr => arr.Length == 0 ? "@()" : string.Join(",", arr.Select(ToLiteral)),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Quote(value.ToString() ?? string.Empty)
    };

    /// <summary>Single-Quote-Literal: nur ' muss verdoppelt werden (auch typografische Varianten).</summary>
    public static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('\'');
        foreach (var c in s)
        {
            sb.Append(c);
            if (c is '\'' or '‘' or '’' or '‚' or '‛') sb.Append(c);
        }
        return sb.Append('\'').ToString();
    }

    private static bool IsTrueText(string? s) =>
        s != null && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" ||
                      s.Equals("$true", StringComparison.OrdinalIgnoreCase) ||
                      s.Equals("ja", StringComparison.OrdinalIgnoreCase));

    private static bool TryParseNumber(string raw, string type, out object value)
    {
        var s = raw.Trim();
        if (ScriptParameter.IsIntegerType(type))
        {
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                value = l is >= int.MinValue and <= int.MaxValue ? (object)(int)l : l;
                return true;
            }
        }
        else
        {
            // Komma oder Punkt als Dezimaltrenner akzeptieren.
            if (double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                value = d;
                return true;
            }
        }
        value = 0;
        return false;
    }
}
