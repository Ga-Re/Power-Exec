using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Infrastructure;
using PowerExec.ViewModels;

namespace PowerExec.UI.Core.Tests;

public class DisplayTextTests
{
    [Theory]
    [InlineData(4.2, "4,2 s")]
    [InlineData(187, "3:07 min")]
    [InlineData(3900, "1:05 h")]
    public void DurationIsShortAndGerman(double seconds, string expected) =>
        Assert.Equal(expected, DisplayText.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void HotkeyUsesGermanKeyNames() =>
        Assert.Equal("Strg+Alt+Leertaste", DisplayText.Hotkey("Ctrl+Alt+Space"));
}

public class HistoryRowViewModelTests
{
    private static HistoryEntry Entry() => new()
    {
        Name = "Reset", ProfileName = "Standard", StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow.AddSeconds(4.2),
        Result = HistoryResult.Success, Summary = "Exit 0",
        Parameters = new(StringComparer.OrdinalIgnoreCase) { ["User"] = "lena", ["Force"] = "true" }
    };

    [Fact]
    public void LocalRowShowsWhereMetaParametersAndDuration()
    {
        var row = new HistoryRowViewModel(new FakeHistoryHost(), Entry());

        Assert.Equal("lokal", row.Where);
        Assert.Equal("lokal · Exit 0", row.Meta);
        Assert.Equal("-User lena -Force", row.ParametersText);
        Assert.Equal("4,2 s", row.Duration);
        Assert.True(row.IsSuccess);
        Assert.False(row.CanRetryFailed);
        Assert.False(row.RetryFailedCommand.CanExecute(null));
        Assert.False(row.OpenLogCommand.CanExecute(null));
    }

    [Fact]
    public void RemoteRowCanRetryOnlyFailedMachines()
    {
        var host = new FakeHistoryHost();
        var e = Entry();
        e.Targets = new() { "A", "B", "C" };
        e.FailedTargets = new() { "B" };
        var row = new HistoryRowViewModel(host, e);

        Assert.Equal("3 Rechner", row.Where);
        Assert.Equal("Nur 1 fehlgeschlagene", row.RetryText);
        row.RetryFailedCommand.Execute(null);
        row.RunAgainCommand.Execute(null);

        Assert.Equal(new[] { true, false }, host.Replays.Select(r => r.OnlyFailed));
        Assert.All(host.Replays, r => Assert.Same(e, r.Entry));
    }

    [Fact]
    public void OpenLogShowsExistingFileInExplorer()
    {
        var host = new FakeHistoryHost();
        var log = Path.GetTempFileName();
        try
        {
            var e = Entry();
            e.LogFile = log;
            var row = new HistoryRowViewModel(host, e);

            Assert.True(row.OpenLogCommand.CanExecute(null));
            row.OpenLogCommand.Execute(null);
            Assert.Equal(new[] { log }, host.OpenedLocations);
        }
        finally
        {
            File.Delete(log);
        }
    }
}

public class ScheduleViewModelTests
{
    private static readonly LaunchItem Item = new() { FullPath = @"C:\s\Backup.ps1", RootFolder = @"C:\s", Type = ItemType.PowerShell };

    [Fact]
    public void NewScheduleDefaultsToWeekdaysAtSeven()
    {
        var vm = new ScheduleViewModel(new FakeScheduleHost(), Item);

        Assert.True(vm.IsDaily);
        Assert.Equal("07:00", vm.DailyTime);
        Assert.Equal(new[] { "Mo", "Di", "Mi", "Do", "Fr" }, vm.Days.Where(d => d.IsChecked).Select(d => d.Name));
        Assert.Null(vm.Error);
        Assert.Contains("nächster Start", vm.Preview);
        Assert.Equal("Planen", vm.SaveLabel);
        Assert.False(vm.HasExisting);
    }

    [Fact]
    public void InvalidInputShowsErrorAndBlocksSave()
    {
        var host = new FakeScheduleHost();
        var vm = new ScheduleViewModel(host, Item);

        vm.DailyTime = "25:00";
        Assert.NotNull(vm.Error);
        Assert.Equal(string.Empty, vm.Preview);

        vm.DailyTime = "06:30";
        foreach (var d in vm.Days) d.IsChecked = false;
        Assert.Equal("Mindestens einen Wochentag wählen.", vm.Error);

        vm.IsInterval = true;
        vm.Interval = "1";
        Assert.Contains("mindestens", vm.Error);

        vm.IsOnce = true;
        vm.OnceDate = DateTime.Now.AddDays(-1).ToString("dd.MM.yyyy");
        vm.OnceTime = "08:00";
        Assert.Equal("Der Zeitpunkt liegt in der Vergangenheit.", vm.Error);

        vm.SaveCommand.Execute(null);
        Assert.Empty(host.Saved);
    }

