using PowerExec.Core.Localization;
using System.Text;

namespace PowerExec.Core.Config;

/// <summary>Filter einer Listenquelle: [Spalte] [Vergleich] [Wert]; alle Filter müssen zutreffen.</summary>
public sealed class ListFilter
{
    public string Column { get; set; } = MachineTable.NameOnlyColumn;
    public AdOperator Operator { get; set; } = AdOperator.Contains;
    public string Value { get; set; } = string.Empty;

    public ListFilter Clone() => new() { Column = Column, Operator = Operator, Value = Value };
}

/// <summary>Rechnergruppe dynamisch aus einer Liste (Text mit einem Namen pro Zeile oder CSV mit Spalten).</summary>
public sealed class ListGroupQuery
{
    /// <summary>Pfad der Datei; wird bei jeder Synchronisierung neu gelesen.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>CSV: Spalte mit den Rechnernamen; leer = automatisch (Name, ComputerName …).</summary>
    public string NameColumn { get; set; } = string.Empty;

    public List<ListFilter> Filters { get; set; } = new();

    public ListGroupQuery Clone() => new() { Path = Path, NameColumn = NameColumn, Filters = Filters.Select(f => f.Clone()).ToList() };
}

/// <summary>Eingelesene Liste: Spalten und Zeilen. Eine reine Namensliste hat genau die Spalte „Name“.</summary>
public sealed class MachineTable
{
    /// <summary>Spalte einer reinen Namensliste (eine Zeile = ein Rechner).</summary>
    public const string NameOnlyColumn = "Name";

