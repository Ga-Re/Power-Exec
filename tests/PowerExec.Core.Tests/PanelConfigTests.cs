using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Einstellungen eines Panels.</summary>
public class PanelConfigTests
{
    [Fact]
    public void PanelContentFilter()
    {
        var p = new PanelConfig();
        Assert.True(p.ShowsFolder(@"C:\x"));
        p.Folders = [@"C:\Scripts", PanelConfig.CustomEntriesKey];
        Assert.True(p.ShowsFolder(@"c:\scripts"));
        Assert.False(p.ShowsFolder(@"C:\Tools"));
        Assert.True(p.ShowsFolder(PanelConfig.CustomEntriesKey));
        p.Categories = ["AD"];
        Assert.True(p.ShowsCategory("ad"));
        Assert.False(p.ShowsCategory("Drucker"));
    }
}
