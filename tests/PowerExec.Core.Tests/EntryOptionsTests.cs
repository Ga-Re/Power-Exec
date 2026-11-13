using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

/// <summary>Startoptionen je Profil.</summary>
public class EntryOptionsTests
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
}
