using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI.Core.Tests;

public class RunViewModelTests
{
    private static LaunchItem Script(params ScriptParameter[] parameters) => new()
    {
        FullPath = @"C:\Scripts\Get-DiskSpace.ps1",
        RootFolder = @"C:\Scripts",
        Type = ItemType.PowerShell,
        Parameters = parameters
    };

    private static RunViewModel Create(FakeRunHost host, LaunchItem item, bool remote = false,
        ExecutionMode mode = ExecutionMode.CurrentUser) => new(host, item, mode, remote);

    [Fact]
    public void LocalRunShowsPreviewAndCanRun()
    {
        var vm = Create(new FakeRunHost(), Script());

        Assert.True(vm.IsLocal);
        Assert.True(vm.CanRun);
        Assert.Equal("Ausführen", vm.RunLabel);
        Assert.Contains("Get-DiskSpace.ps1", vm.Preview);
    }

    [Fact]
    public void RemoteRunNeedsMachinesAndCountsThemInLabel()
    {
        var vm = Create(new FakeRunHost(), Script(), remote: true);
        Assert.False(vm.CanRun);
        Assert.False(vm.RunCommand.CanExecute(null));
        Assert.Equal("Rechner auswählen", vm.RunLabel);
        Assert.False(vm.AddMachineCommand.CanExecute(null));

        vm.NewMachine = "srv1, srv2"; // eingegebene Namen werden großgeschrieben
        Assert.True(vm.AddMachineCommand.CanExecute(null));
        vm.AddMachineCommand.Execute(null);

        Assert.Equal(string.Empty, vm.NewMachine);
        Assert.Equal(new[] { "SRV2", "SRV1" }, vm.RecentMachines.Select(m => m.Name));
        Assert.True(vm.CanRun);
        Assert.Equal("Auf 2 Rechnern ausführen", vm.RunLabel);

        vm.RecentMachines.Single(m => m.Name == "SRV2").RemoveCommand.Execute(null);
        Assert.Equal("Auf SRV1 ausführen", vm.RunLabel);
    }

    [Fact]
    public void ChangingMachinesAsksCommandsToRequery()
    {
        var vm = Create(new FakeRunHost(), Script(), remote: true);
        var raised = 0;
        EventHandler handler = (_, _) => raised++;
        vm.RunCommand.CanExecuteChanged += handler;
        try
        {
            vm.NewMachine = "srv1";
            vm.AddMachineCommand.Execute(null);
            Assert.True(raised > 0);
        }
        finally
        {
            vm.RunCommand.CanExecuteChanged -= handler;
        }
    }

    [Fact]
    public void MachineGroupChipSelectsItsMembers()
    {
        var host = new FakeRunHost();
        host.Settings.MachineGroups.Add(new MachineGroup { Name = "Kunde A", Machines = { "a1", "a2" } });
        var vm = Create(host, Script(), remote: true);

        var chip = Assert.Single(vm.Groups);
        Assert.Equal("@Kunde A", chip.Name);
        Assert.Equal("2 Rechner", chip.CountText);
        chip.Command.Execute(null);

        Assert.Equal(new[] { "a1", "a2" }, vm.SelectedMachines.Select(m => m.Name).OrderBy(n => n));
    }

    [Fact]
    public void ToggleFavoriteMovesMachineAndSaves()
    {
        var host = new FakeRunHost();
        host.Settings.Machines.Add(new MachineEntry { Name = "srv1", LastUsedUtc = DateTime.UtcNow });
        var vm = Create(host, Script(), remote: true);
        var m = Assert.Single(vm.RecentMachines);

        m.ToggleFavoriteCommand.Execute(null);

        Assert.Empty(vm.RecentMachines);
        Assert.Same(m, Assert.Single(vm.FavoriteMachines));
        Assert.True(host.Settings.Machines.Single().Favorite);
        Assert.Equal(1, host.SaveCount);
    }

    [Fact]
    public void InvalidParameterBlocksStart()
    {
        var host = new FakeRunHost();
        var vm = Create(host, Script(new ScriptParameter { Name = "Count", TypeName = "int", IsMandatory = true }));
        vm.Parameters.Single().Value = "viele";
        var closed = false;
        vm.CloseRequested = () => closed = true;

        vm.RunCommand.Execute(null);

        Assert.NotEqual(string.Empty, vm.Validation);
        Assert.Empty(host.LocalStarts);
        Assert.False(closed);
    }

    [Fact]
    public void LocalStartIsTrackedRecordedAndClosesDialog()
    {
        var host = new FakeRunHost();
        var vm = Create(host, Script(new ScriptParameter { Name = "Drive", TypeName = "string" }));
        vm.Parameters.Single().Value = "C:";
        var closed = false;
        vm.CloseRequested = () => closed = true;

        vm.RunCommand.Execute(null);

        var req = Assert.Single(host.LocalStarts);
        Assert.Empty(req.Targets);
        Assert.Equal("C:", req.Parameters["Drive"]);
        Assert.Single(host.HistoryEntries);
        Assert.Equal("C:", Assert.Single(host.RecordedParameters)["Drive"]);
        Assert.Single(host.RecordedRuns);
        Assert.True(closed);
    }

