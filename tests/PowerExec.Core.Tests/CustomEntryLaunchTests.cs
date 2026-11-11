using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Eigene Einträge werden zu Startbefehlen: Webseite, RDP, SSH, Ordner, Befehl, Werkzeug, Snippet.</summary>
public class CustomEntryLaunchTests
{
    [Fact]
    public void SshCommandLines()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Ssh, Target = "srv", User = "root", Port = 2222 };
        var item = e.ToLaunchItem();
        Assert.Equal("wt.exe", item.LaunchTarget);
        Assert.Equal("ssh root@srv -p 2222", item.LaunchArguments);
        e.Client = SshClient.Putty;
        Assert.Equal("-ssh root@srv -P 2222", e.ToLaunchItem().LaunchArguments);
        Assert.Equal("SSH · root@srv:2222", e.SummaryText());
    }

    [Fact]
    public void BrowserChoiceBuildsArguments()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://x", Browser = BrowserChoice.Edge, Private = true, BrowserProfile = "Profile 2" };
        var item = e.ToLaunchItem();
        Assert.Equal("msedge.exe", item.LaunchTarget);
        Assert.Equal("--profile-directory=\"Profile 2\" --inprivate https://x", item.LaunchArguments);
        Assert.Equal(ItemType.Executable, item.Type);
        Assert.Equal("x", new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://x/a" }.ToLaunchItem().DisplayName);
    }

    [Fact]
    public void ToolTypes()
    {
        Assert.Equal(ItemType.ConsoleSnapIn, new CustomEntry { Kind = CustomEntryKind.Tool, Target = "dsa.msc" }.ToLaunchItem().Type);
        Assert.Equal(ItemType.Other, new CustomEntry { Kind = CustomEntryKind.Tool, Target = "ms-settings:display" }.ToLaunchItem().Type);
        var cpl = new CustomEntry { Kind = CustomEntryKind.Tool, Target = "control.exe", Arguments = "ncpa.cpl" }.ToLaunchItem();
        Assert.Equal(ItemType.Executable, cpl.Type);
        Assert.Equal("ncpa.cpl", cpl.LaunchArguments);
    }

    [Fact]
    public void SnippetItems()
    {
        var ps = new CustomEntry { Kind = CustomEntryKind.Snippet, Code = "# Kommentar\nGet-Date" };
        var item = ps.ToLaunchItem();
        Assert.True(item.IsPowerShell);
        Assert.Equal(ps.SnippetPath, item.LaunchTarget);
        Assert.Equal("Get-Date", item.Synopsis);
        Assert.False(ps.CopiesToClipboard);
        Assert.True(new CustomEntry { Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text }.CopiesToClipboard);
        Assert.True(new CustomEntry { Kind = CustomEntryKind.Snippet, Action = SnippetAction.Copy }.CopiesToClipboard);
    }

    [Fact]
    public void InfoTextIsNotRepeated()
    {
        var item = new CustomEntry { Kind = CustomEntryKind.Url, Name = "Google", Target = "https://www.google.de/index.html" }.ToLaunchItem();
        Assert.Equal("https://www.google.de/index.html", item.Synopsis);
        Assert.Equal(string.Empty, item.Description);
    }

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
    public void TerminalPathQuoting()
    {
        Assert.Equal(@"C:\Temp", CustomEntry.CommandLinePath(@"C:\Temp\"));
        Assert.Equal(@"C:\.", CustomEntry.CommandLinePath(@"C:\"));
        Assert.Equal(@"D:\.", CustomEntry.CommandLinePath("D:"));
        Assert.Equal(@"\\srv\share", CustomEntry.CommandLinePath(@"\\srv\share\"));
        CustomEntry.TerminalAvailable = () => true;
        var e = new CustomEntry { Kind = CustomEntryKind.Folder, Target = @"C:\Temp\", OpenWith = FolderOpener.Terminal };
        Assert.Equal("-d \"C:\\Temp\"", e.ToLaunchItem().LaunchArguments);
        CustomEntry.TerminalAvailable = () => false;
        try
        {
            var ps = e.ToLaunchItem();
            Assert.Equal("powershell.exe", ps.LaunchTarget);
            Assert.Contains("Set-Location -LiteralPath 'C:\\Temp'", ps.LaunchArguments);
        }
        finally { CustomEntry.TerminalAvailable = () => true; }
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
    public void CustomEntriesBecomeLaunchItems()
    {
        var rdp = new CustomEntry { Name = "Fileserver", Kind = CustomEntryKind.Rdp, Target = "SRV-FILE01" }.ToLaunchItem();
        Assert.True(rdp.IsCustom);
        Assert.Equal("mstsc.exe", rdp.LaunchTarget);
        var spec = CommandBuilder.BuildLocal(new ExecutionRequest { Item = rdp, ResolvedPath = rdp.LaunchTarget, ExtraArguments = rdp.LaunchArguments });
        Assert.Equal("mstsc.exe", spec.FileName);
        Assert.Equal("/v:SRV-FILE01", spec.Arguments);

        var url = new CustomEntry { Name = "Wiki", Kind = CustomEntryKind.Url, Target = "https://wiki.example/it" }.ToLaunchItem();
        var urlSpec = CommandBuilder.BuildLocal(new ExecutionRequest { Item = url, ResolvedPath = url.LaunchTarget });
        Assert.True(urlSpec.UseShellExecute);
        Assert.Equal("https://wiki.example/it", urlSpec.FileName);
        Assert.StartsWith(PanelConfig.CustomEntriesKey, url.FullPath);
    }
}
