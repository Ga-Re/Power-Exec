using PowerExec.Core.Config;
using PowerExec.Core.Scanning;

namespace PowerExec.Core.Tests;

public class ScanningTests
{
    [Theory]
    [InlineData("Archiv\\alt.ps1", true)]
    [InlineData("Sub\\Archiv\\alt.ps1", true)]
    [InlineData("Archivierung.ps1", false)]
    [InlineData("_helper.ps1", true)]
    [InlineData("Sub\\_helper.ps1", true)]
    [InlineData("Blah.ps1", true)]
    [InlineData("Sub\\blah.PS1", true)]
    [InlineData("Blahblah.ps1", false)]
    [InlineData("setup.exe", true)]
    [InlineData("Get-DiskSpace.ps1", false)]
    public void Exclusions(string relative, bool excluded)
    {
        var m = new ExclusionMatcher(["*\\Archiv\\*", "_*", "Blah.ps1", "*.exe"]);
        Assert.Equal(excluded, m.IsExcluded(relative));
    }

    [Theory]
    [InlineData("Blah.ps1", ExclusionKind.File)]
    [InlineData("*.exe", ExclusionKind.Type)]
    [InlineData("*\\Archiv\\*", ExclusionKind.Pattern)]
    [InlineData("_*", ExclusionKind.Pattern)]
    public void ExclusionKinds(string e, ExclusionKind kind) => Assert.Equal(kind, ExclusionMatcher.KindOf(e));

    [Fact]
    public void ScansFolderWithExtensionsAndExcludes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pe-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Archiv"));
            Directory.CreateDirectory(Path.Combine(root, "Sub"));
            File.WriteAllText(Path.Combine(root, "a.ps1"), "");
            File.WriteAllText(Path.Combine(root, "b.cmd"), "");
            File.WriteAllText(Path.Combine(root, "c.txt"), "");
            File.WriteAllText(Path.Combine(root, "tool.exe"), "");
            File.WriteAllText(Path.Combine(root, "Archiv", "old.ps1"), "");
            File.WriteAllText(Path.Combine(root, "Sub", "deep.ps1"), "");

            var settings = new AppSettings();
            var folder = new FolderConfig { Path = root, Recursive = true, Excludes = ["*\\Archiv\\*", "*.exe"] };
            var r = FolderScanner.Scan(folder, settings);
            Assert.Null(r.Error);
            Assert.Equal(new[] { "a.ps1", "b.cmd", "deep.ps1" }, r.Files.Select(Path.GetFileName).OrderBy(x => x).ToArray());
            Assert.Equal(2, r.ExcludedCount);

            folder.Recursive = false;
            folder.Extensions = [".ps1"];
            r = FolderScanner.Scan(folder, settings);
            Assert.Equal(new[] { "a.ps1" }, r.Files.Select(Path.GetFileName).ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MissingFolderReportsError()
    {
        var r = FolderScanner.Scan(new FolderConfig { Path = "/does/not/exist/pe" }, new AppSettings());
        Assert.NotNull(r.Error);
        Assert.Empty(r.Files);
    }

    [Fact]
    public void MachineListKeepsFavoritesAndPrunes()
    {
        var s = new AppSettings { MaxRecentMachines = 2 };
        MachineList.MarkUsed(s, ["pc1"], new DateTime(2026, 1, 1));
        MachineList.SetFavorite(s, "PC1", true);
        MachineList.MarkUsed(s, ["pc2"], new DateTime(2026, 1, 2));
        MachineList.MarkUsed(s, ["pc3"], new DateTime(2026, 1, 3));
        MachineList.MarkUsed(s, ["pc4"], new DateTime(2026, 1, 4));
        Assert.Equal(new[] { "PC1", "PC3", "PC4" }, s.Machines.Select(m => m.Name).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "PC-1", "PC2", "SRV" }, MachineList.ParseNames("pc-1, pc2;srv  PC2"));
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var file = Path.Combine(Path.GetTempPath(), "pe-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new SettingsStore(file);
            var s = new AppSettings { Edge = ScreenEdge.Left, HoverDelayMs = 300 };
            s.Folders.Add(new FolderConfig { Path = @"C:\Scripts", Excludes = ["_*"] });
            store.Save(s);
            var loaded = store.Load();
            Assert.Equal(ScreenEdge.Left, loaded.Edge);
            Assert.Equal(300, loaded.HoverDelayMs);
            Assert.Equal(@"C:\Scripts", loaded.Folders[0].Path);
            Assert.Contains("\"Left\"", File.ReadAllText(file));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