    [Fact]
    public void DeclinedConfirmationStartsNothing()
    {
        var host = new FakeRunHost { ConfirmResult = false };
        var vm = Create(host, Script());
        var closed = false;
        vm.CloseRequested = () => closed = true;

        vm.RunCommand.Execute(null);

        Assert.Empty(host.LocalStarts);
        Assert.Empty(host.RecordedRuns);
        Assert.False(closed);
    }

    [Fact]
    public void RemoteStartMarksMachinesUsedButNotPureGroupMembers()
    {
        var host = new FakeRunHost();
        host.Settings.MachineGroups.Add(new MachineGroup { Name = "G", Machines = { "g1" } });
        var vm = Create(host, Script(), remote: true);
        vm.NewMachine = "srv1";
        vm.AddMachineCommand.Execute(null);
        vm.Groups.Single().Command.Execute(null);

        vm.RunCommand.Execute(null);

        var req = Assert.Single(host.RemoteStarts);
        Assert.Equal(new[] { "g1", "SRV1" }, req.Targets.OrderBy(t => t));
        Assert.Equal(new[] { "SRV1" }, host.Settings.Machines.Select(m => m.Name));
        Assert.True(host.SaveCount > 0);
        Assert.Single(host.RecordedRuns);
    }

    [Fact]
    public void FailedRemoteStartKeepsDialogOpen()
    {
        var host = new FakeRunHost { StartResult = false };
        var vm = Create(host, Script(), remote: true);
        vm.NewMachine = "srv1";
        vm.AddMachineCommand.Execute(null);
        var closed = false;
        vm.CloseRequested = () => closed = true;

        vm.RunCommand.Execute(null);

        Assert.Single(host.RemoteStarts);
        Assert.Empty(host.RecordedRuns);
        Assert.False(closed);
    }

    [Fact]
    public void ConnectionTestShowsResultPerMachine()
    {
        var host = new FakeRunHost();
        var vm = Create(host, Script(), remote: true);
        vm.NewMachine = "srv1, kaputt";
        vm.AddMachineCommand.Execute(null);

        Assert.True(vm.TestConnectionCommand.CanExecute(null));
        vm.TestConnectionCommand.Execute(null);

        Assert.Equal(2, Assert.Single(host.ConnectionTests).Count);
        Assert.True(vm.RecentMachines.Single(m => m.Name == "SRV1").TestOk);
        Assert.False(vm.RecentMachines.Single(m => m.Name == "KAPUTT").TestOk);
        Assert.False(vm.IsTesting);
        Assert.Equal(0, host.CredentialPrompts);
    }

    [Fact]
    public void ConnectionTestAsOtherUserIsCancelledWithoutCredentials()
    {
        var host = new FakeRunHost { Credential = null };
        var vm = Create(host, Script(), remote: true, mode: ExecutionMode.OtherUser);
        vm.NewMachine = "srv1";
        vm.AddMachineCommand.Execute(null);

        vm.TestConnectionCommand.Execute(null);

        Assert.Equal(1, host.CredentialPrompts);
        Assert.Empty(host.ConnectionTests);
        Assert.False(vm.RecentMachines.Single().HasTest);
    }

    [Fact]
    public void MissingPwshFallsBackAndWarns()
    {
        var host = new FakeRunHost { IsPwshInstalled = false };
        var vm = Create(host, Script());

        Assert.Equal(PsEngine.WindowsPowerShell, vm.Engine);
        Assert.Null(vm.EngineWarning);

        vm.Engine = PsEngine.PowerShell7;
        Assert.Equal("PowerShell 7 ist auf diesem PC nicht installiert.", vm.EngineWarning);
    }

    [Fact]
    public void SwitchingToRemoteUpdatesDependentTexts()
    {
        var vm = Create(new FakeRunHost(), Script());
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsRemote = true;

        Assert.False(vm.IsLocal);
        Assert.Contains(nameof(RunViewModel.IsLocal), changed);
        Assert.Contains(nameof(RunViewModel.ModeInfo), changed);
        Assert.Contains(nameof(RunViewModel.Preview), changed);
        Assert.Equal("Rechner auswählen", vm.RunLabel);
    }
}

public class CommandRequeryTests
{
    [Fact]
    public void ManualBackendCallsSubscribedHandlersUntilRemoved()
    {
        var backend = new ManualCommandRequery();
        var calls = 0;
        EventHandler h = (_, _) => calls++;

        backend.Add(h);
        backend.Invalidate();
        backend.Remove(h);
        backend.Invalidate();

        Assert.Equal(1, calls);
    }
}
