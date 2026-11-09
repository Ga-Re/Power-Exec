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
        new("name", "Name", Text, "z. B. SRV-"),
        new("operatingSystem", "Betriebssystem", Text, "z. B. Server"),
        new("operatingSystemVersion", "Betriebssystem-Version", Text, "z. B. 10.0 (20348)"),
        new("description", "Beschreibung", Text, "Text"),
        new("location", "Standort", Text, "z. B. Berlin"),
        new("dNSHostName", "DNS-Name", Text, "z. B. .firma.local"),
        new("managedBy", "Verwaltet von (DN)", new[] { AdOperator.Equals, AdOperator.NotEquals, AdOperator.Exists, AdOperator.NotExists }, "CN=…,OU=…,DC=…"),
        new(Enabled, "Konto aktiviert", new[] { AdOperator.Equals }, "Ja oder Nein"),
        new(LastLogonDays, "Letzte Anmeldung (Tage)", new[] { AdOperator.AtMost, AdOperator.AtLeast }, "Anzahl Tage, z. B. 30"),
        new(MemberOf, "Mitglied der AD-Gruppe (DN)", new[] { AdOperator.Equals, AdOperator.NotEquals }, "CN=Webserver,OU=Gruppen,DC=…"),
    };

    public static AdAttribute? Find(string key) => Attributes.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Operatoren eines Attributs (unbekannte/eigene Attribute: alle Textvergleiche).</summary>
    public static IReadOnlyList<AdOperator> OperatorsFor(string attribute) => Find(attribute)?.Operators ?? Text;

    public static string Label(AdOperator op, string attribute = "") => op switch
    {
        AdOperator.Equals => attribute == Enabled ? "ist" : "ist gleich",
        AdOperator.NotEquals => "ist nicht",
        AdOperator.Contains => "enthält",
        AdOperator.NotContains => "enthält nicht",
        AdOperator.StartsWith => "beginnt mit",
        AdOperator.EndsWith => "endet mit",
        AdOperator.Exists => "ist gesetzt",
        AdOperator.NotExists => "ist leer",
        AdOperator.AtMost => attribute == LastLogonDays ? "innerhalb von (Tage)" : "höchstens",
        AdOperator.AtLeast => attribute == LastLogonDays ? "länger her als (Tage)" : "mindestens",
        _ => op.ToString()
    };

    public static bool NeedsValue(AdOperator op) => op is not (AdOperator.Exists or AdOperator.NotExists);

    /// <summary>Fehler einer Regel oder null.</summary>
    public static string? Validate(AdRule r)
    {
        var attr = r.Attribute.Trim();
        if (attr.Length == 0) return "Attribut fehlt.";
        if (!attr.StartsWith('@') && !attr.All(c => char.IsLetterOrDigit(c) || c == '-')) return $"„{attr}“ ist kein gültiger Attributname.";
        if (!OperatorsFor(attr).Contains(r.Operator)) return $"„{Label(r.Operator, attr)}“ passt nicht zu „{Find(attr)?.Label ?? attr}“.";
        var v = r.Value.Trim();
        if (NeedsValue(r.Operator) && v.Length == 0) return "Wert fehlt.";
        if (attr == LastLogonDays && (!int.TryParse(v, out var d) || d < 1 || d > 3650)) return "Tage als Zahl zwischen 1 und 3650.";
        if (attr == Enabled && ParseYesNo(v) == null) return "„Ja“ oder „Nein“.";
        return null;
    }

    /// <summary>Erster Fehler der ganzen Abfrage oder null.</summary>
    public static string? Validate(AdGroupQuery q)
    {
        var rules = q.Blocks.SelectMany(b => b.Rules).ToList();
        if (rules.Count == 0) return "Mindestens eine Regel anlegen.";
        foreach (var (block, i) in q.Blocks.Select((b, i) => (b, i)))
        foreach (var r in block.Rules)
            if (Validate(r) is { } e) return (q.Blocks.Count > 1 ? $"Block {i + 1}: " : string.Empty) + e;
        var sb = q.SearchBase.Trim();
        if (sb.Length > 0 && !sb.Contains("DC=", StringComparison.OrdinalIgnoreCase) && !sb.Contains("OU=", StringComparison.OrdinalIgnoreCase))
            return "OU als Distinguished Name angeben, z. B. OU=Server,DC=firma,DC=local.";
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
            .Select(b => string.Join(" und ", b.Rules.Select(r =>
            {
                var name = Find(r.Attribute)?.Label ?? r.Attribute;
                return NeedsValue(r.Operator) ? $"{name} {Label(r.Operator, r.Attribute)} {r.Value.Trim()}" : $"{name} {Label(r.Operator, r.Attribute)}";
            })))
            .ToList();
        var text = blocks.Count == 0 ? "keine Regeln" : string.Join(" · oder ", blocks.Select(b => blocks.Count > 1 ? "(" + b + ")" : b));
        return q.SearchBase.Trim().Length > 0 ? $"{text} · ab {q.SearchBase.Trim()}" : text;
    }

    /// <summary>Ist eine dynamische Gruppe fällig (nie synchronisiert oder älter als das Intervall)?</summary>
    public static bool IsDue(MachineGroup g, int intervalHours, DateTime nowUtc) =>
        g.IsDynamic && (g.LastSyncUtc is not { } last || nowUtc - last >= TimeSpan.FromHours(Math.Max(1, intervalHours)));
}
