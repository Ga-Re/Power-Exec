using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Scanning;
using PowerExec.Core.Search;
using System.Drawing;

namespace PowerExec.Core.Tests;

public class RemoteTestParsingTests
{
    [Fact]
    public void ParsesTestLines()
    {
        var r = PowerExec.Core.Execution.RemoteTester.ParseLine("""{"type":"test","computer":"SRV1","wsman":true,"auth":true,"ps7":true,"version":"5.1.17763","latencyMs":312,"error":""}""");
        Assert.NotNull(r);
        Assert.True(r!.Ok);
        Assert.Contains("PS 7 verfügbar", r.Summary);
        var bad = PowerExec.Core.Execution.RemoteTester.ParseLine("""{"type":"test","computer":"PC9","wsman":false,"auth":false,"ps7":false,"version":"","latencyMs":10,"error":"WinRM nicht erreichbar"}""");
        Assert.False(bad!.Ok);
        Assert.Equal("WinRM nicht erreichbar", bad.Summary);
        Assert.Null(PowerExec.Core.Execution.RemoteTester.ParseLine("""{"type":"done"}"""));
        Assert.Contains("Test-WSMan", PowerExec.Core.Execution.RemoteTester.Script);
        Assert.True(PowerExec.Core.Execution.RemoteTester.ProcessArguments.Length < 32000);
    }
}
