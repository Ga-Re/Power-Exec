using System.Text;
using System.Text.Json;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Updates;

namespace PowerExec.Core.Tests;

public class ExecutionTests
{
    private static LaunchItem Script(string text, string path = @"C:\Scripts\Get-DiskSpace.ps1")
    {
        var item = new LaunchItem { FullPath = path, RootFolder = @"C:\Scripts", Type = ItemType.PowerShell };
        ScriptInfoReader.FillFromText(item, text);
        return item;
    }

    private const string Params = """
        param(
            [Parameter(Mandatory)][int]$MinFreePercent,
            [ValidateSet('Fixed','Network')][string]$DriveType = 'Fixed',
            [string]$Label,
            [switch]$Force,
            [int[]]$Ports,
            [double]$Factor
        )
        """;

    [Fact]
    public void ConvertsParameterValues()
    {
        var item = Script(Params);
        var errors = ParameterValueConverter.TryConvert(item.Parameters, new Dictionary<string, string?>
        {
            ["MinFreePercent"] = "15",
            ["DriveType"] = "Network",
            ["Label"] = "it's",
            ["Force"] = "true",
            ["Ports"] = "80, 443",
            ["Factor"] = "1,5"
        }, out var values);

        Assert.Empty(errors);
        Assert.Equal(15, values["MinFreePercent"]);
        Assert.Equal("Network", values["DriveType"]);
        Assert.Equal(true, values["Force"]);
        Assert.Equal(new object[] { 80, 443 }, (object[])values["Ports"]);
        Assert.Equal(1.5, values["Factor"]);

        var args = ParameterValueConverter.ToPowerShellArguments(values);
        Assert.Equal(" -MinFreePercent 15 -DriveType 'Network' -Label 'it''s' -Force:$true -Ports 80,443 -Factor 1.5", args);
    }

    [Fact]
    public void ReportsValidationErrors()
    {
        var item = Script(Params);
        var errors = ParameterValueConverter.TryConvert(item.Parameters, new Dictionary<string, string?>
        {
            ["MinFreePercent"] = "",
            ["DriveType"] = "Removable",
            ["Factor"] = "abc"
        }, out var values);
        Assert.Equal(3, errors.Count);
        Assert.DoesNotContain("Force", values.Keys);
    }

    [Fact]
    public void LocalPowerShellUsesEncodedCommand()
    {
        var item = Script(Params, @"C:\My Scripts\Test.ps1");
        var req = new ExecutionRequest
        {
            Item = item,
            Engine = PsEngine.PowerShell7,
            Parameters = new Dictionary<string, object> { ["MinFreePercent"] = 15 },
            NoExit = true,
            NoProfile = true
        };
        var spec = CommandBuilder.BuildLocal(req);
        Assert.Equal("pwsh.exe", spec.FileName);
        Assert.StartsWith("-NoProfile -NoExit -ExecutionPolicy Bypass -EncodedCommand ", spec.Arguments);
        var b64 = spec.Arguments.Split(' ').Last();
        Assert.Equal("& 'C:\\My Scripts\\Test.ps1' -MinFreePercent 15", Encoding.Unicode.GetString(Convert.FromBase64String(b64)));
        Assert.False(spec.UseShellExecute);
        Assert.Contains("-Command & 'C:\\My Scripts\\Test.ps1' -MinFreePercent 15", spec.Preview);
    }

    [Fact]
    public void AdminAndOtherUserModes()
    {
        var item = Script("Get-Date");
        var admin = CommandBuilder.BuildLocal(new ExecutionRequest { Item = item, Mode = ExecutionMode.Administrator });
        Assert.True(admin.UseShellExecute);
        Assert.Equal("runas", admin.Verb);
        Assert.Equal("powershell.exe", admin.FileName);

        var other = CommandBuilder.BuildLocal(new ExecutionRequest { Item = item, Mode = ExecutionMode.OtherUser });
        Assert.True(other.RequiresCredential);
        Assert.False(other.UseShellExecute);
        Assert.Null(other.WorkingDirectory);

        var exe = new LaunchItem { FullPath = @"C:\Tools\procexp64.exe", RootFolder = @"C:\Tools", Type = ItemType.Executable };
        var exeSpec = CommandBuilder.BuildLocal(new ExecutionRequest { Item = exe });
        Assert.True(exeSpec.UseShellExecute);
        Assert.Null(exeSpec.Verb);

        var msc = new LaunchItem { FullPath = @"C:\Tools\dsa.msc", RootFolder = @"C:\Tools", Type = ItemType.ConsoleSnapIn };
        var mscOther = CommandBuilder.BuildLocal(new ExecutionRequest { Item = msc, Mode = ExecutionMode.OtherUser });
        Assert.Equal("mmc.exe", mscOther.FileName);
        Assert.Contains("dsa.msc", mscOther.Arguments);
    }

