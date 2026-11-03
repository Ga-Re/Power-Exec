using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using Xunit;

namespace PowerExec.Core.Tests;

public class V014Tests
{
    [Fact]
    public void UrlAppWindow()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://portal.example.com", AppWindow = true };
        var item = e.ToLaunchItem();
        Assert.Equal("msedge.exe", item.LaunchTarget);
        Assert.Equal("--app=https://portal.example.com", item.LaunchArguments);
        e.Browser = BrowserChoice.Firefox;
        Assert.Contains("-new-window", e.ToLaunchItem().LaunchArguments);
    }

    [Fact]
    public void RdpCommandLineAndGeneratedFile()
    {
        var e = new CustomEntry { Id = "r1", Kind = CustomEntryKind.Rdp, Target = "SRV-01", Port = 3390, Screen = RdpScreen.Window, ScreenWidth = 1600, ScreenHeight = 900, AllMonitors = true, AdminSession = true };
        Assert.Equal("/v:SRV-01:3390 /w:1600 /h:900 /multimon /admin", e.ToLaunchItem().LaunchArguments);
        Assert.False(e.NeedsGeneratedRdpFile);

        e.User = @"FIRMA\admin";
        e.Gateway = "gw.firma.de";
        Assert.True(e.NeedsGeneratedRdpFile);
        var args = e.ToLaunchItem().LaunchArguments;
        Assert.StartsWith("\"" + e.GeneratedRdpPath + "\"", args);
        Assert.EndsWith("/admin", args);
        var content = e.RdpFileContent();
        Assert.Contains("full address:s:SRV-01:3390", content);
        Assert.Contains(@"username:s:FIRMA\admin", content);
        Assert.Contains("gatewayhostname:s:gw.firma.de", content);
        Assert.DoesNotContain("password", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RdpExistingFile()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Rdp, RdpFile = @"C:\Verbindungen\Server A.rdp", AdminSession = true, Screen = RdpScreen.Fullscreen };
        Assert.Equal("\"C:\\Verbindungen\\Server A.rdp\" /admin", e.ToLaunchItem().LaunchArguments);
        Assert.Equal("Server A", e.ToLaunchItem().DisplayName);
        Assert.DoesNotContain(EntryValidation.Check(e), i => i.IsError);
    }

    [Fact]
    public void SshJumpForwardCommandProfile()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Ssh, Target = "web01", User = "root", JumpHost = "admin@bastion", PortForward = "8080:localhost:80; 5432:db:5432", RemoteCommand = "sudo -i; ls", TerminalProfile = "Prod" };
        var item = e.ToLaunchItem();
        Assert.Equal("wt.exe", item.LaunchTarget);
        Assert.Equal("-p \"Prod\" ssh root@web01 -J admin@bastion -L 8080:localhost:80 -L 5432:db:5432 -t \"sudo -i\\; ls\"", item.LaunchArguments);
        e.Client = SshClient.Putty;
        Assert.Equal(3, e.SshWarnings().Count);
        e.PortForward = "8080-localhost";
        Assert.Contains(EntryValidation.Check(e), i => i.Field == EntryField.PortForward && i.IsError);
    }

    [Fact]
    public void FolderOpenWith()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Folder, Target = @"\\srv\daten", OpenWith = FolderOpener.Terminal };
        Assert.Equal(("wt.exe", "-d \"\\\\srv\\daten\""), (e.ToLaunchItem().LaunchTarget, e.ToLaunchItem().LaunchArguments));
        e.OpenWith = FolderOpener.Program;
        Assert.Contains(EntryValidation.Check(e), i => i.Field == EntryField.OpenWithProgram);
        e.OpenWithProgram = @"C:\TC\totalcmd64.exe";
        Assert.Equal(@"C:\TC\totalcmd64.exe", e.ToLaunchItem().LaunchTarget);
    }

    [Fact]
    public void CommandWorkingDirectoryAndWindow()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Command, Target = "cmd.exe", WorkingDirectory = @"C:\Logs", WindowState = LaunchWindowState.Maximized };
        var item = e.ToLaunchItem();
        var spec = CommandBuilder.BuildLocal(new ExecutionRequest { Item = item, ResolvedPath = item.LaunchTarget });
        Assert.Equal(@"C:\Logs", spec.WorkingDirectory);
        Assert.Equal(LaunchWindowState.Maximized, spec.WindowState);
    }

    [Fact]
    public void BuiltinVariablesAreNotAsked()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text, Code = "Gruß {{$Benutzer}} am {{$Datum}} – Ticket {{Nr}}" };
        Assert.Single(e.Placeholders);
        Assert.True(e.NeedsResolving);
        var resolved = e.WithValues(new Dictionary<string, string> { ["Nr"] = "42" });
        Assert.Contains(Environment.UserName, resolved.Code);
        Assert.Contains("Ticket 42", resolved.Code);
        Assert.DoesNotContain("{{", resolved.Code);
        Assert.False(new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://x" }.NeedsResolving);
    }

    [Fact]
    public void SnippetSyntaxCheck()
    {
        Assert.Empty(EntryValidation.CheckSyntax("Get-Date | Select-Object Year"));
        var errors = EntryValidation.CheckSyntax("if ($true) {\n  Get-Date\n");
        Assert.NotEmpty(errors);
        var e = new CustomEntry { Kind = CustomEntryKind.Snippet, Code = "foreach ($x in" };
        Assert.Contains(EntryValidation.Check(e), i => i.Field == EntryField.Code && i.IsError);
    }

    [Fact]
    public void UrlValidation()
    {
        Assert.Contains(EntryValidation.Check(new CustomEntry { Kind = CustomEntryKind.Url, Target = "intranet firma" }), i => i.IsError);
        Assert.Empty(EntryValidation.Check(new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://ticket/{{Nr}}" }));
    }
}
