namespace PowerExec.Core.Config;

/// <summary>Pflegt die Liste „Zuletzt ausgeführt“.</summary>
public static class RecentRunList
{
    public const int MaxStored = 20;

    public static void Add(AppSettings settings, string path, string profileName, DateTime? nowUtc = null)
    {
        settings.RecentRuns.RemoveAll(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                                           r.ProfileName.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        settings.RecentRuns.Insert(0, new RecentRun { Path = path, ProfileName = profileName, TimeUtc = nowUtc ?? DateTime.UtcNow });
        if (settings.RecentRuns.Count > MaxStored) settings.RecentRuns.RemoveRange(MaxStored, settings.RecentRuns.Count - MaxStored);
    }

    public static string Ago(DateTime utc, DateTime? nowUtc = null)
    {
        var span = (nowUtc ?? DateTime.UtcNow) - utc;
        if (span.TotalMinutes < 1) return "gerade eben";
        if (span.TotalMinutes < 60) return $"vor {(int)span.TotalMinutes} Min.";
        if (span.TotalHours < 24) return $"vor {(int)span.TotalHours} Std.";
        if (span.TotalDays < 2) return "gestern";
        return $"vor {(int)span.TotalDays} Tagen";
    }
}