    [Fact]
    public void BatchRespectsNoExit()
    {
        var bat = new LaunchItem { FullPath = @"C:\My Tools\Restart-Spooler.cmd", RootFolder = @"C:\My Tools", Type = ItemType.Batch };
        var spec = CommandBuilder.BuildLocal(new ExecutionRequest { Item = bat, NoExit = true });
        Assert.Equal("cmd.exe", spec.FileName);
        Assert.Equal("/k \"\"C:\\My Tools\\Restart-Spooler.cmd\"\"", spec.Arguments);
        Assert.True(spec.UseShellExecute);

        var admin = CommandBuilder.BuildLocal(new ExecutionRequest { Item = bat, NoExit = false, Mode = ExecutionMode.Administrator });
        Assert.StartsWith("/c ", admin.Arguments);
        Assert.Equal("runas", admin.Verb);
    }

    [Fact]
    public void RemoteConfigContainsNoCredentials()
    {
        var item = Script(Params);
        var req = new ExecutionRequest
        {
            Item = item,
            Targets = ["PC-0142", "PC-0187"],
            Parameters = new Dictionary<string, object> { ["MinFreePercent"] = 15, ["Force"] = true, ["Ports"] = new object[] { 80, 443 } },
            Mode = ExecutionMode.OtherUser
        };
        var line = RemoteRunner.BuildConfigLine(req, new AppSettings { ThrottleLimit = 8 }, useCredential: true);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(line));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("Targets").GetArrayLength());
        Assert.Equal(15, root.GetProperty("Parameters").GetProperty("MinFreePercent").GetInt32());
        Assert.True(root.GetProperty("Parameters").GetProperty("Force").GetBoolean());
        Assert.Equal(8, root.GetProperty("ThrottleLimit").GetInt32());
        Assert.True(root.GetProperty("UseCredential").GetBoolean());
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunnerScriptFitsIntoCommandLine()
    {
        Assert.Contains("CreateRunspacePool", RemoteRunner.Script);
        Assert.True(RemoteRunner.ProcessArguments.Length < 32000, $"Länge {RemoteRunner.ProcessArguments.Length}");
    }

    [Fact]
    public void ParsesResultLines()
    {
        var r = RemoteRunner.ParseResultLine("""{"type":"result","computer":"PC1","status":"Unreachable","durationMs":10000,"output":"x","error":"WinRM","note":"","engine":"5.1"}""");
        Assert.NotNull(r);
        Assert.Equal("PC1", r!.Computer);
        Assert.Equal(RemoteStatus.Unreachable, r.Status);
        Assert.Null(RemoteRunner.ParseResultLine("""{"type":"start","count":2}"""));
        Assert.Null(RemoteRunner.ParseResultLine("WARNUNG: irgendwas"));
        Assert.True(RemoteRunner.IsDoneLine("""{"type":"done"}"""));
    }

    [Fact]
    public void CsvEscapesAndPreventsFormulas()
    {
        var csv = ResultExport.ToCsv([new RemoteResult { Computer = "PC1", Status = RemoteStatus.Success, DurationMs = 1200, Output = "=SUM(A1)\n\"x\"" }]);
        Assert.Contains("\"PC1\";\"Erfolgreich\";\"1,2\"", csv);
        Assert.Contains("\"'=SUM(A1)\n\"\"x\"\"\"", csv);
    }

    [Fact]
    public void FindsNewerRelease()
    {
        var json = """
        [
          {"tag_name":"v1.5.0-beta.1","prerelease":true,"draft":false,"name":"1.5 beta","html_url":"h","body":"","assets":[]},
          {"tag_name":"v1.4.0","prerelease":false,"draft":false,"name":"1.4","html_url":"h14","body":"Neu",
           "assets":[{"name":"PowerExec.exe","browser_download_url":"https://x/PowerExec.exe"},{"name":"PowerExec.exe.sha256","browser_download_url":"https://x/sum"}]},
          {"tag_name":"v1.3.2","prerelease":false,"draft":false,"name":"1.3.2","html_url":"h","body":"","assets":[]}
        ]
        """;
        var r = UpdateChecker.FindNewer(json, new Version(1, 3, 2), includePrereleases: false);
        Assert.NotNull(r);
        Assert.Equal(new Version(1, 4, 0), r!.Version);
        Assert.Equal("https://x/PowerExec.exe", r.ExeUrl);
        Assert.Equal("https://x/sum", r.ChecksumUrl);

        Assert.Equal(new Version(1, 5, 0), UpdateChecker.FindNewer(json, new Version(1, 3, 2), true)!.Version);
        Assert.Null(UpdateChecker.FindNewer(json, new Version(1, 4, 0, 0), false));
        Assert.Equal("ab" + new string('0', 62), UpdateChecker.ParseChecksum("AB" + new string('0', 62) + "  PowerExec.exe"));
    }
}
