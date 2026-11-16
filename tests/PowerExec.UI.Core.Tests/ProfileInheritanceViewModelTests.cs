using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI.Core.Tests;

/// <summary>Eintrags-Editor: Reiter, Vererbung vom Standardprofil, Variablen je Profil, Vorschau mit Bearbeiten.</summary>
public class ProfileInheritanceViewModelTests
{
    private static (FakeEntryHost Host, ProfilesViewModel Vm) Ticket()
    {
        var host = new FakeEntryHost();
        host.Settings.Variables.Add(new UserVariable { Name = "Kunde", Value = "Fabrikam" });
        var entry = new CustomEntry { Kind = CustomEntryKind.Url, Name = "Ticket", Target = "https://esm/{{$Kunde}}/{{Ticket}}?srv={{Server:srv-01;srv-02}}" };
        var vm = new ProfilesViewModel(host, entry.ToLaunchItem(), host.Settings, saveImmediately: false, entry, isNew: true);
        return (host, vm);
    }

    [Fact]
    public void OpensOnProfilesTabAndSwitchesToGeneral()
    {
        var (_, vm) = Ticket();
        Assert.True(vm.IsProfilesPage);
        vm.IsGeneralPage = true;
        Assert.Equal("general", vm.Page);
        vm.ShowProfilesPageCommand.Execute(null);
        Assert.True(vm.IsProfilesPage);
    }

    [Fact]
    public void NewProfileInheritsEverythingAndFollowsTheStandard()
    {
        var (_, vm) = Ticket();
        var standard = vm.Profiles.Single();
        vm.AddCommand.Execute(null);
        var server = vm.Selected!;
        Assert.Equal(0, server.OwnCount);
        Assert.True(server.WindowInherit.IsInherited);
        Assert.False(standard.WindowInherit.ShowControls);                 // im Standard gibt es kein ✎/↺

        standard.ConfirmBeforeRun = true;                                  // Standard ändern → wirkt im geerbten Wert
        Assert.True(server.ConfirmBeforeRun);

        server.WindowInherit.MakeOwnCommand.Execute(null);
        server.ConfirmBeforeRun = false;
        standard.ConfirmBeforeRun = true;
        Assert.False(server.ConfirmBeforeRun);                             // eigener Wert bleibt
        Assert.Equal(1, server.OwnCount);

        server.WindowInherit.ResetCommand.Execute(null);                   // ↺ zurück zum Standard
        Assert.True(server.ConfirmBeforeRun);
        Assert.Equal(0, server.OwnCount);
    }

