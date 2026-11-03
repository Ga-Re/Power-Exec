using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

public class V07Tests
{
    [Fact]
    public void LegacyPanelSettingsAreMigrated()
    {
        var file = Path.Combine(Path.GetTempPath(), "pe-v07-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, """{"Edge":"Left","PanelWidth":520,"PanelColor":"#1B2A3D","Hotkey":"Ctrl+Alt+P","DefaultFilter":"Favorites"}""");
            var s = new SettingsStore(file).Load();
            var p = Assert.Single(s.Panels);
            Assert.Equal("Hauptpanel", p.Name);
            Assert.Equal(ScreenEdge.Left, p.Edge);
            Assert.Equal(520, p.PanelWidth);
            Assert.Equal("#1B2A3D", p.PanelColor);
            Assert.Equal("Ctrl+Alt+P", p.Hotkey);
            Assert.Equal(TypeFilter.Favorites, p.DefaultFilter);
            Assert.Equal("Standard", s.ActiveWorkspace);
            Assert.Single(s.Workspaces);
        }
        finally
        {
            File.Delete(file);
        }
    }

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

    [Fact]
    public void WorkspacesSwitchKeepData()
    {
        // Ab 0.13: Ordner/Gruppen global; Favoriten, Profile, Verlauf und Gruppenauswahl je Arbeitsbereich
        var s = new AppSettings();
        WorkspaceManager.EnsureInitialized(s);
        s.Folders.Add(new FolderConfig { Path = @"C:\KundeA", Group = "A" });
        s.Favorites.Add(@"C:\KundeA\x.ps1");
        s.Entries.Add(new EntryConfig { Path = @"C:\KundeA\x.ps1", Notes = "global", Profiles = [new LaunchProfile { Name = "P1", IsDefault = true }] });

        var b = WorkspaceManager.Create(s, "Kunde B", copyCurrent: false);
        Assert.True(WorkspaceManager.Switch(s, b.Name));
        Assert.Single(s.Folders);                          // global
        Assert.Empty(s.Favorites);                         // je Arbeitsbereich
        Assert.Empty(s.FindEntry(@"C:\KundeA\x.ps1")!.Profiles);
        Assert.Equal("global", s.FindEntry(@"C:\KundeA\x.ps1")!.Notes);
        WorkspaceManager.SetGroupHidden(WorkspaceManager.Active(s), "A", true);
        Assert.False(WorkspaceManager.ShowsGroup(s, "A/Unter"));

        Assert.True(WorkspaceManager.Switch(s, "standard"));
        Assert.Single(s.Favorites);
        Assert.Equal("P1", Assert.Single(s.FindEntry(@"C:\KundeA\x.ps1")!.Profiles).Name);
        Assert.True(WorkspaceManager.ShowsGroup(s, "A"));

        Assert.True(WorkspaceManager.Switch(s, "Kunde B"));
        Assert.False(WorkspaceManager.Delete(s, "Kunde B")); // aktiv
        Assert.True(WorkspaceManager.Rename(s, "Kunde B", "Kunde Beta"));
        Assert.Equal("Kunde Beta", s.ActiveWorkspace);
        var copy = WorkspaceManager.Create(s, "Kunde Beta", copyCurrent: true);
        Assert.Equal("Kunde Beta 2", copy.Name);
        Assert.Single(copy.HiddenGroups);
        Assert.True(WorkspaceManager.Delete(s, "Standard"));
    }

    [Fact]
    public void CustomEntriesBecomeLaunchItems()
    {
        var rdp = new CustomEntry { Name = "Fileserver", Kind = CustomEntryKind.Rdp, Target = "SRV-FILE01" }.ToLaunchItem();
        Assert.True(rdp.IsCustom);
        Assert.Equal("mstsc.exe", rdp.LaunchTarget);
        var spec = CommandBuilder.BuildLocal(new ExecutionRequest { Item = rdp, ResolvedPath = rdp.LaunchTarget, ExtraArguments = rdp.LaunchArguments });
        Assert.Equal("mstsc.exe", spec.FileName);
        Assert.Equal("/v:SRV-FILE01", spec.Arguments);

        var url = new CustomEntry { Name = "Wiki", Kind = CustomEntryKind.Url, Target = "https://wiki.example/it" }.ToLaunchItem();
        var urlSpec = CommandBuilder.BuildLocal(new ExecutionRequest { Item = url, ResolvedPath = url.LaunchTarget });
        Assert.True(urlSpec.UseShellExecute);
        Assert.Equal("https://wiki.example/it", urlSpec.FileName);
        Assert.StartsWith(PanelConfig.CustomEntriesKey, url.FullPath);
    }

    [Fact]
    public void MachineGroupsAndImport()
    {
        var s = new AppSettings();
        s.MachineGroups.Add(new MachineGroup { Name = "Druckserver", Machines = ["PRT01", "PRT02"] });
        Assert.Equal(new[] { "PRT01", "PRT02", "PC1" }, MachineList.Resolve(s, ["@druckserver", "pc1", "prt01"]));
        var csv = "Name;Standort\r\nPC-0142;Nord\r\n\"PC-0187\";Süd\r\n# Kommentar\r\npc-0142;x\r\n";
        Assert.Equal(new[] { "PC-0142", "PC-0187" }, MachineList.ParseImport(csv));
        Assert.Equal(new[] { "A", "B" }, MachineList.ParseImport("a\nb\n"));
    }

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

    [Fact]
    public void RequiresIsRead()
    {
        var item = new LaunchItem { FullPath = "/tmp/x.ps1", RootFolder = "/tmp", Type = ItemType.PowerShell };
        ScriptInfoReader.FillFromText(item, "#Requires -RunAsAdministrator\n#Requires -Version 7.2\n#Requires -Modules ActiveDirectory\nGet-Date");
        Assert.True(item.RequiresAdmin);
        Assert.Equal("7.2", item.RequiredPsVersion);
        Assert.Equal(new[] { "ActiveDirectory" }, item.RequiredModules);
    }

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
