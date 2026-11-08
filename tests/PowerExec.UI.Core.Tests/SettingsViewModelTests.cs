using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.Core.Updates;
using PowerExec.Infrastructure;
using PowerExec.ViewModels;

namespace PowerExec.UI.Core.Tests;

/// <summary>Einstellungsfenster ohne Oberfläche – mit echten Einstellungen und dem Fake-Host.</summary>
public class SettingsViewModelTests
{
    private static (FakeEntryHost host, SettingsViewModel vm) Create(Action<AppSettings>? setup = null)
    {
        var host = new FakeEntryHost();
        SmartGroups.Add(host.Settings, @"C:\Scripts");
        GroupManager.Create(host.Settings, "Kunde A", null);
        host.Items.List.Add(new LaunchItem { FullPath = @"C:\Scripts\Get-DiskSpace.ps1", RootFolder = @"C:\Scripts", Type = ItemType.PowerShell, DisplayName = "Get-DiskSpace" });
        host.Items.List.Add(new LaunchItem { FullPath = @"C:\Scripts\procexp.exe", RootFolder = @"C:\Scripts", Type = ItemType.Executable, DisplayName = "procexp" });
        setup?.Invoke(host.Settings);
        return (host, new SettingsViewModel(host, focusFolder: null));
    }

    [Fact]
    public void WorksOnCopyAndBuildsResult()
    {
        var (host, vm) = Create();

        Assert.Single(vm.Folders);
        Assert.Contains(vm.Categories, c => c.Name == "Kunde A");
        vm.CheckUpdatesOnStart = !host.Settings.CheckUpdatesOnStart;

        var built = vm.Build();
        Assert.NotSame(host.Settings, built);
        Assert.NotEqual(host.Settings.CheckUpdatesOnStart, built.CheckUpdatesOnStart);
        Assert.Equal(@"C:\Users\test\AppData\Local\Power-Exec\settings.json", vm.SettingsFile);
        Assert.Equal("0.19.5", vm.CurrentVersion);
    }

    [Fact]
    public void PanelSizeDraggedWhileSettingsOpenSurvivesApply()
    {
        var (host, vm) = Create(s => s.Panels.Add(new PanelConfig { PanelWidth = 400, PanelHeightPercent = 80 }));

        // Panel am Rand gezogen, während die Einstellungen offen sind
        host.Settings.Panels[0].PanelWidth = 620;
        host.Settings.Panels[0].PanelHeightPercent = 95;
        Assert.Equal((620, 95), (vm.Build().Panels[0].PanelWidth, vm.Build().Panels[0].PanelHeightPercent));

        // Im Formular geändert: das Formular gilt
        vm.Panel.PanelWidth = 500;
        Assert.Equal((500, 95), (vm.Build().Panels[0].PanelWidth, vm.Build().Panels[0].PanelHeightPercent));
    }

    [Fact]
    public void FolderRowsAreFilterable()
    {
        var (_, vm) = Create();
        var folder = vm.Folders.Single();

        Assert.Equal(2, folder.VisibleRows.Count);
        folder.Query = "disk";
        Assert.EndsWith("Get-DiskSpace.ps1", Assert.Single(folder.VisibleRows).RelativePath); // Windows-Pfade: unter Linux nicht relativ
        folder.Query = "gibtsnicht";
        Assert.True(folder.NoRows);
    }

    [Fact]
    public void ConfigureOpensProfilesDialogThroughHost()
    {
        var (host, vm) = Create();
        var row = vm.Folders.Single().VisibleRows.First(r => r.Item != null);

        row.ConfigureCommand.Execute(null);

        Assert.Contains(host.Calls, c => c.StartsWith("ShowProfilesDialog "));
    }

    [Fact]
    public void RemovingGroupAsksAndKeepsItWhenDeclined()
    {
        var (host, vm) = Create();
        var group = vm.Categories.Single(c => c.Name == "Kunde A");

        host.FakeDialogs.ConfirmAnswer = false;
        group.RemoveCommand.Execute(null);
        Assert.Contains(group, vm.Categories);
        Assert.Equal(("confirm-destructive", "Gruppe löschen"), (host.FakeDialogs.Messages[0].Kind, host.FakeDialogs.Messages[0].Title));

        host.FakeDialogs.ConfirmAnswer = true;
        group.RemoveCommand.Execute(null);
        Assert.DoesNotContain(group, vm.Categories);
    }

