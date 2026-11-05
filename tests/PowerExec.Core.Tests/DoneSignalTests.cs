using System.Text;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;

namespace PowerExec.Core.Tests;

/// <summary>Ende eines Scripts erkennen, obwohl das Fenster offen bleibt.</summary>
public class DoneSignalTests
{
    private const string Done = @"C:\Users\x\AppData\Local\Temp\PowerExec\done-1.txt";

    private static LaunchItem Ps => new() { FullPath = @"C:\Scripts\Test.ps1", RootFolder = @"C:\Scripts", Type = ItemType.PowerShell };
    private static LaunchItem Cmd => new() { FullPath = @"C:\My Scripts\Clean.cmd", RootFolder = @"C:\My Scripts", Type = ItemType.Batch };

    [Fact]
    public void PowerShellWritesResultAfterScript()
    {
        var spec = CommandBuilder.BuildLocal(new ExecutionRequest { Item = Ps, NoExit = true }, Done);
        var command = Encoding.Unicode.GetString(Convert.FromBase64String(spec.Arguments.Split(' ').Last()));
        Assert.StartsWith("& 'C:\\Scripts\\Test.ps1'", command);
        Assert.Contains("WriteAllText('" + Done + "'", command);
        Assert.Contains("$LASTEXITCODE", command);
    }

    [Fact]
    public void BatchUsesCallAndErrorlevel()
    {
        var spec = CommandBuilder.BuildLocal(new ExecutionRequest { Item = Cmd, NoExit = true, ExtraArguments = "-x" }, Done);
        Assert.Equal("/k \"call \"C:\\My Scripts\\Clean.cmd\" -x & if errorlevel 1 (>\"" + Done + "\" echo E) else (>\"" + Done + "\" echo 0)\"", spec.Arguments);
    }

    [Fact]
    public void NoMarkerWhenWindowClosesOrOtherUser()
    {
        var closes = CommandBuilder.BuildLocal(new ExecutionRequest { Item = Cmd, NoExit = false }, Done);
        Assert.DoesNotContain("errorlevel", closes.Arguments);
        var other = CommandBuilder.BuildLocal(new ExecutionRequest { Item = Ps, NoExit = true, Mode = ExecutionMode.OtherUser }, Done);
        Assert.DoesNotContain("WriteAllText", Encoding.Unicode.GetString(Convert.FromBase64String(other.Arguments.Split(' ').Last())));
        var exe = new LaunchItem { FullPath = @"C:\Tools\a.exe", RootFolder = @"C:\Tools", Type = ItemType.Executable };
        Assert.False(CommandBuilder.SupportsDoneFile(new ExecutionRequest { Item = exe }));
    }
}
