using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Parametersätze eines Scripts als Startprofile.</summary>
public class ParameterSetTests
{
    [Fact]
    public void ParameterSetBecomesProfileAndKeepsDefault()
    {
        var item = new LaunchItem
        {
            FullPath = @"C:\s\New-User.ps1",
            RootFolder = @"C:\s",
            Type = ItemType.PowerShell,
            Parameters = new[] { new ScriptParameter { Name = "Name", TypeName = "string" }, new ScriptParameter { Name = "Credential", TypeName = "pscredential" } }
        };
        var s = new AppSettings();
        var standard = new LaunchProfile { Name = "Standard", IsDefault = true, IsImplicit = true, Dialog = DialogMode.Always };
        var values = new Dictionary<string, string?> { ["Name"] = "Azubi" };
        Assert.Null(PowerExec.Core.Config.ParameterSets.Save(s, item, "Neuer Azubi", values, standard));
        var profiles = s.FindEntry(item.FullPath)!.Profiles;
        Assert.Equal(2, profiles.Count);
        Assert.True(profiles[0].IsDefault);
        Assert.Equal(DialogMode.Always, profiles[0].Dialog); // Klick im Panel tut weiter dasselbe
        var set = profiles[1];
        Assert.False(set.IsDefault);
        Assert.Equal(ParameterMode.Fixed, set.Parameters["Name"].Mode);
        Assert.Equal("Azubi", set.Parameters["Name"].Value);
        Assert.Equal(ParameterMode.Ask, set.Parameters["Credential"].Mode);
        Assert.NotNull(PowerExec.Core.Config.ParameterSets.Save(s, item, "neuer azubi", values, standard)); // Name vergeben
        Assert.NotNull(PowerExec.Core.Config.ParameterSets.Save(s, item, "  ", values, standard));
    }
}
