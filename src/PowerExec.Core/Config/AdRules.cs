using PowerExec.Core.Localization;
using System.Globalization;
using System.Text;

namespace PowerExec.Core.Config;

/// <summary>Vergleich einer Regel für dynamische Rechnergruppen.</summary>
public enum AdOperator { Equals, NotEquals, Contains, NotContains, StartsWith, EndsWith, Exists, NotExists, AtLeast, AtMost }

/// <summary>Eine Regel: [Attribut] [Operator] [Wert], z. B. operatingSystem enthält „Server“.</summary>
public sealed class AdRule
{
    public string Attribute { get; set; } = "name";
    public AdOperator Operator { get; set; } = AdOperator.StartsWith;
    public string Value { get; set; } = string.Empty;

    public AdRule Clone() => new() { Attribute = Attribute, Operator = Operator, Value = Value };
}

/// <summary>Regelblock: alle Regeln müssen zutreffen (UND). Mehrere Blöcke werden mit ODER verknüpft.</summary>
public sealed class AdRuleBlock
{
    public List<AdRule> Rules { get; set; } = new();

    public AdRuleBlock Clone() => new() { Rules = Rules.Select(r => r.Clone()).ToList() };
}

/// <summary>Abfrage einer dynamischen Rechnergruppe: optional ab einer OU, Blöcke mit ODER verknüpft.</summary>
public sealed class AdGroupQuery
{
    /// <summary>Leer = ganze Domäne; sonst z. B. „OU=Server,DC=firma,DC=local“.</summary>
    public string SearchBase { get; set; } = string.Empty;
    public List<AdRuleBlock> Blocks { get; set; } = new();

    public AdGroupQuery Clone() => new() { SearchBase = SearchBase, Blocks = Blocks.Select(b => b.Clone()).ToList() };
}

/// <summary>Ein wählbares Attribut in der Oberfläche (eigene LDAP-Attribute gehen auch).</summary>
public sealed record AdAttribute(string Key, string Label, AdOperator[] Operators, string ValueHint);

/// <summary>Baut aus Regelblöcken einen LDAP-Filter für Computerkonten und prüft die Eingaben.</summary>
public static class AdRules
{
    /// <summary>Pseudo-Attribute: werden in passende LDAP-Ausdrücke übersetzt.</summary>
    public const string Enabled = "@aktiv", LastLogonDays = "@anmeldungTage", MemberOf = "@mitglied";

    private static readonly AdOperator[] Text =
        { AdOperator.Equals, AdOperator.NotEquals, AdOperator.Contains, AdOperator.NotContains, AdOperator.StartsWith, AdOperator.EndsWith, AdOperator.Exists, AdOperator.NotExists };

    public static readonly IReadOnlyList<AdAttribute> Attributes = new AdAttribute[]
    {
        new("name", L.T("Name"), Text, L.T("e.g. SRV-")),
        new("operatingSystem", L.T("Operating system"), Text, L.T("e.g. Server")),
        new("operatingSystemVersion", L.T("Operating system version"), Text, "z. B. 10.0 (20348)"),
        new("description", L.T("Description"), Text, "Text"),
        new("location", L.T("Location"), Text, L.T("e.g. Berlin")),
        new("dNSHostName", L.T("DNS name"), Text, L.T("e.g. .company.local")),
        new("managedBy", L.T("Managed by (DN)"), new[] { AdOperator.Equals, AdOperator.NotEquals, AdOperator.Exists, AdOperator.NotExists }, "CN=…,OU=…,DC=…"),
        new(Enabled, L.T("Account enabled"), new[] { AdOperator.Equals }, L.T("Yes or No")),
        new(LastLogonDays, L.T("Last sign-in (days)"), new[] { AdOperator.AtMost, AdOperator.AtLeast }, L.T("Number of days, e.g. 30")),
        new(MemberOf, L.T("Member of AD group (DN)"), new[] { AdOperator.Equals, AdOperator.NotEquals }, L.T("CN=Webservers,OU=Groups,DC=…")),
    };

