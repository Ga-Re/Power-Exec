using PowerExec.Core.Config;

namespace PowerExec.UI.ViewModels;

/// <summary>Was der Planen-Dialog (<see cref="ScheduleViewModel"/>) von der App braucht.</summary>
public interface IScheduleHost
{
    AppSettings Settings { get; }

    /// <summary>Neuen Zeitplan anlegen oder bestehenden (gleiche Id) ersetzen und speichern.</summary>
    void SaveSchedule(Schedule schedule);

    void RemoveSchedule(Schedule schedule);
}
