using PowerExec.Core.Config;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI.Core.Tests;

public class TagsViewModelTests
{
    private static (FakeEntryHost host, AppSettings s, TagsViewModel vm, List<string> log) Setup()
    {
        var host = new FakeEntryHost();
        var s = new AppSettings();
        Tags.Set(s, @"C:\s\a.ps1", new[] { "ad", "wartung" });
        Tags.Set(s, @"C:\s\b.ps1", new[] { "ad" });
        var log = new List<string>();
        var vm = new TagsViewModel(host, s, () => log.Add("before"), () => log.Add("after"));
        return (host, s, vm, log);
    }

    [Fact]
    public void RowsShowTagsWithCountsAndFilter()
    {
        var (_, _, vm, _) = Setup();

        Assert.Equal(new[] { ("ad", "2 Einträge"), ("wartung", "1 Eintrag") }, vm.Rows.Select(r => (r.Tag, r.CountText)).OrderBy(x => x.Tag));

        vm.Query = "#war";
        Assert.Equal("wartung", Assert.Single(vm.Rows).Tag);
    }

    [Fact]
    public void ColorFromPickerTintsChip()
    {
        var (host, s, vm, _) = Setup();
        var row = vm.Rows.Single(r => r.Tag == "ad");
        Assert.Null(row.ChipColor);

        host.FakeDialogs.ColorAnswer = "#4CAF50";
        row.CustomColorCommand.Execute(null);

        Assert.Equal("#4CAF50", Tags.ColorOf(s, "ad"));
        Assert.Equal(new ArgbColor(0x55, 0x4C, 0xAF, 0x50), row.ChipColor);
        Assert.Contains(row.Colors, c => c.Key == "#4CAF50");
    }

    [Fact]
    public void RenameIntoExistingTagAsksToMerge()
    {
        var (host, s, vm, log) = Setup();
        var row = vm.Rows.Single(r => r.Tag == "wartung");
        host.FakeDialogs.Answer = "#AD";

        host.FakeDialogs.ConfirmAnswer = false;
        row.RenameCommand.Execute(null);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Empty(log);

        host.FakeDialogs.ConfirmAnswer = true;
        vm.Rows.Single(r => r.Tag == "wartung").RenameCommand.Execute(null);
        var merged = Assert.Single(vm.Rows);
        Assert.Equal("ad", merged.Tag);
        Assert.Equal(new[] { "before", "after" }, log);
        Assert.Equal(new[] { "ad" }, Tags.Of(s, @"C:\s\a.ps1"));
    }

    [Fact]
    public void DeleteIsDestructiveConfirm()
    {
        var (host, s, vm, _) = Setup();

        vm.Rows.Single(r => r.Tag == "ad").DeleteCommand.Execute(null);

        Assert.Equal("confirm-destructive", host.FakeDialogs.Messages.Single().Kind);
        Assert.DoesNotContain(vm.Rows, r => r.Tag == "ad");
        Assert.Empty(Tags.Of(s, @"C:\s\b.ps1"));
    }
}

public class PanelSettingsViewModelTests
{
    private static PanelSettingsViewModel Create(FakeEntryHost host, PanelConfig config) =>
        new(host, config, new[] { new KeyValuePair<int, string>(0, "Monitor 1") });

    [Fact]
    public void PreviewUsesPanelColorAndOpacity()
    {
        var config = new PanelConfig { PanelColor = string.Empty, PanelOpacity = 80 };
        var vm = Create(new FakeEntryHost(), config);
        Assert.Null(vm.PreviewColor);
        Assert.True(vm.ColorPresets.First(p => p.Hex.Length == 0).IsSelected);

        vm.PanelColor = "#203040";
        Assert.Equal(new ArgbColor(204, 0x20, 0x30, 0x40), vm.PreviewColor);
        Assert.Contains("Dunkle Fläche", vm.PreviewText);

        vm.PanelOpacity = 50;
        Assert.Equal(127, vm.PreviewColor!.Value.A);
    }

