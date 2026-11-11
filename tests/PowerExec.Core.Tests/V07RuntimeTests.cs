using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;

namespace PowerExec.Core.Tests;

public class V07RuntimeTests
{
    private static LaunchItem Script(bool admin = false, string version = "") => new()
    {
        FullPath = @"C:\Scripts\a.ps1",
        RootFolder = @"C:\Scripts",
        Type = ItemType.PowerShell,
        RequiresAdmin = admin,
        RequiredPsVersion = version
    };

    [Theory]
    [InlineData("", false)]
    [InlineData("5.1", false)]
    [InlineData("7", true)]
    [InlineData("7.2", true)]
    [InlineData("6.0", true)]
    public void RequiredVersionSelectsPowerShell7(string version, bool expected)
    {
        Assert.Equal(expected, Requirements.NeedsPowerShell7(Script(version: version)));
        Assert.Equal(expected ? PsEngine.PowerShell7 : PsEngine.WindowsPowerShell, Requirements.EngineFor(Script(version: version), PsEngine.WindowsPowerShell));
    }

    [Fact]
    public void RequiresAdminElevatesOnlyLocally()
    {
        var item = Script(admin: true);
        Assert.Equal(ExecutionMode.Administrator, Requirements.ModeFor(item, ExecutionMode.CurrentUser, remote: false));
        Assert.Equal(ExecutionMode.CurrentUser, Requirements.ModeFor(item, ExecutionMode.CurrentUser, remote: true));
        Assert.Equal(ExecutionMode.OtherUser, Requirements.ModeFor(item, ExecutionMode.OtherUser, remote: false));
        Assert.Equal(ExecutionMode.CurrentUser, Requirements.ModeFor(Script(), ExecutionMode.CurrentUser, remote: false));
    }

    [Fact]
    public void CustomUrlStartsViaShellWithoutWorkingDirectory()
    {
        var item = new CustomEntry { Name = "Intranet", Kind = CustomEntryKind.Url, Target = "https://intranet.local/x" }.ToLaunchItem();
        var spec = CommandBuilder.BuildLocal(new ExecutionRequest { Item = item, ResolvedPath = item.LaunchTarget });
        Assert.Equal("https://intranet.local/x", spec.FileName);
        Assert.True(spec.UseShellExecute);
        Assert.Null(spec.WorkingDirectory);
    }

    [Fact]
    public void CustomRdpRunsMstscWithHost()
    {
        var item = new CustomEntry { Kind = CustomEntryKind.Rdp, Target = "SRV-01", Arguments = "/admin" }.ToLaunchItem();
        var spec = CommandBuilder.BuildLocal(new ExecutionRequest
        {
            Item = item,
            ResolvedPath = item.LaunchTarget,
            ExtraArguments = item.LaunchArguments
        });
        Assert.Equal("mstsc.exe", spec.FileName);
        Assert.Equal("/v:SRV-01 /admin", spec.Arguments);
    }

    [Fact]
    public void MergeAddsCustomEntriesGroupsAndWorkspaces()
    {
        var current = new AppSettings();
        WorkspaceManager.EnsureInitialized(current);
        current.MachineGroups.Add(new MachineGroup { Name = "Web", Machines = ["A"] });
        var imported = new AppSettings();
        WorkspaceManager.EnsureInitialized(imported);
        imported.CustomEntries.Add(new CustomEntry { Name = "Wiki", Target = "https://wiki" });
        imported.MachineGroups.Add(new MachineGroup { Name = "web", Machines = ["B", "C"] });
        WorkspaceManager.Create(imported, "Kunde A", copyCurrent: false);

        var merged = SettingsTransfer.Merge(current, imported);
        Assert.Single(merged.CustomEntries);
        var g = Assert.Single(merged.MachineGroups);
        Assert.Equal(new[] { "B", "C" }, g.Machines);
        Assert.Contains(merged.Workspaces, w => w.Name == "Kunde A");
        Assert.Equal("Standard", merged.ActiveWorkspace);
    }

    [Fact]
    public void ParseMigratesLegacyExport()
    {
        var s = SettingsTransfer.Parse("""{"Edge":"Top","PanelWidth":480}""");
        var p = Assert.Single(s.Panels);
        Assert.Equal(ScreenEdge.Top, p.Edge);
        Assert.Equal(480, p.PanelWidth);
    }
}
