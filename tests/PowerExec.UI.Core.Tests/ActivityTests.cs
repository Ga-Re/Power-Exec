using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using PowerExec.Core.Models;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI.Core.Tests;

/// <summary>UI-Thread zum Mitschreiben: Posts landen in einer Warteschlange, Timer-Ticks löst der Test aus.</summary>
internal sealed class FakeUiDispatcher : IUiDispatcher
{
    public ConcurrentQueue<Action> Posted { get; } = new();
    public List<FakeTimer> Timers { get; } = new();

    public void Post(Action action) => Posted.Enqueue(action);

    public IUiTimer CreateTimer(TimeSpan interval, Action tick)
    {
        var t = new FakeTimer(interval, tick);
        Timers.Add(t);
        return t;
    }

    /// <summary>Wartende Aktionen ausführen, bis <paramref name="until"/> gilt (höchstens 10 s).</summary>
    public void RunPosted(Func<bool> until)
    {
        var sw = Stopwatch.StartNew();
        while (!until() && sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (Posted.TryDequeue(out var a)) a();
            else Thread.Sleep(10);
        }
    }

    internal sealed class FakeTimer(TimeSpan interval, Action tick) : IUiTimer
    {
        public TimeSpan Interval { get; } = interval;
        public bool IsRunning { get; private set; }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Tick() => tick();
    }
}

internal sealed class FakeRemoteRun : IRemoteRunProgress
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Finished;

    public string Title => "Get-DiskSpace";
    public bool IsRunning { get; set; } = true;
    public int Total { get; set; } = 4;
    public int DoneCount { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public int UnreachableCount { get; set; }
    public double Progress => Total == 0 ? 0 : (double)DoneCount / Total;
    public string? RunnerError { get; set; }
    public int CancelCount { get; private set; }

    public void Cancel() => CancelCount++;

    public void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Finish()
    {
        IsRunning = false;
        Finished?.Invoke();
    }
}

public class ActivityViewModelTests
{
    [Fact]
    public void RunningActivityCanBeCancelledButNotRestarted()
    {
        var cancelled = 0;
        var a = new ActivityViewModel("Backup", "lokal", "PS", cancel: () => cancelled++) { RestartAction = () => { } };

        Assert.True(a.IsRunning);
        Assert.True(a.CanCancel);
        Assert.False(a.CanRestart);
        Assert.False(a.CanOpen);
        Assert.StartsWith("seit ", a.TimeText);
        Assert.True(a.CancelCommand.CanExecute(null));
        Assert.False(a.RestartCommand.CanExecute(null));
        a.CancelCommand.Execute(null);
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void CompleteIsFinalAndRaisedOnce()
    {
        var a = new ActivityViewModel("Backup", "lokal", "PS", cancel: () => { }) { RestartAction = () => { } };
        var completed = 0;
        a.Completed += () => completed++;

        a.Complete(ActivityState.Error, "Exit-Code 1");
        a.Complete(ActivityState.Success, "nochmal");

        Assert.Equal(1, completed);
        Assert.True(a.IsError);
        Assert.Equal("Exit-Code 1", a.Detail);
        Assert.NotNull(a.Finished);
        Assert.False(a.CanCancel);
        Assert.True(a.CanRestart);
        Assert.False(a.CancelCommand.CanExecute(null));
        Assert.True(a.RestartCommand.CanExecute(null));
        Assert.DoesNotContain("seit", a.TimeText);
    }

    [Fact]
    public void ProgressIsShownOnlyWhileRunning()
    {
        var a = new ActivityViewModel("Remote", "", "PS") { Progress = 0.25 };
        Assert.True(a.HasProgress);
        Assert.Equal(25, a.ProgressValue);

        a.Complete(ActivityState.Success);
        Assert.False(a.HasProgress);
    }
}

public class ActivityListTests
{
    [Fact]
    public void AddPutsNewestFirstAndStartsTimer()
    {
        var d = new FakeUiDispatcher();
        var list = new ActivityList(d);
        var changed = 0;
        list.Changed += () => changed++;

        var first = list.Add(new ActivityViewModel("A", "", "PS"));
        var second = list.Add(new ActivityViewModel("B", "", "PS"));

        Assert.Equal(new[] { second, first }, list.Items);
        Assert.Equal(2, list.RunningCount);
        Assert.Equal(2, changed);
        var timer = Assert.Single(d.Timers);
        Assert.True(timer.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(1), timer.Interval);
    }

