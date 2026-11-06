using System.Collections.ObjectModel;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Infrastructure;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec.UI.Core.Tests;

internal sealed class FakeResultsHost : IResultsHost
{
    public AppSettings Settings { get; } = new();
    public FakeShell FakeShell { get; } = new();
    public FakeDialogs FakeDialogs { get; } = new();
    public IShell Shell => FakeShell;
    public IDialogs Dialogs => FakeDialogs;
    public IUiDispatcher UiDispatcher { get; } = new FakeUiDispatcher();
    public List<ExecutionRequest> Retries { get; } = new();

    public bool RetryRemote(ExecutionRequest request)
    {
        Retries.Add(request);
        return true;
    }
}

public class FilteredViewTests
{
    private sealed class Row(string name, int order)
    {
        public string Name { get; } = name;
        public int Order { get; set; } = order;
    }

    [Fact]
    public void FiltersSortsAndFollowsSource()
    {
        var source = new ObservableCollection<Row> { new("a", 3), new("b", 1), new("c", 2) };
        var minOrder = 2;
        var view = new FilteredView<Row>(source, r => r.Order >= minOrder, rows => rows.OrderBy(r => r.Order));

        Assert.Equal(new[] { "c", "a" }, view.Items.Select(r => r.Name));

        source.Add(new Row("d", 5));
        Assert.Equal(new[] { "c", "a", "d" }, view.Items.Select(r => r.Name));

        minOrder = 0;
        view.Refresh();
        Assert.Equal(new[] { "b", "c", "a", "d" }, view.Items.Select(r => r.Name));
    }

    [Fact]
    public void RefreshKeepsItemIdentity()
    {
        var source = new ObservableCollection<Row> { new("a", 2), new("b", 1) };
        var view = new FilteredView<Row>(source, _ => true, rows => rows.OrderBy(r => r.Order));
        var a = source[0];

        a.Order = 0;
        view.Refresh();

        Assert.Same(a, view.Items[0]);
        Assert.Equal(2, view.Count);
    }
}

public class ResultsViewModelTests
{
    private static ExecutionRequest Request(int machines) => new()
    {
        Item = new LaunchItem { FullPath = @"C:\s\Get-DiskSpace.ps1", RootFolder = @"C:\s", Type = ItemType.PowerShell, DisplayName = "Get-DiskSpace" },
        Targets = Enumerable.Range(1, machines).Select(i => $"SRV{i:00}").ToList()
    };

    private static void SetStatus(ResultsViewModel vm, int index, RemoteStatus status, string error = "") =>
        vm.Rows[index].Update(new RemoteResult { Computer = vm.Rows[index].Computer, Status = status, Error = error, Output = "ok" });

    [Fact]
    public void ManyMachinesStartAsTiles()
    {
        Assert.False(new ResultsViewModel(new FakeResultsHost(), Request(3), "TEST\\u").ShowTiles);
        var many = new ResultsViewModel(new FakeResultsHost(), Request(ResultsViewModel.TilesThreshold), "TEST\\u");
        Assert.True(many.ShowTiles);
        Assert.Equal(ResultsViewModel.TilesThreshold, many.VisibleRows.Count);
    }

    [Fact]
    public void FilterAndTileOrderPutProblemsFirst()
    {
        var vm = new ResultsViewModel(new FakeResultsHost(), Request(4), "TEST\\u");
        SetStatus(vm, 0, RemoteStatus.Success);
        SetStatus(vm, 1, RemoteStatus.Error, "Zugriff verweigert");
        SetStatus(vm, 2, RemoteStatus.Unreachable);
        vm.RefreshViews();

        Assert.Equal(new[] { "SRV02", "SRV03", "SRV04", "SRV01" }, vm.VisibleTiles.Select(r => r.Computer));
        Assert.Equal(new[] { "SRV01", "SRV02", "SRV03", "SRV04" }, vm.VisibleRows.Select(r => r.Computer));

        vm.SetFilterCommand.Execute("err");
        Assert.Equal(new[] { "SRV02" }, vm.VisibleRows.Select(r => r.Computer));
        Assert.Equal(new[] { "SRV02" }, vm.VisibleTiles.Select(r => r.Computer));

        vm.SetFilterCommand.Execute("all");
        Assert.Equal(4, vm.VisibleRows.Count);
        Assert.Equal(1, vm.SuccessCount);
        Assert.Equal(1, vm.ErrorCount);
        Assert.Equal(1, vm.UnreachableCount);
        Assert.Equal(0.75, vm.Progress);
    }

    [Fact]
    public void SecondClickOnTileClearsSelection()
    {
        var vm = new ResultsViewModel(new FakeResultsHost(), Request(2), "TEST\\u");
        var tile = vm.VisibleTiles[0];

        vm.SelectTileCommand.Execute(tile);
        Assert.True(tile.IsSelected);
        Assert.True(vm.HasSelectedTile);

        vm.SelectTileCommand.Execute(tile);
        Assert.False(tile.IsSelected);
        Assert.False(vm.HasSelectedTile);
    }

    [Fact]
    public void RetryStartsOnlyFailedMachines()
    {
        var host = new FakeResultsHost();
        var vm = new ResultsViewModel(host, Request(3), "TEST\\u");
        SetStatus(vm, 0, RemoteStatus.Success);
        SetStatus(vm, 1, RemoteStatus.Error);
        SetStatus(vm, 2, RemoteStatus.Unreachable);

        Assert.True(vm.RetryFailedCommand.CanExecute(null));
        vm.RetryFailedCommand.Execute(null);

        var retry = Assert.Single(host.Retries);
        Assert.Equal(new[] { "SRV02", "SRV03" }, retry.Targets);
    }

    [Fact]
    public void CsvExportWritesChosenFileOrWarns()
    {
        var host = new FakeResultsHost();
        var vm = new ResultsViewModel(host, Request(2), "TEST\\u");
        SetStatus(vm, 0, RemoteStatus.Success);
        SetStatus(vm, 1, RemoteStatus.Error, "kaputt");
        var file = Path.Combine(Path.GetTempPath(), $"results-{Guid.NewGuid():N}.csv");
        try
        {
            host.FakeDialogs.FileAnswer = file;
            vm.ExportCsvCommand.Execute(null);
            Assert.Contains("SRV02", File.ReadAllText(file));
            Assert.Contains("Get-DiskSpace_", host.FakeDialogs.Asked.Single()); // Windows-Pfad: unter Linux ohne Trennung

            host.FakeDialogs.FileAnswer = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "fehlt", "x.csv");
            vm.ExportCsvCommand.Execute(null);
            Assert.Equal(("warn", "CSV-Export"), (host.FakeDialogs.Messages.Single().Kind, host.FakeDialogs.Messages.Single().Title));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void CancelledExportWritesNothing()
    {
        var host = new FakeResultsHost();
        var vm = new ResultsViewModel(host, Request(1), "TEST\\u");
        host.FakeDialogs.FileAnswer = null;

        vm.ExportCsvCommand.Execute(null);

        Assert.Empty(host.FakeDialogs.Messages);
    }

    [Fact]
    public void LogFolderOpensInExplorer()
    {
        var host = new FakeResultsHost();
        var vm = new ResultsViewModel(host, Request(1), "TEST\\u");

        vm.OpenLogFolderCommand.Execute(null);

        Assert.Equal((host.Settings.EffectiveLogFolder, true), Assert.Single(host.FakeShell.Opened));
    }
}
