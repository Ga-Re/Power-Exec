namespace PowerExec.Core.Models;

/// <summary>Wie ein Parameter im Ausführen-Dialog dargestellt wird.</summary>
public enum ParameterKind
{
    Text,
    Number,
    Switch,
    Boolean,
    Choice,
    List
}

/// <summary>Ein Parameter aus dem param()-Block eines PowerShell-Scripts.</summary>
public sealed class ScriptParameter
{
    public required string Name { get; init; }

    /// <summary>Typname wie im Script angegeben, z. B. "int", "string[]", "switch". Leer = untypisiert.</summary>
    public string TypeName { get; init; } = string.Empty;

    public bool IsMandatory { get; init; }

    /// <summary>Werte aus [ValidateSet(...)].</summary>
    public IReadOnlyList<string> ValidValues { get; init; } = Array.Empty<string>();

    /// <summary>Standardwert als Quelltext, z. B. "15" oder "'Fixed'".</summary>
    public string? DefaultValueText { get; init; }

    /// <summary>Hilfetext aus .PARAMETER oder HelpMessage.</summary>
    public string HelpText { get; init; } = string.Empty;

    public ParameterKind Kind
    {
        get
        {
            var t = NormalizedType;
            if (t is "switch" or "switchparameter") return ParameterKind.Switch;
            if (ValidValues.Count > 0 && !t.EndsWith("[]")) return ParameterKind.Choice;
            if (t.EndsWith("[]")) return ParameterKind.List;
            if (t is "bool" or "boolean") return ParameterKind.Boolean;
            if (IsNumericType(t)) return ParameterKind.Number;
            return ParameterKind.Text;
        }
    }

    /// <summary>Kleingeschriebener Typname ohne Namespace, z. B. "int" oder "string[]".</summary>
    public string NormalizedType
    {
        get
        {
            var t = TypeName.Trim().ToLowerInvariant();
            if (t.StartsWith("system.management.automation.")) t = t["system.management.automation.".Length..];
            if (t.StartsWith("system.")) t = t["system.".Length..];
            return t;
        }
    }

    /// <summary>Standardwert ohne Anführungszeichen, wenn er ein einfaches Literal ist.</summary>
    public string? DefaultValueLiteral
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DefaultValueText)) return null;
            var v = DefaultValueText.Trim();
            if (v.Length >= 2 && ((v[0] == '\'' && v[^1] == '\'') || (v[0] == '"' && v[^1] == '"')))
            {
                var inner = v[1..^1];
                // Doppelte Anführungszeichen mit Variablen/Ausdrücken nicht als Literal behandeln.
                if (v[0] == '"' && (inner.Contains('$') || inner.Contains('`'))) return null;
                return v[0] == '\'' ? inner.Replace("''", "'") : inner;
            }
            if (v.Equals("$true", StringComparison.OrdinalIgnoreCase)) return "true";
            if (v.Equals("$false", StringComparison.OrdinalIgnoreCase)) return "false";
            if (v.StartsWith('$') || v.Contains('(') || v.Contains('@')) return null;
            return v;
        }
    }

    internal static bool IsNumericType(string t) => t is
        "int" or "int32" or "int64" or "long" or "int16" or "short" or "byte" or "sbyte" or
        "uint" or "uint32" or "uint64" or "ulong" or "uint16" or "ushort" or
        "double" or "float" or "single" or "decimal";

    internal static bool IsIntegerType(string t) => IsNumericType(t) && t is not ("double" or "float" or "single" or "decimal");
}