    [Fact]
    public void FinishedActivitiesAreLimitedAndDismissable()
    {
        var list = new ActivityList(new FakeUiDispatcher());
        var running = list.Add(new ActivityViewModel("läuft", "", "PS"));
        Assert.False(running.DismissCommand!.CanExecute(null));

        var done = Enumerable.Range(0, 5).Select(i => list.Add(new ActivityViewModel($"fertig {i}", "", "PS"))).ToList();
        foreach (var a in done) a.Complete(ActivityState.Success);

        Assert.Contains(running, list.Items);
        Assert.Equal(ActivityList.MaxFinished, list.Items.Count(a => !a.IsRunning));
        Assert.Equal(new[] { "fertig 4", "fertig 3", "fertig 2" }, list.Items.Where(a => !a.IsRunning).Select(a => a.Title));

        var newest = done[4];
        Assert.True(newest.DismissCommand!.CanExecute(null));
        newest.DismissCommand.Execute(null);
        Assert.DoesNotContain(newest, list.Items);
    }

    [Fact]
    public void TimerStopsWhenNothingIsLeft()
    {
        var d = new FakeUiDispatcher();
        var list = new ActivityList(d);
        var a = list.Add(new ActivityViewModel("A", "", "PS"));
        a.Complete(ActivityState.Success);
        list.Remove(a);

        d.Timers[0].Tick();

        Assert.False(d.Timers[0].IsRunning);
        Assert.False(list.HasRunning);
    }

    [Fact]
    public void RemoteRunShowsProgressAndProblemsAsWarning()
    {
        var list = new ActivityList(new FakeUiDispatcher());
        var run = new FakeRemoteRun();
        var opened = 0;
        var a = list.TrackRemote(run, () => opened++);

        Assert.Equal("0 / 4 Rechner", a.Detail);
        Assert.Equal(0, a.Progress);

        run.DoneCount = 3; run.SuccessCount = 2; run.ErrorCount = 1;
        run.Raise(nameof(FakeRemoteRun.DoneCount));
        Assert.Equal(0.75, a.Progress);
        Assert.Equal("3 / 4 Rechner · 2 ✓ · 1 Fehler", a.Detail);

        a.CancelCommand.Execute(null);
        a.OpenCommand.Execute(null);
        Assert.Equal(1, run.CancelCount);
        Assert.Equal(1, opened);

        run.DoneCount = 4; run.UnreachableCount = 1;
        run.Finish();
        Assert.True(a.IsWarning);
        Assert.Equal("4 Rechner · 2 ✓ · 1 Fehler · 1 nicht erreichbar", a.Detail);
    }

    [Fact]
    public void RunnerErrorEndsRemoteRunAsError()
    {
        var list = new ActivityList(new FakeUiDispatcher());
        var run = new FakeRemoteRun();
        var a = list.TrackRemote(run, () => { });

        run.RunnerError = "PowerShell nicht gefunden";
        run.Finish();

        Assert.True(a.IsError);
        Assert.Equal("PowerShell nicht gefunden", a.Detail);
    }

    [Fact]
    public void TrackedProcessReportsExitCodeOnUiThread()
    {
        var d = new FakeUiDispatcher();
        var list = new ActivityList(d);
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c exit 3")
            : new ProcessStartInfo("/bin/sh", "-c \"exit 3\"");
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        var item = new LaunchItem { FullPath = @"C:\s\Fail.cmd", RootFolder = @"C:\s", Type = ItemType.Batch };
        ActivityViewModel? created = null;

        list.TrackProcess(item, "Fail", Process.Start(psi)!, "lokal", a => created = a);
        Assert.NotNull(created);
        Assert.Equal("CMD", created.Badge);
        Assert.Equal(item.FullPath, created.ItemPath);

        d.RunPosted(() => !created.IsRunning);

        Assert.True(created.IsError);
        Assert.Equal(3, created.ExitCode);
        Assert.Equal("beendet · Exit-Code 3", created.Detail);
    }

    [Theory]
    [InlineData(ItemType.PowerShell, "PS", true)]
    [InlineData(ItemType.Batch, "CMD", true)]
    [InlineData(ItemType.Executable, "EXE", false)]
    public void BadgeAndTrackingDependOnType(ItemType type, string badge, bool track)
    {
        var item = new LaunchItem { FullPath = @"C:\x", RootFolder = @"C:\", Type = type };
        Assert.Equal(badge, ActivityList.BadgeOf(item));
        Assert.Equal(track, ActivityList.ShouldTrack(item));
    }
}
