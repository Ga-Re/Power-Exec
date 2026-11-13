using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Abläufe (mehrere Einträge nacheinander).</summary>
public class WorkflowTests
{
    [Fact]
    public void WorkflowEntryDescribesStepsAndClonesDeep()
    {
        var wf = new CustomEntry
        {
            Kind = CustomEntryKind.Workflow,
            Name = "Outlook reparieren",
            Steps = new List<WorkflowStep> { new() { Path = @"C:\s\Stop.ps1", Name = "Stop" }, new() { Path = @"C:\s\Cache.ps1" }, new() { Path = "custom:1", Name = "Start" } }
        };
        var item = wf.ToLaunchItem();
        Assert.Equal("Outlook reparieren", item.DisplayName);
        Assert.Equal(CustomEntryKind.Workflow, item.CustomKind);
        Assert.Contains("3 Schritte", item.Synopsis);
        Assert.Contains("Stop → Cache.ps1 → Start", item.Synopsis);
        Assert.True(wf.StopOnError);

        var copy = wf.Clone();
        copy.Steps.RemoveAt(0);
        copy.Steps[0].Name = "geändert";
        Assert.Equal(3, wf.Steps.Count);
        Assert.Equal(string.Empty, wf.Steps[1].Name);
    }
}
