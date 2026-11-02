using PowerExec.Core.Metadata;
using PowerExec.Core.Models;

namespace PowerExec.Core.Tests;

public class SampleScriptTests
{
    private static string SamplePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PowerExec.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "samples", "Get-DiskSpace.ps1");
    }

    [Fact]
    public void SampleScriptIsReadCompletely()
    {
        var path = SamplePath();
        var item = new ItemLoader().Load(path, Path.GetDirectoryName(path)!);
        Assert.Equal(ItemType.PowerShell, item.Type);
        Assert.Null(item.ParseError);
        Assert.Equal("Zeigt den freien Speicherplatz aller Laufwerke an.", item.Synopsis);
        Assert.True(item.CopyFolderByDefault);
        Assert.Equal(new[] { "MinFreePercent", "DriveType", "ExportPath", "IncludeSystemReserved" }, item.Parameters.Select(p => p.Name));
        Assert.True(item.Parameters[0].IsMandatory);
        Assert.Equal(ParameterKind.Choice, item.Parameters[1].Kind);
        Assert.Equal(ParameterKind.Switch, item.Parameters[3].Kind);
        Assert.False(item.UsesReadHost);
    }
}
