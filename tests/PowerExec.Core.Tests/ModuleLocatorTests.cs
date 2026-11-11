using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Benötigte PowerShell-Module finden.</summary>
public class ModuleLocatorTests
{
    [Fact]
    public void ModuleLocatorFindsModules()
    {
        var root = Path.Combine(Path.GetTempPath(), "pe-mod-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Lib", "MyModule"));
            Directory.CreateDirectory(Path.Combine(root, "Script", "Modules", "Local"));
            var missing = ModuleLocator.FindMissing(["MyModule", "Local", "Fehlt"], [Path.Combine(root, "Lib")], Path.Combine(root, "Script"));
            Assert.Equal(new[] { "Fehlt" }, missing);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
