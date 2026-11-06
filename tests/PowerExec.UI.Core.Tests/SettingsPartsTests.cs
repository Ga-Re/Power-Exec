using PowerExec.Core.Config;
using PowerExec.Infrastructure;
using PowerExec.ViewModels;

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
