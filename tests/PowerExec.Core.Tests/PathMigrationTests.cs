using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Scanning;
using PowerExec.Core.Search;
using System.Drawing;

namespace PowerExec.Core.Tests;

/// <summary>Umbenannte und verschobene Dateien behalten Profile und Favorit.</summary>
public class PathMigrationTests
{
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
}