    [Fact]
    public void ProfileOverridesTargetAndVariablesAndSavesOnlyItsOwnValues()
    {
        var (host, vm) = Ticket();
        vm.Entry!.Name = "Ticket";
        vm.AddCommand.Execute(null);
        var server = vm.Selected!;
        server.Name = "Server";

        // Variablen-Tabelle: Abfragen und {{$Variablen}} aus dem Text
        Assert.Contains(server.Variables, v => v.Name == "Ticket" && v.IsQuery);
        Assert.Contains(server.Variables, v => v.Name == "Server" && v.IsQuery);
        var kunde = server.Variables.Single(v => v.Name == "$Kunde");
        Assert.True(kunde.IsVariable);
        Assert.False(kunde.Inherit.IsEditable);                            // geerbt: erst anpassen

        kunde.Inherit.MakeOwnCommand.Execute(null);
        kunde.Value = "Contoso";
        server.TargetInherit.MakeOwnCommand.Execute(null);
        Assert.StartsWith("https://esm/", server.TargetText);              // eigenes Ziel beginnt mit dem des Eintrags
        server.TargetText = server.TargetText.Replace("esm/", "esm-admin/");
        var ticket = server.Variables.Single(v => v.Name == "Ticket");
        ticket.Inherit.MakeOwnCommand.Execute(null);
        ticket.KindKey = "fixed";
        ticket.Value = "42";

        vm.SaveCommand.Execute(null);
        var key = host.Settings.CustomEntries.Single().Key;
        var stored = host.Settings.Entries.Single(e => e.Path == key).Profiles.Single(p => p.Name == "Server");
        Assert.Equal(new[] { "Target", "Var:$Kunde", "Var:Ticket" }, stored.Own!.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("Contoso", stored.Variables["$Kunde"].Value);

        // Beim Start: eigenes Ziel und eigene Werte, der Rest vom Standard; Auswahl bleibt eine Abfrage
        var item = host.Settings.CustomEntries.Single().ToLaunchItem();
        var effective = ProfileInheritance.Resolve(host.Settings.Entries.Single(e => e.Path == key).Profiles).Single(p => p.Name == "Server");
        var launched = host.Settings.CustomEntries.Single().WithProfile(effective);
        Assert.Equal("https://esm-admin/Contoso/42?srv={{Server:srv-01;srv-02}}", launched.Target);
        Assert.Equal(new[] { "Server" }, launched.Placeholders.Select(p => p.Name));
        Assert.NotNull(item);
    }

    [Fact]
    public void ChangedStandardIsReportedAndCanBeTakenOver()
    {
        var (_, vm) = Ticket();
        var standard = vm.Profiles.Single();
        vm.AddCommand.Execute(null);
        var server = vm.Selected!;
        server.WindowInherit.MakeOwnCommand.Execute(null);
        Assert.False(server.HasStandardChanged);

        standard.ConfirmBeforeRun = !standard.ConfirmBeforeRun;            // Standard nach dem Anpassen geändert
        Assert.True(server.HasStandardChanged);
        Assert.Contains("Fenster", server.StandardChangedText, StringComparison.Ordinal);

        server.DismissStandardChangeCommand.Execute(null);                 // eigene behalten
        Assert.False(server.HasStandardChanged);
        Assert.True(server.WindowInherit.IsOwn);

        standard.ConfirmBeforeRun = !standard.ConfirmBeforeRun;
        server.AdoptStandardCommand.Execute(null);                         // Standard übernehmen
        Assert.False(server.WindowInherit.IsOwn);
        Assert.Equal(standard.ConfirmBeforeRun, server.ConfirmBeforeRun);
    }

    [Fact]
    public void MakingAnotherProfileTheStandardKeepsEveryProfilesValues()
    {
        var (_, vm) = Ticket();
        var first = vm.Profiles.Single();
        vm.AddCommand.Execute(null);
        var second = vm.Selected!;
        second.WindowInherit.MakeOwnCommand.Execute(null);
        second.ConfirmBeforeRun = !first.ConfirmBeforeRun;

        second.IsDefault = true;
        Assert.True(second.IsDefault);
        Assert.False(first.IsDefault);
        Assert.NotEqual(first.ConfirmBeforeRun, second.ConfirmBeforeRun);  // Werte bleiben
        Assert.True(first.WindowInherit.IsOwn);                            // Unterschied ist jetzt eigen im alten Standard
    }

    [Fact]
    public void ProfileOnlyVariableCanBeAddedAndRemoved()
    {
        var (host, vm) = Ticket();
        var standard = vm.Profiles.Single();
        host.FakeDialogs.Answer = "Umgebung";
        standard.AddVariableCommand.Execute(null);
        var row = standard.Variables.Single(v => v.Name == "$Umgebung");
        Assert.True(row.IsProfileOnly);
        row.Value = "Test";
        Assert.Equal("Test", standard.ToProfile().Variables["$Umgebung"].Value);
        row.RemoveCommand.Execute(null);
        Assert.DoesNotContain(standard.Variables, v => v.Name == "$Umgebung");
    }

    [Fact]
    public void ScriptFilePreviewShowsCodeAndSavesWithBackup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "px-edit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var backups = Path.Combine(dir, "backups");
        var before = ScriptFiles.BackupFolder;
        ScriptFiles.BackupFolder = backups;
        try
        {
            var path = Path.Combine(dir, "Reset.ps1");
            File.WriteAllText(path, "param([string]$User)\nWrite-Host $User\n");
            var host = new FakeEntryHost();
            var item = new LaunchItem { FullPath = path, Type = ItemType.PowerShell, RootFolder = dir, DisplayName = "Reset.ps1" };
            PowerExec.Core.Metadata.ScriptInfoReader.FillFromText(item, File.ReadAllText(path));
            var vm = new ProfilesViewModel(host, item, host.Settings, saveImmediately: false);
            var p = vm.Selected!;
            Assert.True(p.HasCode);
            Assert.True(p.CanEditFile);
            Assert.Contains(p.CodeSegments, s => s.Text.Contains("Write-Host", StringComparison.Ordinal));

            p.EditCodeCommand.Execute(null);
            Assert.True(p.IsEditingCode);
            p.EditableCode = "param([string]$User, [switch]$Force)\nWrite-Host $User\n";
            p.SaveCodeCommand.Execute(null);

            Assert.False(p.IsEditingCode);
            Assert.Contains("$Force", File.ReadAllText(path), StringComparison.Ordinal);
            var backup = Assert.Single(Directory.GetFiles(backups));
            Assert.DoesNotContain("$Force", File.ReadAllText(backup), StringComparison.Ordinal);
            Assert.Contains(p.Parameters, x => x.Name == "Force");                   // Parameter neu gelesen
        }
        finally
        {
            ScriptFiles.BackupFolder = before;
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LegacyProfilesMoveIntoInheritanceByDifferences()
    {
        var host = new FakeEntryHost();
        var entry = new CustomEntry { Kind = CustomEntryKind.Command, Target = "cmd.exe" };
        host.Settings.CustomEntries.Add(entry);
        host.Settings.Entries.Add(new EntryConfig
        {
            Path = entry.Key,
            Profiles =
            {
                new LaunchProfile { Name = "Standard", IsDefault = true },
                new LaunchProfile { Name = "Admin", RunAsAdmin = true }      // altes Profil ohne Vererbung
            }
        });
        var vm = new ProfilesViewModel(host, entry.ToLaunchItem(), host.Settings, saveImmediately: false, entry);
        var admin = vm.Profiles.Single(p => p.Name == "Admin");
        Assert.True(admin.RunAsInherit.IsOwn);                             // Unterschied → eigen
        Assert.True(admin.WindowInherit.IsInherited);                      // gleicher Wert → geerbt
        Assert.Equal(1, admin.OwnCount);
    }

    [Fact]
    public void LegacyScriptProfileWithParametersShowsNoFalseStandardChange()
    {
        var host = new FakeEntryHost();
        var item = new LaunchItem { FullPath = @"C:\Scripts\Reset.ps1", Type = ItemType.PowerShell, RootFolder = @"C:\Scripts", DisplayName = "Reset.ps1" };
        PowerExec.Core.Metadata.ScriptInfoReader.FillFromText(item, "param([Parameter(Mandatory)][string]$User, [string]$Mode = 'Reset', [switch]$Force)\nWrite-Host $User");
        host.Settings.Entries.Add(new EntryConfig
        {
            Path = item.FullPath,
            Profiles =
            {
                new LaunchProfile { Name = "Standard", IsDefault = true },              // ohne Parametereinstellungen gespeichert
                new LaunchProfile { Name = "Set", Parameters = { ["Mode"] = new ParameterSetting { Mode = ParameterMode.Fixed, Value = "Unlock" } } }
            }
        });
        var vm = new ProfilesViewModel(host, item, host.Settings, saveImmediately: false);
        var set = vm.Profiles.Single(p => p.Name == "Set");
        Assert.False(set.HasStandardChanged, set.StandardChangedText);
        Assert.True(set.Parameters.Single(p => p.Name == "Mode").Inherit.IsOwn);
        Assert.False(set.Parameters.Single(p => p.Name == "User").Inherit.IsOwn);  // gleiche Vorgabe → geerbt
    }
}
