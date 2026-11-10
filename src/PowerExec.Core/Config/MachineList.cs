using PowerExec.Core.Localization;
namespace PowerExec.Core.Config;

/// <summary>Verwaltet die Liste der zuletzt verwendeten und favorisierten Rechner.</summary>
public static class MachineList
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>Zerlegt eine Eingabe wie "PC1, PC2; PC3" in einzelne Rechnernamen.</summary>
    public static IReadOnlyList<string> ParseNames(string input) =>
        input.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Select(n => n.ToUpperInvariant())
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .ToList();

    /// <summary>Löst „@Gruppe“ in die Rechner der Gruppe auf; doppelte Namen werden entfernt.</summary>
    public static IReadOnlyList<string> Resolve(AppSettings settings, IEnumerable<string> names)
    {
        var result = new List<string>();
        foreach (var raw in names)
        {
            var n = raw.Trim();
            if (n.StartsWith('@'))
            {
                var g = settings.MachineGroups.FirstOrDefault(x => x.Name.Equals(n[1..].Trim(), StringComparison.CurrentCultureIgnoreCase));
                if (g != null) result.AddRange(g.Machines);
            }
            else if (n.Length > 0) result.Add(n.ToUpperInvariant());
        }
        return result.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Rechnernamen aus einer Text- oder CSV-Datei: erste Spalte (Trennzeichen ; , oder Tab), Kopfzeile wie
    /// „Name“/„Computer“/„ComputerName“/„DNSHostName“ wird übersprungen.
    /// </summary>
    public static IReadOnlyList<string> ParseImport(string text)
    {
        var headers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "computer", "computername", "dnshostname", "hostname", "rechner", "rechnername" };
        var names = new List<string>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var first = line.Split(new[] { ';', ',', '\t' }, StringSplitOptions.TrimEntries)[0].Trim('"', '\'', ' ');
            if (first.Length == 0 || headers.Contains(first)) continue;
            names.Add(first.ToUpperInvariant());
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void MarkUsed(AppSettings settings, IEnumerable<string> names, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        foreach (var raw in names)
        {
            var name = raw.Trim().ToUpperInvariant();
            if (name.Length == 0) continue;
            var entry = settings.Machines.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new MachineEntry { Name = name };
                settings.Machines.Add(entry);
            }
            entry.LastUsedUtc = now;
        }
        Prune(settings);
    }

    public static void SetFavorite(AppSettings settings, string name, bool favorite)
    {
        var entry = settings.Machines.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            entry = new MachineEntry { Name = name.ToUpperInvariant(), LastUsedUtc = DateTime.UtcNow };
            settings.Machines.Add(entry);
        }
        entry.Favorite = favorite;
        Prune(settings);
    }

    /// <summary>Favoriten bleiben immer erhalten; von den übrigen nur die neuesten MaxRecentMachines.</summary>
    public static void Prune(AppSettings settings)
    {
        var keep = settings.Machines.Where(m => m.Favorite)
            .Concat(settings.Machines.Where(m => !m.Favorite)
                .OrderByDescending(m => m.LastUsedUtc)
                .Take(settings.MaxRecentMachines))
            .ToList();
        settings.Machines = keep;
    }

    public static void ClearRecent(AppSettings settings) =>
        settings.Machines = settings.Machines.Where(m => m.Favorite).ToList();

    public static string RelativeTime(DateTime utc, DateTime? nowUtc = null)
    {
        var now = (nowUtc ?? DateTime.UtcNow).ToLocalTime();
        var t = utc.ToLocalTime();
        if (t.Date == now.Date) return "heute";
        if (t.Date == now.Date.AddDays(-1)) return "gestern";
        var days = (now.Date - t.Date).Days;
        if (days < 7) return L.F("{0} days ago", days);
        if (days < 14) return L.T("last week");
        return t.ToString("dd.MM.yyyy");
    }
}