    public static AdAttribute? Find(string key) => Attributes.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Operatoren eines Attributs (unbekannte/eigene Attribute: alle Textvergleiche).</summary>
    public static IReadOnlyList<AdOperator> OperatorsFor(string attribute) => Find(attribute)?.Operators ?? Text;

    public static string Label(AdOperator op, string attribute = "") => op switch
    {
        AdOperator.Equals => attribute == Enabled ? "ist" : L.T("equals"),
        AdOperator.NotEquals => L.T("is not"),
        AdOperator.Contains => L.T("contains"),
        AdOperator.NotContains => L.T("does not contain"),
        AdOperator.StartsWith => L.T("starts with"),
        AdOperator.EndsWith => L.T("ends with"),
        AdOperator.Exists => L.T("is set"),
        AdOperator.NotExists => L.T("is empty"),
        AdOperator.AtMost => attribute == LastLogonDays ? L.T("within (days)") : L.T("at most"),
        AdOperator.AtLeast => attribute == LastLogonDays ? L.T("longer ago than (days)") : "mindestens",
        _ => op.ToString()
    };

    public static bool NeedsValue(AdOperator op) => op is not (AdOperator.Exists or AdOperator.NotExists);

    /// <summary>Fehler einer Regel oder null.</summary>
    public static string? Validate(AdRule r)
    {
        var attr = r.Attribute.Trim();
        if (attr.Length == 0) return L.T("Attribute missing.");
        if (!attr.StartsWith('@') && !attr.All(c => char.IsLetterOrDigit(c) || c == '-')) return L.F("“{0}” is not a valid attribute name.", attr);
        if (!OperatorsFor(attr).Contains(r.Operator)) return L.F("“{0}” doesn't fit “{1}”.", Label(r.Operator, attr), Find(attr)?.Label ?? attr);
        var v = r.Value.Trim();
        if (NeedsValue(r.Operator) && v.Length == 0) return L.T("Value missing.");
        if (attr == LastLogonDays && (!int.TryParse(v, out var d) || d < 1 || d > 3650)) return L.T("Days as a number between 1 and 3650.");
        if (attr == Enabled && ParseYesNo(v) == null) return L.T("“Yes” or “No”.");
        return null;
    }

    /// <summary>Erster Fehler der ganzen Abfrage oder null.</summary>
    public static string? Validate(AdGroupQuery q)
    {
        var rules = q.Blocks.SelectMany(b => b.Rules).ToList();
        if (rules.Count == 0) return L.T("Create at least one rule.");
        foreach (var (block, i) in q.Blocks.Select((b, i) => (b, i)))
            foreach (var r in block.Rules)
                if (Validate(r) is { } e) return (q.Blocks.Count > 1 ? L.F("Block {0}: ", i + 1) : string.Empty) + e;
        var sb = q.SearchBase.Trim();
        if (sb.Length > 0 && !sb.Contains("DC=", StringComparison.OrdinalIgnoreCase) && !sb.Contains("OU=", StringComparison.OrdinalIgnoreCase))
            return L.T("Enter the OU as a distinguished name, e.g. OU=Server,DC=company,DC=local.");
        return null;
    }

    /// <summary>LDAP-Filter: (&amp;(objectCategory=computer)(|(&amp;Regeln Block 1)(&amp;Regeln Block 2)…)).</summary>
    public static string BuildFilter(AdGroupQuery q, DateTime nowUtc)
    {
        var blocks = q.Blocks.Where(b => b.Rules.Count > 0).Select(b => And(b.Rules.Select(r => Term(r, nowUtc)))).ToList();
        var body = blocks.Count switch
        {
            0 => string.Empty,
            1 => blocks[0],
            _ => "(|" + string.Concat(blocks) + ")"
        };
        return "(&(objectCategory=computer)" + body + ")";
    }

    private static string And(IEnumerable<string> terms)
    {
        var list = terms.ToList();
        return list.Count == 1 ? list[0] : "(&" + string.Concat(list) + ")";
    }