    private static readonly string[] NameColumns =
        { "Name", "ComputerName", "Computer", "Hostname", "HostName", "DNSHostName", "Rechner", "Rechnername", "Server", "Servername", "Gerät", "Device", "DeviceName" };

    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; }

    /// <summary>CSV mit Kopfzeile (Filter je Spalte); sonst eine Namensliste (nur Namensfilter).</summary>
    public bool IsCsv { get; }

    private MachineTable(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyDictionary<string, string>> rows, bool isCsv)
    {
        Columns = columns;
        Rows = rows;
        IsCsv = isCsv;
    }

    /// <summary>
    /// Text einlesen: Enthält die erste Zeile ein Trennzeichen (; , oder Tab), ist es eine CSV mit Kopfzeile; sonst eine
    /// Namensliste (eine Zeile = ein Rechner, Leerzeilen und #Kommentare werden übersprungen).
    /// </summary>
    public static MachineTable Parse(string text)
    {
        var lines = text.Replace("\r", string.Empty).Split('\n').Select(l => l.TrimEnd()).Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#')).ToList();
        if (lines.Count == 0) return new MachineTable(new[] { NameOnlyColumn }, Array.Empty<IReadOnlyDictionary<string, string>>(), false);
        var delimiter = DetectDelimiter(lines[0]);
        if (delimiter == null)
        {
            // Einspaltige CSV (z. B. eigener Export) hat eine Überschrift wie „Name“ – die ist kein Rechner
            if (NameColumns.Contains(lines[0].Trim().Trim('"'), StringComparer.OrdinalIgnoreCase)) lines.RemoveAt(0);
            var rows = lines.Select(l => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [NameOnlyColumn] = l.Trim().Trim('"') }).ToList();
            return new MachineTable(new[] { NameOnlyColumn }, rows, false);
        }
        var header = SplitCsv(lines[0], delimiter.Value).Select((h, i) => h.Length > 0 ? h : L.F("Column {0}", i + 1)).ToList();
        // Doppelte Spaltennamen eindeutig machen
        for (int i = 0; i < header.Count; i++)
            for (int n = 2; header.Take(i).Contains(header[i], StringComparer.OrdinalIgnoreCase); n++) header[i] = $"{header[i]} {n}";
        var data = new List<IReadOnlyDictionary<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            var cells = SplitCsv(line, delimiter.Value);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++) row[header[i]] = i < cells.Count ? cells[i] : string.Empty;
            data.Add(row);
        }
        return new MachineTable(header, data, true);
    }

    private static char? DetectDelimiter(string line)
    {
        foreach (var d in new[] { ';', '\t', ',' })
            if (SplitCsv(line, d).Count > 1) return d;
        return null;
    }

    /// <summary>Eine CSV-Zeile zerlegen (Anführungszeichen, verdoppelte "" im Feld).</summary>
    public static List<string> SplitCsv(string line, char delimiter)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == delimiter) { cells.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString().Trim());
        return cells;
    }

    /// <summary>Spalte mit den Rechnernamen: gewünschte, sonst eine typische (Name, ComputerName …), sonst die erste.</summary>
    public string NameColumnFor(string? wanted)
    {
        if (!string.IsNullOrWhiteSpace(wanted) && Columns.Contains(wanted.Trim(), StringComparer.OrdinalIgnoreCase))
            return Columns.First(c => c.Equals(wanted.Trim(), StringComparison.OrdinalIgnoreCase));
        foreach (var n in NameColumns)
            if (Columns.FirstOrDefault(c => c.Equals(n, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
        return Columns[0];
    }

    /// <summary>Rechnernamen der Zeilen, die alle Filter erfüllen (groß geschrieben, ohne Doppelte, sortiert).</summary>
    public IReadOnlyList<string> Apply(ListGroupQuery query)
    {
        var nameColumn = NameColumnFor(query.NameColumn);
        var filters = query.Filters.Where(f => f.Column.Trim().Length > 0 && (!AdRules.NeedsValue(f.Operator) || f.Value.Trim().Length > 0)).ToList();
        return Rows
            .Where(r => filters.All(f => Matches(r.TryGetValue(f.Column.Trim(), out var v) ? v : string.Empty, f.Operator, f.Value)))
            .Select(r => r.TryGetValue(nameColumn, out var n) ? n.Trim().Trim('"') : string.Empty)
            .Where(n => n.Length > 0)
            .Select(n => n.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Vergleich ohne Groß-/Kleinschreibung; „ist gleich“ versteht * und ? als Platzhalter.</summary>
    public static bool Matches(string cell, AdOperator op, string value)
    {
        cell = cell.Trim();
        value = value.Trim();
        var cmp = StringComparison.OrdinalIgnoreCase;
        return op switch
        {
            AdOperator.Equals => Wildcard(cell, value),
            AdOperator.NotEquals => !Wildcard(cell, value),
            AdOperator.Contains => cell.Contains(value, cmp),
            AdOperator.NotContains => !cell.Contains(value, cmp),
            AdOperator.StartsWith => cell.StartsWith(value, cmp),
            AdOperator.EndsWith => cell.EndsWith(value, cmp),
            AdOperator.Exists => cell.Length > 0,
            AdOperator.NotExists => cell.Length == 0,
            AdOperator.AtLeast => double.TryParse(cell, out var a) && double.TryParse(value, out var b) && a >= b,
            AdOperator.AtMost => double.TryParse(cell, out var c) && double.TryParse(value, out var d) && c <= d,
            _ => false
        };
    }

    private static bool Wildcard(string text, string pattern)
    {
        if (!pattern.Contains('*') && !pattern.Contains('?')) return text.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(text, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>Vergleiche für Listenfilter (Text und Zahlen).</summary>
    public static readonly IReadOnlyList<AdOperator> Operators =
        new[] { AdOperator.Contains, AdOperator.NotContains, AdOperator.Equals, AdOperator.NotEquals, AdOperator.StartsWith, AdOperator.EndsWith,
                AdOperator.Exists, AdOperator.NotExists, AdOperator.AtLeast, AdOperator.AtMost };
}

/// <summary>Rechnergruppe als Datei weitergeben: CSV (Spalte „Name“) oder Text (ein Name pro Zeile).</summary>
public static class MachineGroupExport
{
    public static string ToFileText(MachineGroup group, bool csv)
    {
        var sb = new StringBuilder();
        if (csv) sb.AppendLine("Name");
        foreach (var m in group.Machines) sb.AppendLine(csv && m.Contains(';') ? $"\"{m}\"" : m);
        return sb.ToString();
    }
}

/// <summary>Ein Knoten im OU-Baum des Active Directory.</summary>
public sealed class OuNode
{
    public required string Name { get; init; }
    public required string DistinguishedName { get; init; }
    public List<OuNode> Children { get; } = new();
}

/// <summary>Baut aus Distinguished Names (OUs, Container, Domäne) einen Baum für die Auswahl „Ab OU“.</summary>
public static class OuTree
{
    public static IReadOnlyList<OuNode> Build(IEnumerable<string> distinguishedNames)
    {
        var nodes = new Dictionary<string, OuNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in distinguishedNames)
        {
            var dn = raw.Trim();
            if (dn.Length == 0 || nodes.ContainsKey(dn)) continue;
            nodes[dn] = new OuNode { Name = DisplayName(dn), DistinguishedName = dn };
        }
        var roots = new List<OuNode>();
        foreach (var node in nodes.Values)
        {
            // nächster vorhandener Vorfahre (Zwischenebenen dürfen fehlen)
            OuNode? parent = null;
            for (var p = Parent(node.DistinguishedName); p != null && parent == null; p = Parent(p))
                nodes.TryGetValue(p, out parent);
            if (parent != null) parent.Children.Add(node);
            else roots.Add(node);
        }
        void Sort(List<OuNode> list)
        {
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            foreach (var n in list) Sort(n.Children);
        }
        Sort(roots);
        return roots;
    }

    /// <summary>Bestandteile eines DN, Kommas in Werten (\,) berücksichtigt.</summary>
    public static List<string> Split(string dn)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < dn.Length; i++)
        {
            if (dn[i] == '\\' && i + 1 < dn.Length) { sb.Append(dn[i]).Append(dn[i + 1]); i++; }
            else if (dn[i] == ',') { parts.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(dn[i]);
        }
        if (sb.Length > 0) parts.Add(sb.ToString().Trim());
        return parts;
    }

    public static string? Parent(string dn)
    {
        var parts = Split(dn);
        return parts.Count <= 1 ? null : string.Join(",", parts.Skip(1));
    }

    /// <summary>Anzeigename: „Server“ für OU=Server,…; die Domäne als firma.local.</summary>
    public static string DisplayName(string dn)
    {
        var parts = Split(dn);
        if (parts.Count > 0 && parts.All(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)))
            return string.Join(".", parts.Select(p => p[3..]));
        var first = parts.FirstOrDefault() ?? dn;
        var eq = first.IndexOf('=');
        return (eq >= 0 ? first[(eq + 1)..] : first).Replace("\\,", ",");
    }
}
