using PowerExec.Core.Config;
using Xunit;

namespace PowerExec.Core.Tests;

public class V015Tests
{
    [Fact]
    public void ProfileOptionsOverrideBrowser()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://intranet", Browser = BrowserChoice.Edge, BrowserProfile = "Default" };
        var work = new LaunchProfile { Name = "Arbeit", Options = new EntryOptions { Browser = BrowserChoice.Edge, BrowserProfile = "Profile 1" } };
        var priv = new LaunchProfile { Name = "Privat", Options = new EntryOptions { Browser = BrowserChoice.Chrome, BrowserProfile = "Profile 2", Private = true } };
        Assert.Contains("--profile-directory=\"Profile 1\"", e.WithProfile(work).ToLaunchItem().LaunchArguments);
        var chrome = e.WithProfile(priv).ToLaunchItem();
        Assert.Equal("chrome.exe", chrome.LaunchTarget);
        Assert.Contains("--incognito", chrome.LaunchArguments);
        Assert.Equal("https://intranet", e.WithProfile(priv).Target);
        Assert.Same(e.Target, e.WithProfile(null).Target);
        Assert.Equal("Chrome · Profile 2 · privat", priv.Options.Summary(CustomEntryKind.Url));
        var clone = priv.Clone();
        clone.Options!.Browser = BrowserChoice.Firefox;
        Assert.Equal(BrowserChoice.Chrome, priv.Options.Browser);
    }

    [Fact]
    public void OptionsRoundTrip()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Rdp, Target = "SRV", User = "u", Screen = RdpScreen.Fullscreen, AdminSession = true, Gateway = "gw" };
        var o = EntryOptions.From(e);
        var other = new CustomEntry { Kind = CustomEntryKind.Rdp, Target = "SRV2" };
        o.ApplyTo(other);
        Assert.Equal("SRV2", other.Target);
        Assert.Equal("u", other.User);
        Assert.True(other.AdminSession);
        Assert.Equal("u · Vollbild · Admin-Sitzung · über gw", o.Summary(CustomEntryKind.Rdp));
    }

    [Fact]
    public void ChromiumProfiles()
    {
        const string json = """
        {"profile":{"info_cache":{
          "Profile 1":{"name":"Arbeit","user_name":"g@firma.de"},
          "Default":{"name":"Persönlich","user_name":""}}}}
        """;
        var list = BrowserProfiles.ParseChromiumLocalState(json);
        Assert.Equal(2, list.Count);
        Assert.Equal("Default", list[0].Id);
        Assert.Equal("Arbeit (g@firma.de)", list[1].Display);
        Assert.Empty(BrowserProfiles.ParseChromiumLocalState("kaputt"));
    }

    [Fact]
    public void FirefoxProfiles()
    {
        const string ini = "[General]\nStartWithLastProfile=1\n\n[Profile1]\nName=Arbeit\nPath=abc.Arbeit\n\n[Profile0]\nName=default-release\n\n[Install4F96D1932A9F858E]\nDefault=x\n";
        var list = BrowserProfiles.ParseFirefoxProfilesIni(ini);
        Assert.Equal(new[] { "Arbeit", "default-release" }, list.Select(p => p.Name));
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
}
