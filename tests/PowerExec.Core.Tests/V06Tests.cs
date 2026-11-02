using System.Drawing;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Scanning;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

public class V06Tests
{
    [Fact]
    public void RecentRunsDeduplicateAndLimit()
    {
        var s = new AppSettings();
        RecentRunList.Add(s, @"C:\a.ps1", "Lokal");
        RecentRunList.Add(s, @"C:\b.ps1", "Standard");
        RecentRunList.Add(s, @"C:\A.ps1", "lokal");
        Assert.Equal(2, s.RecentRuns.Count);
        Assert.Equal(@"C:\A.ps1", s.RecentRuns[0].Path);
        for (int i = 0; i < 30; i++) RecentRunList.Add(s, $@"C:\x{i}.ps1", "Standard");
        Assert.Equal(RecentRunList.MaxStored, s.RecentRuns.Count);
        Assert.Equal("vor 5 Min.", RecentRunList.Ago(DateTime.UtcNow.AddMinutes(-5)));
    }

    [Fact]
    public void CustomOrderAndMove()
    {
        var items = new[] { "a", "b", "c", "d" };
        Assert.Equal(new[] { "c", "a", "b", "d" }, ItemOrdering.Apply(items, x => x, new[] { "c" }));
        var moved = ItemOrdering.Move(new[] { "a", "b", "c", "d" }, "d", "b", after: false);
        Assert.Equal(new[] { "a", "d", "b", "c" }, moved);
        Assert.Equal(new[] { "a", "c", "d", "b" }, ItemOrdering.Move(new[] { "a", "b", "c", "d" }, "b", "d", after: true));
    }

