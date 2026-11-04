namespace PowerExec.Core.Config;

/// <summary>Wie oft und wann ein Eintrag gestartet wurde (für die Sortierung der Suche).</summary>
public sealed class RunStat
{
    /// <summary>Gewichtete Anzahl der Starts; verliert alle <see cref="RunStats.HalfLifeDays"/> Tage die Hälfte.</summary>
    public double Score { get; set; }
    public int Count { get; set; }
    public DateTime LastUtc { get; set; }
}

/// <summary>Häufigkeit und Aktualität („frecency“) je Eintrag.</summary>
public static class RunStats
{
    public const double HalfLifeDays = 14;
    public const int MaxStored = 500;

    private static double Decay(DateTime fromUtc, DateTime nowUtc)
    {
        var days = Math.Max(0, (nowUtc - fromUtc).TotalDays);
        return Math.Pow(0.5, days / HalfLifeDays);
    }

    public static void Record(AppSettings s, string path, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var key = s.RunStats.Keys.FirstOrDefault(k => k.Equals(path, StringComparison.OrdinalIgnoreCase)) ?? path;
        var stat = s.RunStats.GetValueOrDefault(key) ?? new RunStat();
        stat.Score = stat.Score * Decay(stat.LastUtc, now) + 1;
        stat.Count++;
        stat.LastUtc = now;
        s.RunStats[key] = stat;
        if (s.RunStats.Count > MaxStored)
            foreach (var old in s.RunStats.OrderBy(kv => kv.Value.Score * Decay(kv.Value.LastUtc, now)).Take(s.RunStats.Count - MaxStored).Select(kv => kv.Key).ToList())
                s.RunStats.Remove(old);
    }

    /// <summary>Aktueller Wert: 0 = nie oder vor langer Zeit gestartet.</summary>
    public static double Frecency(AppSettings s, string path, DateTime? nowUtc = null)
    {
        if (!s.RunStats.TryGetValue(path, out var stat))
        {
            var key = s.RunStats.Keys.FirstOrDefault(k => k.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (key == null) return 0;
            stat = s.RunStats[key];
        }
        return stat.Score * Decay(stat.LastUtc, nowUtc ?? DateTime.UtcNow);
    }

    public static void Rename(AppSettings s, string oldPath, string newPath)
    {
        var key = s.RunStats.Keys.FirstOrDefault(k => k.Equals(oldPath, StringComparison.OrdinalIgnoreCase));
        if (key == null) return;
        var stat = s.RunStats[key];
        s.RunStats.Remove(key);
        s.RunStats[newPath] = stat;
    }

    public static void Remove(AppSettings s, string path)
    {
        foreach (var k in s.RunStats.Keys.Where(k => k.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList()) s.RunStats.Remove(k);
    }
}