    [Fact]
    public void PresetsHaveSwatchesExceptThemeColor()
    {
        var vm = Create(new FakeEntryHost(), new PanelConfig());

        Assert.All(vm.ColorPresets, p => Assert.Equal(p.Hex.Length == 0, p.SwatchColor == null));
        var preset = vm.ColorPresets.First(p => p.Hex.Length > 0);
        preset.SelectCommand.Execute(null);
        Assert.True(preset.IsSelected);
    }

    [Fact]
    public void PanelColorWorksLikeWindowsAccentPicker()
    {
        var host = new FakeEntryHost { FakeDialogs = { ColorAnswer = "#123456" } };
        var recent = new List<string>();
        var vm = new PanelSettingsViewModel(host, new PanelConfig(), Array.Empty<KeyValuePair<int, string>>(), recent);

        Assert.Equal("design", vm.ColorMode);
        Assert.False(vm.ShowManualColors);
        Assert.Equal(48, vm.WindowsColors.Count);

        vm.ColorMode = "manual";
        Assert.True(vm.ShowManualColors);
        Assert.NotEqual(string.Empty, vm.PanelColor);

        vm.WindowsColors[0].SelectCommand.Execute(null);
        Assert.Equal("#FFB900", vm.PanelColor);
        Assert.True(vm.WindowsColors[0].IsSelected);
        Assert.Equal(new[] { "#FFB900" }, recent);

        vm.PickColorCommand.Execute(null); // „Farben anzeigen“ → benutzerdefinierte Farbe
        Assert.Equal(new[] { "#123456", "#FFB900" }, recent);
        Assert.Equal("#123456", vm.RecentColors[0].Hex);

        vm.ColorMode = "design";
        Assert.Equal(string.Empty, vm.PanelColor);
    }

    [Fact]
    public void ColorPickerAndHotkeyText()
    {
        var host = new FakeEntryHost { FakeDialogs = { ColorAnswer = "#FFFFFF" } };
        var config = new PanelConfig { Hotkey = "Ctrl+Alt+Space" };
        var vm = Create(host, config);

        vm.PickColorCommand.Execute(null);

        Assert.Equal("#FFFFFF", vm.PanelColor);
        Assert.Equal(string.Empty, config.PanelColor); // arbeitet auf einer Kopie, übernommen wird beim Speichern
        Assert.Contains("Helle Fläche", vm.PreviewText);
        Assert.Equal("Strg+Alt+Leertaste", vm.HotkeyDisplay);
    }
}

public class MachineGroupViewModelTests
{
    [Fact]
    public void ImportAddsOnlyNewMachinesAndSortWorks()
    {
        var host = new FakeEntryHost();
        var vm = new MachineGroupViewModel(host, new MachineGroup { Name = "Kunde", Machines = { "SRV2" } }, _ => { });
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "srv2\nsrv1\n");
            host.FakeDialogs.FileAnswer = file;

            vm.ImportCommand.Execute(null);
            Assert.Equal("2 Rechner", vm.CountText);
            Assert.Contains("1 Rechner hinzugefügt", vm.Status);

            vm.SortCommand.Execute(null);
            Assert.Equal("SRV1, SRV2", vm.MachinesInline);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AdSearchAddsFoundMachines()
    {
        var host = new FakeEntryHost { AdAnswer = new[] { "PC01", "PC02" } };
        var vm = new MachineGroupViewModel(host, new MachineGroup { Name = "Büro" }, _ => { }) { AdQuery = "PC*" };

        vm.AdSearchCommand.Execute(null);

        Assert.Equal("PC01, PC02", vm.MachinesInline);
        Assert.False(vm.IsSearching);
    }