    [Fact]
    public void SaveStoresScheduleAndCloses()
    {
        var host = new FakeScheduleHost();
        var vm = new ScheduleViewModel(host, Item);
        var closed = false;
        vm.CloseRequested += () => closed = true;
        vm.DailyTime = "18:15";

        vm.SaveCommand.Execute(null);

        var s = Assert.Single(host.Saved);
        Assert.Equal(Item.FullPath, s.Path);
        Assert.Equal(new TimeSpan(18, 15, 0), s.TimeOfDay);
        Assert.Equal(5, s.Days.Count);
        Assert.True(closed);
    }

    [Fact]
    public void ExistingSchedulesCanBeEditedAndRemoved()
    {
        var host = new FakeScheduleHost();
        host.Settings.Schedules.Add(new Schedule { Path = Item.FullPath, Kind = ScheduleKind.Interval, IntervalMinutes = 30 });
        var vm = new ScheduleViewModel(host, Item);
        var row = Assert.Single(vm.Existing);

        row.EditCommand.Execute(null);
        Assert.True(vm.IsInterval);
        Assert.Equal("30", vm.Interval);
        Assert.Equal("Änderungen speichern", vm.SaveLabel);

        row.RemoveCommand.Execute(null);
        Assert.Empty(host.Settings.Schedules);
        Assert.False(vm.HasExisting);
    }
}

public class WorkflowEditorViewModelTests
{
    private static (FakeWorkflowHost host, CustomEntry wf) Setup()
    {
        var host = new FakeWorkflowHost();
        host.Items[@"C:\s\Stop.ps1"] = new LaunchItem { FullPath = @"C:\s\Stop.ps1", RootFolder = @"C:\s", Type = ItemType.PowerShell };
        host.Items[@"C:\s\Start.ps1"] = new LaunchItem { FullPath = @"C:\s\Start.ps1", RootFolder = @"C:\s", Type = ItemType.PowerShell };
        var wf = new CustomEntry
        {
            Name = "Neustart", Kind = CustomEntryKind.Workflow,
            Steps = new()
            {
                new WorkflowStep { Path = @"C:\s\Stop.ps1", Name = "Stop" },
                new WorkflowStep { Path = @"C:\s\Fehlt.ps1", Name = "Fehlt" },
                new WorkflowStep { Path = @"C:\s\Start.ps1", Name = "Start" }
            }
        };
        return (host, wf);
    }

    [Fact]
    public void StepsAreNumberedAndMissingOnesMarked()
    {
        var (host, wf) = Setup();
        var vm = new WorkflowEditorViewModel(host, wf);

        Assert.Equal(new[] { 1, 2, 3 }, vm.Steps.Select(s => s.Number));
        Assert.Equal(new[] { true, false, true }, vm.Steps.Select(s => s.Exists));
        Assert.NotEqual(string.Empty, vm.Steps[1].Status);
        Assert.Equal("Standard", vm.Steps[0].ProfileName);
    }

    [Fact]
    public void MoveAndRemoveReorderSteps()
    {
        var (host, wf) = Setup();
        var vm = new WorkflowEditorViewModel(host, wf);
        var start = vm.Steps[2];

        vm.MoveUpCommand.Execute(start);
        Assert.Equal(new[] { "Stop", "Start", "Fehlt" }, vm.Steps.Select(s => s.Name));
        Assert.Same(start, vm.Selected);
        Assert.Equal(2, start.Number);

        vm.MoveUpCommand.Execute(vm.Steps[0]); // schon oben: nichts passiert
        vm.RemoveCommand.Execute(vm.Steps[2]);
        Assert.Equal(new[] { "Stop", "Start" }, vm.Steps.Select(s => s.Name));
    }

    [Fact]
    public void SaveNeedsNameAndStepsAndReportsEditedCopy()
    {
        var (host, wf) = Setup();
        var vm = new WorkflowEditorViewModel(host, wf);
        var closed = false;
        vm.CloseRequested += () => closed = true;

        vm.Name = "  ";
        Assert.False(vm.CanSave);
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.Save();
        Assert.Empty(host.SavedWorkflows);

        vm.Name = " Neustart Dienst ";
        vm.StopOnError = false;
        vm.RemoveCommand.Execute(vm.Steps[1]);
        vm.SaveCommand.Execute(null);

        var (original, edited) = Assert.Single(host.SavedWorkflows);
        Assert.Same(wf, original);
        Assert.Equal(3, wf.Steps.Count); // Original bleibt unverändert
        Assert.Equal("Neustart Dienst", edited.Name);
        Assert.False(edited.StopOnError);
        Assert.Equal(new[] { @"C:\s\Stop.ps1", @"C:\s\Start.ps1" }, edited.Steps.Select(s => s.Path));
        Assert.All(edited.Steps, s => Assert.Equal(string.Empty, s.ProfileName));
        Assert.True(closed);
    }

