using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI.Core.Tests;

internal sealed class FakeHistoryHost : IHistoryHost
{
    public List<(HistoryEntry Entry, bool OnlyFailed)> Replays { get; } = new();
    public List<string> OpenedLocations { get; } = new();

    public bool Replay(HistoryEntry entry, bool onlyFailed = false)
    {
        Replays.Add((entry, onlyFailed));
        return true;
    }

    public void OpenLocation(string path) => OpenedLocations.Add(path);
}

internal sealed class FakeScheduleHost : IScheduleHost
{
    public AppSettings Settings { get; } = new();
    public List<Schedule> Saved { get; } = new();

    public void SaveSchedule(Schedule schedule)
    {
        Saved.Add(schedule);
        Settings.Schedules.RemoveAll(s => s.Id == schedule.Id);
        Settings.Schedules.Add(schedule);
    }

    public void RemoveSchedule(Schedule schedule) => Settings.Schedules.RemoveAll(s => s.Id == schedule.Id);
}

internal sealed class FakeWorkflowHost : IWorkflowHost
{
    public AppSettings Settings { get; } = new();
    public Dictionary<string, LaunchItem> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(CustomEntry Original, CustomEntry Edited)> SavedWorkflows { get; } = new();
    public List<(string Name, IReadOnlyList<WorkflowStep> Steps, bool StopOnError)> Runs { get; } = new();

    public LaunchItem? FindItem(string path) => Items.GetValueOrDefault(path);

    public Task<bool> RunWorkflowAsync(string name, IReadOnlyList<WorkflowStep> steps, bool stopOnError)
    {
        Runs.Add((name, steps, stopOnError));
        return Task.FromResult(true);
    }

    public void OnWorkflowSaved(CustomEntry original, CustomEntry edited) => SavedWorkflows.Add((original, edited));
}

internal sealed class FakeWelcomeHost : IWelcomeHost
{
    public AppSettings Settings { get; } = new();
    public string? FolderToPick { get; set; }
    public List<AppSettings> Applied { get; } = new();

    public string? ApplySettings(AppSettings updated)
    {
        Applied.Add(updated);
        return null;
    }

    public string? PickFolder(string title) => FolderToPick;
}
