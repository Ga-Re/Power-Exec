using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Layout;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Scanning;
using PowerExec.Core.Search;
using System.Drawing;

namespace PowerExec.Core.Tests;

/// <summary>Einstellungen exportieren, importieren und zusammenführen.</summary>
public class SettingsTransferTests
{
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
    public void MergeAddsCustomEntriesGroupsAndWorkspaces()
    {
        var current = new AppSettings();
        WorkspaceManager.EnsureInitialized(current);
        current.MachineGroups.Add(new MachineGroup { Name = "Web", Machines = ["A"] });
        var imported = new AppSettings();
        WorkspaceManager.EnsureInitialized(imported);
        imported.CustomEntries.Add(new CustomEntry { Name = "Wiki", Target = "https://wiki" });
        imported.MachineGroups.Add(new MachineGroup { Name = "web", Machines = ["B", "C"] });
        WorkspaceManager.Create(imported, "Kunde A", copyCurrent: false);

        var merged = SettingsTransfer.Merge(current, imported);
        Assert.Single(merged.CustomEntries);
        var g = Assert.Single(merged.MachineGroups);
        Assert.Equal(new[] { "B", "C" }, g.Machines);
        Assert.Contains(merged.Workspaces, w => w.Name == "Kunde A");
        Assert.Equal("Standard", merged.ActiveWorkspace);
    }

    [Fact]
    public void ParseMigratesLegacyExport()
    {
        var s = SettingsTransfer.Parse("""{"Edge":"Top","PanelWidth":480}""");
        var p = Assert.Single(s.Panels);
        Assert.Equal(ScreenEdge.Top, p.Edge);
        Assert.Equal(480, p.PanelWidth);
    }
}
