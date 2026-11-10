using PowerExec.Core.Localization;
namespace PowerExec.Core.Config;

public enum ScheduleKind { Once, Daily, Interval }

/// <summary>Geplanter Start eines Eintrags (D4).</summary>
public sealed class Schedule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Eintrag (Pfad bzw. „custom:Id“).</summary>
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Startprofil; leer = Standardprofil.</summary>
    public string ProfileName { get; set; } = string.Empty;
    public ScheduleKind Kind { get; set; } = ScheduleKind.Daily;
    /// <summary>Einmal: Zeitpunkt (lokal).</summary>
    public DateTime OnceAt { get; set; }
    /// <summary>Täglich: Uhrzeit (lokal).</summary>
    public TimeSpan TimeOfDay { get; set; } = new(7, 0, 0);
    /// <summary>Täglich: an diesen Wochentagen (leer = jeden Tag).</summary>
    public List<DayOfWeek> Days { get; set; } = new();
    /// <summary>Intervall in Minuten (mind. 5).</summary>
    public int IntervalMinutes { get; set; } = 60;
    public bool Enabled { get; set; } = true;
    /// <summary>Windows-Benachrichtigung nur bei Fehlern (sonst nach jedem Lauf).</summary>
    public bool NotifyOnlyOnError { get; set; } = true;
    /// <summary>War Power-Exec zur geplanten Zeit nicht aktiv: einmal nachholen.</summary>
    public bool CatchUp { get; set; }
    public DateTime? LastRunLocal { get; set; }
    public DateTime? NextRunLocal { get; set; }

    public Schedule Clone()
    {
        var c = (Schedule)MemberwiseClone();
        c.Days = new List<DayOfWeek>(Days);
        return c;
    }
}

/// <summary>Berechnung der nächsten Ausführung und Beschreibung von Zeitplänen.</summary>
public static class Schedules
{
    public const int MinIntervalMinutes = 5;

    /// <summary>Nächster Termin nach <paramref name="afterLocal"/> (exklusive); null = keiner mehr (einmalig und vorbei).</summary>
    public static DateTime? Next(Schedule s, DateTime afterLocal)
    {
        switch (s.Kind)
        {
            case ScheduleKind.Once:
                return s.OnceAt > afterLocal ? s.OnceAt : null;
            case ScheduleKind.Interval:
            {
                var minutes = Math.Max(MinIntervalMinutes, s.IntervalMinutes);
                var basis = s.LastRunLocal ?? afterLocal;
                var next = basis.AddMinutes(minutes);
                while (next <= afterLocal) next = next.AddMinutes(minutes);
                return next;
            }
            default:
            {
                for (int d = 0; d <= 7; d++)
                {
                    var day = afterLocal.Date.AddDays(d);
                    if (s.Days.Count > 0 && !s.Days.Contains(day.DayOfWeek)) continue;
                    var at = day + s.TimeOfDay;
                    if (at > afterLocal) return at;
                }
                return null;
            }
        }
    }

    /// <summary>Fällig? Ohne „nachholen“ nur, wenn der Termin höchstens <paramref name="grace"/> zurückliegt.</summary>
    public static bool IsDue(Schedule s, DateTime nowLocal, TimeSpan grace)
    {
        if (!s.Enabled || s.NextRunLocal is not { } next) return false;
        if (next > nowLocal) return false;
        return s.CatchUp || nowLocal - next <= grace;
    }

    /// <summary>Nach einem Lauf (oder einem verpassten Termin) den nächsten Termin setzen.</summary>
    public static void Advance(Schedule s, DateTime nowLocal, bool ran)
    {
        if (ran) s.LastRunLocal = nowLocal;
        s.NextRunLocal = Next(s, nowLocal);
        if (s.Kind == ScheduleKind.Once && s.NextRunLocal == null && ran) s.Enabled = false;
    }

    private static readonly string[] DayNames = { L.T("Sun"), L.T("Mon"), L.T("Tue"), L.T("Wed"), L.T("Thu"), L.T("Fri"), L.T("Sat") };

    /// <summary>Kurzform für das Panel, z. B. „tägl. 07:00“, „Mo–Fr 07:00“, „alle 30 min“, „einmal 05.11. 09:00“.</summary>
    public static string Describe(Schedule s) => s.Kind switch
    {
        ScheduleKind.Once => L.F("once {0:g}", s.OnceAt),
        ScheduleKind.Interval => s.IntervalMinutes % 60 == 0 && s.IntervalMinutes >= 60
            ? L.F("every {0} h", s.IntervalMinutes / 60)
            : L.F("every {0} min", Math.Max(MinIntervalMinutes, s.IntervalMinutes)),
        _ => $"{DaysText(s.Days)} {s.TimeOfDay:hh\\:mm}"
    };

    public static string DaysText(IReadOnlyCollection<DayOfWeek> days)
    {
        if (days.Count == 0 || days.Count == 7) return L.T("daily");
        var workdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        if (days.Count == 5 && workdays.All(days.Contains)) return L.T("Mon–Fri");
        return string.Join(", ", days.OrderBy(d => ((int)d + 6) % 7).Select(d => DayNames[(int)d]));
    }

    public static IReadOnlyList<Schedule> For(AppSettings s, string path) =>
        s.Schedules.Where(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList();
}