    private static string Term(AdRule r, DateTime nowUtc)
    {
        var a = r.Attribute.Trim();
        var v = r.Value.Trim();
        switch (a)
        {
            case Enabled:
                // Bit 2 (ACCOUNTDISABLE) in userAccountControl
                const string disabled = "(userAccountControl:1.2.840.113556.1.4.803:=2)";
                return ParseYesNo(v) == false ? disabled : "(!" + disabled + ")";
            case LastLogonDays:
                {
                    var days = int.TryParse(v, out var d) ? d : 30;
                    var ft = nowUtc.AddDays(-days).ToFileTimeUtc().ToString(CultureInfo.InvariantCulture);
                    // innerhalb von N Tagen: Zeitstempel neuer als die Grenze; länger her: älter oder nie angemeldet
                    return r.Operator == AdOperator.AtLeast
                        ? $"(|(lastLogonTimestamp<={ft})(!(lastLogonTimestamp=*)))"
                        : $"(lastLogonTimestamp>={ft})";
                }
            case MemberOf:
                {
                    // verschachtelte Mitgliedschaft (LDAP_MATCHING_RULE_IN_CHAIN)
                    var t = $"(memberOf:1.2.840.113556.1.4.1941:={Escape(v)})";
                    return r.Operator == AdOperator.NotEquals ? "(!" + t + ")" : t;
                }
        }
        var e = Escape(v);
        return r.Operator switch
        {
            AdOperator.Equals => $"({a}={e})",
            AdOperator.NotEquals => $"(!({a}={e}))",
            AdOperator.Contains => $"({a}=*{e}*)",
            AdOperator.NotContains => $"(!({a}=*{e}*))",
            AdOperator.StartsWith => $"({a}={e}*)",
            AdOperator.EndsWith => $"({a}=*{e})",
            AdOperator.Exists => $"({a}=*)",
            AdOperator.NotExists => $"(!({a}=*))",
            AdOperator.AtLeast => $"({a}>={e})",
            AdOperator.AtMost => $"({a}<={e})",
            _ => $"({a}={e})"
        };
    }

    public static bool? ParseYesNo(string v) => v.Trim().ToLowerInvariant() switch
    {
        "ja" or "j" or "yes" or "true" or "1" or "aktiv" or "aktiviert" => true,
        "nein" or "n" or "no" or "false" or "0" or "deaktiviert" => false,
        _ => null
    };

    /// <summary>LDAP-Sonderzeichen im Wert maskieren (RFC 4515) – auch *, damit der Wert wörtlich gilt.</summary>
    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '\\' => "\\5c",
                '*' => "\\2a",
                '(' => "\\28",
                ')' => "\\29",
                '\0' => "\\00",
                _ => c.ToString()
            });
        }
        return sb.ToString();
    }

    /// <summary>Lesbare Zusammenfassung, z. B. „Name beginnt mit SRV- und Konto aktiviert ist Ja · oder …“.</summary>
    public static string Describe(AdGroupQuery q)
    {
        var blocks = q.Blocks.Where(b => b.Rules.Count > 0)
            .Select(b => string.Join(L.T(" and "), b.Rules.Select(r =>
            {
                var name = Find(r.Attribute)?.Label ?? r.Attribute;
                return NeedsValue(r.Operator) ? $"{name} {Label(r.Operator, r.Attribute)} {r.Value.Trim()}" : $"{name} {Label(r.Operator, r.Attribute)}";
            })))
            .ToList();
        var text = blocks.Count == 0 ? L.T("no rules") : string.Join(L.T(" · or "), blocks.Select(b => blocks.Count > 1 ? "(" + b + ")" : b));
        return q.SearchBase.Trim().Length > 0 ? L.F("{0} · from {1}", text, q.SearchBase.Trim()) : text;
    }

    /// <summary>Ist eine dynamische Gruppe fällig (nie synchronisiert oder älter als das Intervall)?</summary>
    public static bool IsDue(MachineGroup g, int intervalHours, DateTime nowUtc) =>
        g.IsDynamic && (g.LastSyncUtc is not { } last || nowUtc - last >= TimeSpan.FromHours(Math.Max(1, intervalHours)));
}