    [Fact]
    public void RunSavesAndStartsWorkflow()
    {
        var (host, wf) = Setup();
        var vm = new WorkflowEditorViewModel(host, wf);

        vm.RunCommand.Execute(null);

        Assert.Single(host.SavedWorkflows);
        var run = Assert.Single(host.Runs);
        Assert.Equal("Neustart", run.Name);
        Assert.Equal(3, run.Steps.Count);
        Assert.True(run.StopOnError);
    }
}

public class WelcomeViewModelTests
{
    [Fact]
    public void StepsNavigateWithinBounds()
    {
        var vm = new WelcomeViewModel(new FakeWelcomeHost());

        Assert.True(vm.IsStep0);
        Assert.False(vm.BackCommand.CanExecute(null));
        for (int i = 0; i < 10; i++) vm.Step++;
        Assert.True(vm.IsLast);
        Assert.Equal("Fertig", vm.NextLabel);
        Assert.Equal("Schritt 4 von 4", vm.StepText);
        Assert.True(vm.BackCommand.CanExecute(null));
    }

    [Fact]
    public void AllSamplesAreShipped()
    {
        var names = typeof(WelcomeViewModel).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("PowerExec.Sample.")).ToList();
        Assert.Contains("PowerExec.Sample.Get-DiskSpace.ps1", names);
        Assert.Contains("PowerExec.Sample.Test-NetworkConnection.ps1", names);
        Assert.True(names.Count >= 8, $"nur {names.Count} Beispiele eingebettet");
    }

    [Fact]
    public void HotkeyDefaultsToCtrlAltSpaceAndIsAppliedOnFinish()
    {
        var host = new FakeWelcomeHost();
        var vm = new WelcomeViewModel(host);
        Assert.Equal("Ctrl+Alt+Space", vm.Hotkey);
        Assert.Equal("Strg+Alt+Leertaste", vm.HotkeyText);

        vm.Hotkey = "Ctrl+Shift+P";
        vm.Finish();
        Assert.Equal("Ctrl+Shift+P", Assert.Single(host.Applied).Panels[0].Hotkey);

        vm.Hotkey = string.Empty;
        Assert.Contains("keins", vm.HotkeyText);
        vm.ResetHotkeyCommand.Execute(null);
        Assert.Equal("Ctrl+Alt+Space", vm.Hotkey);
    }

    [Fact]
    public void SkipOnlyMarksWelcomeAsDone()
    {
        var host = new FakeWelcomeHost();
        var vm = new WelcomeViewModel(host);
        bool? tryPanel = null;
        vm.CloseRequested += t => tryPanel = t;
        vm.Edge = ScreenEdge.Left;

        vm.SkipCommand.Execute(null);

        var s = Assert.Single(host.Applied);
        Assert.True(s.WelcomeDone);
        Assert.NotSame(host.Settings, s);
        Assert.Empty(s.Folders);
        Assert.False(tryPanel);
    }

    [Fact]
    public void ChosenFolderEdgeAndOptionsAreAppliedOnFinish()
    {
        var host = new FakeWelcomeHost { FolderToPick = @"D:\Scripte" };
        host.Settings.Panels.Add(new PanelConfig());
        var vm = new WelcomeViewModel(host);
        Assert.True(vm.CreateSample); // noch keine Ordner: Beispiel vorgeschlagen
        bool? tryPanel = null;
        vm.CloseRequested += t => tryPanel = t;

        vm.ChooseFolderCommand.Execute(null);
        vm.SetEdgeCommand.Execute("Top");
        vm.StartWithWindows = true;
        vm.Finish();

        Assert.Equal(@"D:\Scripte", vm.Folder);
        Assert.False(vm.CreateSample);
        var s = Assert.Single(host.Applied);
        Assert.True(s.WelcomeDone);
        Assert.True(s.StartWithWindows);
        Assert.All(s.Panels, p => Assert.Equal(ScreenEdge.Top, p.Edge));
        Assert.Equal(@"D:\Scripte", Assert.Single(s.Folders).Path);
        Assert.True(tryPanel);
    }

    [Fact]
    public void CancelledFolderDialogKeepsSample()
    {
        var vm = new WelcomeViewModel(new FakeWelcomeHost { FolderToPick = null });

        vm.ChooseFolderCommand.Execute(null);

        Assert.Null(vm.Folder);
        Assert.True(vm.CreateSample);
    }

    [Fact]
    public void SampleScriptIsEmbeddedNextToViewModel()
    {
        using var stream = typeof(WelcomeViewModel).Assembly.GetManifestResourceStream("PowerExec.Sample.Get-DiskSpace.ps1");
        Assert.NotNull(stream);
        Assert.True(stream.Length > 0);
    }
}