    [Fact]
    public void DynamicGroupSyncsThroughHost()
    {
        var host = new FakeEntryHost { AdAnswer = new[] { "SRV9" } };
        var vm = new MachineGroupViewModel(host, new MachineGroup { Name = "Dyn" }, _ => { }) { IsDynamic = true };

        vm.SyncNowCommand.Execute(null);

        Assert.Equal(new[] { "Dyn" }, host.AdSyncs);
        Assert.Equal("SRV9", vm.MachinesInline);
        Assert.Contains("1 Rechner gefunden", vm.Status);
    }

    [Fact]
    public void WorkspaceGroupIndentGrowsWithDepth()
    {
        var ws = new Workspace { Name = "W" };
        Assert.Equal(0, new WorkspaceGroupChoice(ws, "Kunde", "", () => { }).IndentLeft);
        Assert.Equal(44, new WorkspaceGroupChoice(ws, "Kunde/AD/Server", "", () => { }).IndentLeft);
    }
}

public class MachineGroupListSourceTests
{
    [Fact]
    public void ListSourceFiltersCsvAndSyncsWithoutHost()
    {
        var host = new FakeEntryHost();
        var vm = new MachineGroupViewModel(host, new MachineGroup { Name = "Liste" }, _ => { });
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            File.WriteAllText(file, "Hostname;Standort\nsrv1;Berlin\nsrv2;Hamburg\nsrv3;Berlin\n");
            vm.UseList(file);

            Assert.True(vm.IsListSource);
            Assert.Equal("list", vm.Source);
            Assert.True(vm.CanSync);
            Assert.False(vm.IsStatic);
            Assert.True(vm.IsCsv);
            Assert.Equal("Hostname", vm.NameColumn);
            Assert.Equal("SRV1, SRV2, SRV3", vm.MachinesInline);

            vm.AddFilterCommand.Execute(null);
            var f = Assert.Single(vm.ListFilters);
            f.Column = "Standort";
            f.Operator = AdOperator.Equals;
            f.Value = "Berlin";
            Assert.Equal("SRV1, SRV3", vm.MachinesInline);

            var g = vm.ToGroup();
            Assert.Null(g.AdQuery);
            Assert.Equal(file, g.ListQuery!.Path);
            Assert.Equal("Hostname", g.ListQuery.NameColumn);
            Assert.Equal("Standort", Assert.Single(g.ListQuery.Filters).Column);

            f.RemoveCommand.Execute(null);
            Assert.Equal("SRV1, SRV2, SRV3", vm.MachinesInline);

            // Zurück auf statisch: Liste fällt weg, Rechner bleiben
            vm.Source = "static";
            Assert.True(vm.IsStatic);
            Assert.Null(vm.ToGroup().ListQuery);
            Assert.Equal("SRV1, SRV2, SRV3", vm.MachinesInline);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ExportWritesMachines()
    {
        var host = new FakeEntryHost();
        var vm = new MachineGroupViewModel(host, new MachineGroup { Name = "Web", Machines = { "SRV-01", "SRV-02" } }, _ => { });
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            host.FakeDialogs.FileAnswer = file;
            vm.ExportCommand.Execute(null);
            Assert.Contains("SaveFile Web.csv", host.FakeDialogs.Asked);
            Assert.Equal("Name\nSRV-01\nSRV-02\n", File.ReadAllText(file).Replace("\r", ""));
            Assert.Contains("exportiert", vm.Status);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void PickOuShowsTreeAndSetsSearchBase()
    {
        var host = new FakeEntryHost { OuAnswer = new[] { "DC=firma,DC=local", "OU=Server,DC=firma,DC=local" } };
        host.FakeDialogs.OuAnswer = "OU=Server,DC=firma,DC=local";
        var vm = new MachineGroupViewModel(host, new MachineGroup { Name = "Dyn" }, _ => { }) { Source = "ad" };

        vm.PickOuCommand.Execute(null);

        Assert.Equal("OU=Server,DC=firma,DC=local", vm.SearchBase);
        var root = Assert.Single(host.FakeDialogs.OuRoots!);
        Assert.Equal("Server", Assert.Single(root.Children).Name);
        Assert.False(vm.IsSearching);
    }
}