    [Fact]
    public void GroupColorFromPickerAndIndent()
    {
        var (host, vm) = Create();
        var group = vm.Categories.Single(c => c.Name == "Kunde A");
        host.FakeDialogs.ColorAnswer = "#E91E63";

        group.CustomColorCommand.Execute(null);

        Assert.Equal("#E91E63", group.ColorOwn);
        Assert.Equal(new ArgbColor(255, 0xE9, 0x1E, 0x63), group.SwatchColor);
        Assert.Equal(0, group.IndentLeft);
    }

    [Fact]
    public void ExportWritesFileAndConfirms()
    {
        var (host, vm) = Create();
        var file = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        try
        {
            host.FakeDialogs.FileAnswer = file;
            vm.ExportCommand.Execute(null);

            Assert.True(File.Exists(file));
            Assert.Equal(("info", "Exportieren"), (host.FakeDialogs.Messages.Single().Kind, host.FakeDialogs.Messages.Single().Title));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ImportMergesReplacesOrCancels()
    {
        var (host, vm) = Create();
        var file = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(file, SettingsTransfer.Export(new AppSettings { CheckUpdatesOnStart = false }));
            host.FakeDialogs.FileAnswer = file;
            var reopened = 0;
            vm.ReopenRequested += () => reopened++;

            host.FakeDialogs.ConfirmOrCancelAnswer = null;
            vm.ImportCommand.Execute(null);
            Assert.Empty(host.Applied);

            host.FakeDialogs.ConfirmOrCancelAnswer = true; // zusammenführen: Ordner bleiben
            vm.ImportCommand.Execute(null);
            Assert.Single(Assert.Single(host.Applied).Folders);

            host.FakeDialogs.ConfirmOrCancelAnswer = false; // ersetzen
            vm.ImportCommand.Execute(null);
            Assert.Empty(host.Applied[1].Folders);
            Assert.Equal(2, reopened);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void UnreadableImportWarns()
    {
        var (host, vm) = Create();
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "{ kaputt");
            host.FakeDialogs.FileAnswer = file;

            vm.ImportCommand.Execute(null);

            Assert.Equal("warn", host.FakeDialogs.Messages.Single().Kind);
            Assert.Empty(host.Applied);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void UpdateCheckReportsNewVersion()
    {
        var (host, vm) = Create();
        host.CheckAnswer = new ReleaseInfo(new Version(0, 20, 0), "v0.20.0", "Power-Exec 0.20.0", false, "https://github.com/x/y/releases/tag/v0.20.0", null, null, "");

        vm.CheckUpdatesCommand.Execute(null);

        Assert.Contains("0.20.0", vm.UpdateStatus);
        Assert.True(vm.HasUpdate);
        Assert.Equal("Version 0.20.0 ist verfügbar", vm.UpdateBannerTitle);
        vm.ReleaseNotesCommand.Execute(null);
        Assert.Equal("https://github.com/x/y/releases/tag/v0.20.0", host.FakeShell.Urls.Single());
    }

    [Fact]
    public void DiagnosticsGoToClipboard()
    {
        var (host, vm) = Create();

        vm.CopyLogCommand.Execute(null);

        var text = host.FakeShell.Clipboard.Single();
        Assert.StartsWith("Power-Exec 0.19.5 · portabel", text);
    }

    [Fact]
    public void CatalogSubscriptionShowsError()
    {
        var (host, vm) = Create();
        host.FakeDialogs.FileAnswer = @"\\srv\kataloge\team.json";
        host.SubscribeAnswer = "Datei nicht gefunden";

        vm.SubscribeCatalogCommand.Execute(null);

        Assert.Contains(@"Subscribe \\srv\kataloge\team.json", host.Calls);
        Assert.Equal("Datei nicht gefunden", vm.CatalogError);
    }

    [Fact]
    public void DesignPresetsHaveReadablePreview()
    {
        var (_, vm) = Create();

        foreach (var p in vm.DesignPresets)
        {
            Assert.Equal(255, p.SwatchColor.A);
            var ink = p.InkColor;
            Assert.True(ink == new ArgbColor(255, 0, 0, 0) || ink == new ArgbColor(255, 255, 255, 255));
        }
    }
}
