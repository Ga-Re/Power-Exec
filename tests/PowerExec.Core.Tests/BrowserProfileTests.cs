using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

/// <summary>Browserprofile von Edge, Chrome und Firefox finden.</summary>
public class BrowserProfileTests
{
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
}
