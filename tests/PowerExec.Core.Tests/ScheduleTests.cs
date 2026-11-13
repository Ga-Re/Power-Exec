using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Zeitpläne: nächster Lauf, Fälligkeit, Nachholen.</summary>
public class ScheduleTests
{
    [Fact]
    public void ScheduleNextRunAndDescription()
    {
        var wed = new DateTime(2026, 11, 4, 8, 0, 0); // Mittwoch
        var workdays = new Schedule
        {
            Kind = ScheduleKind.Daily,
            TimeOfDay = new TimeSpan(7, 0, 0),
            Days = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
        };
        Assert.Equal(new DateTime(2026, 11, 5, 7, 0, 0), Schedules.Next(workdays, wed)); // heute 07:00 vorbei → Donnerstag
        Assert.Equal(new DateTime(2026, 11, 9, 7, 0, 0), Schedules.Next(workdays, new DateTime(2026, 11, 7, 8, 0, 0))); // Samstag → Montag
        Assert.Equal("Mo–Fr 07:00", Schedules.Describe(workdays));

        var daily = new Schedule { Kind = ScheduleKind.Daily, TimeOfDay = new TimeSpan(9, 30, 0) };
        Assert.Equal(new DateTime(2026, 11, 4, 9, 30, 0), Schedules.Next(daily, wed));
        Assert.Equal("tägl. 09:30", Schedules.Describe(daily));

        var every = new Schedule { Kind = ScheduleKind.Interval, IntervalMinutes = 30, LastRunLocal = wed };
        Assert.Equal(wed.AddMinutes(30), Schedules.Next(every, wed));
        Assert.Equal("alle 30 min", Schedules.Describe(every));
        Assert.Equal("alle 2 Std.", Schedules.Describe(new Schedule { Kind = ScheduleKind.Interval, IntervalMinutes = 120 }));

        var once = new Schedule { Kind = ScheduleKind.Once, OnceAt = wed.AddHours(1) };
        Assert.Equal(wed.AddHours(1), Schedules.Next(once, wed));
        Assert.Null(Schedules.Next(once, wed.AddHours(2)));
    }

    [Fact]
    public void ScheduleDueGraceAndCatchUp()
    {
        var now = new DateTime(2026, 11, 4, 10, 0, 0);
        var s = new Schedule { Kind = ScheduleKind.Once, OnceAt = now.AddMinutes(-1), NextRunLocal = now.AddMinutes(-1) };
        Assert.True(Schedules.IsDue(s, now, TimeSpan.FromMinutes(2)));
        s.NextRunLocal = now.AddHours(-3);
        Assert.False(Schedules.IsDue(s, now, TimeSpan.FromMinutes(2))); // verpasst, nicht nachholen
        s.CatchUp = true;
        Assert.True(Schedules.IsDue(s, now, TimeSpan.FromMinutes(2)));
        Schedules.Advance(s, now, ran: true);
        Assert.False(s.Enabled); // einmalig: nach dem Lauf erledigt
        Assert.Equal(now, s.LastRunLocal);
        s.Enabled = true;
        Assert.False(Schedules.IsDue(s, now, TimeSpan.FromMinutes(2)));
    }
}