    [Fact]
    public void RenamedFileKeepsProfileAndFavorite()
    {
        var root = Path.Combine(Path.GetTempPath(), "pe-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Sub"));
        try
        {
            var oldPath = Path.Combine(root, "Alt.ps1");
            File.WriteAllText(oldPath, "Get-Date # eindeutiger Inhalt " + Guid.NewGuid());
            File.WriteAllText(Path.Combine(root, "Anderes.ps1"), "Get-Process");
            var s = new AppSettings();
            s.Folders.Add(new FolderConfig { Path = root });
            s.Entries.Add(new EntryConfig { Path = oldPath, DisplayName = "Mein Script" });
            s.Favorites.Add(oldPath);
            s.CustomOrder["fav"] = [oldPath];
            Assert.True(PathMigration.UpdateFingerprints(s, [oldPath], PathMigration.Fingerprint));

            // umbenennen + verschieben
            var newPath = Path.Combine(root, "Sub", "Neu.ps1");
            File.Move(oldPath, newPath);
            var existing = Directory.GetFiles(root, "*.ps1", SearchOption.AllDirectories);
            var moves = PathMigration.Migrate(s, existing, [root], PathMigration.Fingerprint, p => new FileInfo(p).Length);

            var m = Assert.Single(moves);
            Assert.Equal(newPath, m.NewPath);
            Assert.Equal(newPath, s.Entries[0].Path);
            Assert.Equal(newPath, s.Favorites[0]);
            Assert.Equal(newPath, s.CustomOrder["fav"][0]);
            Assert.True(s.Fingerprints.ContainsKey(newPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void UnreachableFolderDoesNotMigrate()
    {
        var s = new AppSettings();
        s.Entries.Add(new EntryConfig { Path = @"\\srv\share\x.ps1" });
        var moves = PathMigration.Migrate(s, [], [], _ => null, _ => null);
        Assert.Empty(moves);
        Assert.Equal(@"\\srv\share\x.ps1", s.Entries[0].Path);
    }

    [Fact]
    public void ExportImportMerge()
    {
        var a = new AppSettings { Edge = ScreenEdge.Left };
        a.Folders.Add(new FolderConfig { Path = @"C:\Scripts" });
        a.Favorites.Add(@"C:\Scripts\x.ps1");
        a.Fingerprints[@"C:\Scripts\x.ps1"] = "1:ab";
        a.Entries.Add(new EntryConfig { Path = @"C:\Scripts\x.ps1", Category = "Drucker" });

        var json = SettingsTransfer.Export(a);
        Assert.DoesNotContain("1:ab", json);
        var imported = SettingsTransfer.Parse(json);
        Assert.Equal(ScreenEdge.Left, imported.Edge);
        Assert.Equal("Drucker", imported.Entries[0].Category);

        var b = new AppSettings { Edge = ScreenEdge.Right };
        b.Folders.Add(new FolderConfig { Path = @"D:\Tools" });
        var merged = SettingsTransfer.Merge(b, imported);
        Assert.Equal(ScreenEdge.Right, merged.Edge); // allgemeine Einstellungen bleiben
        Assert.Equal(2, merged.Folders.Count);
        Assert.Single(merged.Favorites);
        Assert.Single(merged.Entries);
    }

    [Fact]
    public void FullscreenDetection()
    {
        var mon = new Rectangle(0, 0, 1920, 1080);
        Assert.True(Fullscreen.IsFullscreen(new Rectangle(0, 0, 1920, 1080), mon, "Chrome_WidgetWin_1"));
        Assert.True(Fullscreen.IsFullscreen(new Rectangle(-1, -1, 1922, 1082), mon, "mpv"));
        Assert.False(Fullscreen.IsFullscreen(new Rectangle(0, 0, 1920, 1032), mon, "Notepad")); // maximiert, Taskleiste sichtbar
        Assert.False(Fullscreen.IsFullscreen(new Rectangle(0, 0, 1920, 1080), mon, "Progman"));  // Desktop
        Assert.False(Fullscreen.IsFullscreen(new Rectangle(1920, 0, 1920, 1080), mon, "x"));      // anderer Monitor
    }

    [Fact]
    public void ZoneIdentifierParsing()
    {
        var z = ZoneIdentifier.ParseZoneId("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.com/\r\n");
        Assert.Equal(3, z);
        Assert.True(ZoneIdentifier.IsBlocked(z));
        Assert.False(ZoneIdentifier.IsBlocked(ZoneIdentifier.ParseZoneId("[ZoneTransfer]\nZoneId=1")));
        Assert.False(ZoneIdentifier.IsBlocked(null));
    }

    [Fact]
    public void ScanCacheRoundTrip()
    {
        var file = Path.Combine(Path.GetTempPath(), "pe-cache-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var item = new LaunchItem { FullPath = @"\\srv\s\a.ps1", RootFolder = @"\\srv\s", Type = ItemType.PowerShell, Synopsis = "Hallo", ZoneId = 3 };
            item.Parameters = [new ScriptParameter { Name = "Name", TypeName = "string", IsMandatory = true }];
            item.Shortcut = new ShortcutTarget("t.exe", "-x", "C:\\");
            var cache = new ScanCache(file);
            cache.Save([new CachedFolder { Path = @"\\srv\s", ScannedUtc = DateTime.UtcNow, Items = [item],
                Excluded = [new ExcludedFile(@"\\srv\s\Archiv\b.ps1", @"Archiv\b.ps1", @"*\Archiv\*")] }]);
            var loaded = cache.Load();
            var f = loaded[@"\\SRV\s"];
            var i = Assert.Single(f.Items);
            Assert.Equal("Hallo", i.Synopsis);
            Assert.True(i.IsBlocked);
            Assert.True(i.Parameters[0].IsMandatory);
            Assert.Equal("-x", i.Shortcut!.Arguments);
            Assert.Equal(@"*\Archiv\*", f.Excluded[0].Pattern);
        }
        finally
        {
            File.Delete(file);
        }
    }
}

public class RemoteTestParsingTests
{
    [Fact]
    public void ParsesTestLines()
    {
        var r = PowerExec.Core.Execution.RemoteTester.ParseLine("""{"type":"test","computer":"SRV1","wsman":true,"auth":true,"ps7":true,"version":"5.1.17763","latencyMs":312,"error":""}""");
        Assert.NotNull(r);
        Assert.True(r!.Ok);
        Assert.Contains("PS 7 verfügbar", r.Summary);
        var bad = PowerExec.Core.Execution.RemoteTester.ParseLine("""{"type":"test","computer":"PC9","wsman":false,"auth":false,"ps7":false,"version":"","latencyMs":10,"error":"WinRM nicht erreichbar"}""");
        Assert.False(bad!.Ok);
        Assert.Equal("WinRM nicht erreichbar", bad.Summary);
        Assert.Null(PowerExec.Core.Execution.RemoteTester.ParseLine("""{"type":"done"}"""));
        Assert.Contains("Test-WSMan", PowerExec.Core.Execution.RemoteTester.Script);
        Assert.True(PowerExec.Core.Execution.RemoteTester.ProcessArguments.Length < 32000);
    }
}
