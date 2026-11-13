using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Zuletzt verwendete Parameterwerte.</summary>
public class ParameterHistoryTests
{
    [Fact]
    public void ParameterHistoryKeepsRecentValues()
    {
        var s = new AppSettings();
        for (int i = 0; i < 15; i++) ParameterHistory.Add(s, @"C:\a.ps1", "Name", "Wert" + i);
        ParameterHistory.Add(s, @"C:\A.ps1", "name", "Wert3");
        var list = ParameterHistory.Get(s, @"c:\a.ps1", "NAME");
        Assert.Equal(ParameterHistory.MaxValues, list.Count);
        Assert.Equal("Wert3", list[0]);
        ParameterHistory.Add(s, @"C:\a.ps1", "Name", "  ");
        Assert.Equal("Wert3", ParameterHistory.Get(s, @"C:\a.ps1", "Name")[0]);
    }
}
