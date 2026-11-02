using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Scanning;

namespace PowerExec.Core.Tests;

public class ProfileAndVisibilityTests
{
    private const string Script = """
        param(
            [Parameter(Mandatory)][int]$MinFreePercent,
            [ValidateSet('Fixed','All')][string]$DriveType = 'Fixed',
            [string]$ExportPath,
            [switch]$Force,
            [pscredential]$Credential,
            [string]$AdminPassword
        )
        """;

    private static LaunchItem Item()
    {
        var item = new LaunchItem { FullPath = @"C:\Scripts\Get-DiskSpace.ps1", RootFolder = @"C:\Scripts", Type = ItemType.PowerShell };
        ScriptInfoReader.FillFromText(item, Script);
        return item;
    }

    [Fact]
    public void SecureParametersAreDetectedAndNeverPassed()
    {
        var item = Item();
        Assert.Equal(ParameterKind.Secure, item.Parameters.Single(p => p.Name == "Credential").Kind);
        Assert.Equal(ParameterKind.Secure, item.Parameters.Single(p => p.Name == "AdminPassword").Kind);
        var errors = ParameterValueConverter.TryConvert(item.Parameters,
            new Dictionary<string, string?> { ["MinFreePercent"] = "5", ["AdminPassword"] = "geheim" }, out var values);
        Assert.Empty(errors);
        Assert.False(values.ContainsKey("AdminPassword"));
    }

    [Fact]
    public void ImplicitProfileKeepsPreviousBehavior()
    {
        var s = new AppSettings { DefaultEngine = PsEngine.WindowsPowerShell, NoExit = false };
        var item = Item();
        var p = ProfileResolver.GetDefault(s, item);
        Assert.True(p.IsImplicit);
        Assert.Equal(DialogMode.Always, p.Dialog);
        Assert.Equal(PsEngine.WindowsPowerShell, p.Engine);
        Assert.True(ProfileResolver.NeedsDialog(p, item));

        var exe = new LaunchItem { FullPath = @"C:\Tools\x.exe", RootFolder = @"C:\Tools", Type = ItemType.Executable };
        Assert.False(ProfileResolver.NeedsDialog(ProfileResolver.GetDefault(s, exe), exe));
    }

    [Fact]
    public void DialogOnlyWhenNeeded()
    {
        var item = Item();
        var p = ProfileResolver.CreateNew(new AppSettings(), item, "Lokal");
        p.Dialog = DialogMode.WhenNeeded;
        Assert.True(ProfileResolver.NeedsDialog(p, item)); // Pflichtfeld MinFreePercent fehlt

        p.Parameters["MinFreePercent"] = new ParameterSetting { Mode = ParameterMode.Fixed, Value = "15" };
        Assert.False(ProfileResolver.NeedsDialog(p, item));

        p.Target = ProfileTarget.Ask;
        Assert.True(ProfileResolver.NeedsDialog(p, item));

        p.Target = ProfileTarget.Fixed;
        p.Machines = ["SRV1", "SRV2"];
        p.Dialog = DialogMode.Never;
        Assert.False(ProfileResolver.NeedsDialog(p, item));
        Assert.Equal("2 Rechner · startet direkt", ProfileResolver.Summary(p, item));

        var values = ProfileResolver.InitialValues(p, item);
        Assert.Equal("15", values["MinFreePercent"]);
        Assert.Equal("Fixed", values["DriveType"]); // Standardwert aus dem Script vorbelegt
        Assert.Null(values["Credential"]);
        Assert.True(ProfileResolver.IsFixed(p, item.Parameters[0]));
    }

    [Fact]
    public void StoredProfilesWin()
    {
        var item = Item();
        var s = new AppSettings();
        s.Entries.Add(new EntryConfig
        {
            Path = item.FullPath.ToLowerInvariant(),
            Profiles =
            [
                new LaunchProfile { Name = "A" },
                new LaunchProfile { Name = "B", IsDefault = true, RunAsAdmin = true }
            ]
        });
        Assert.Equal(2, ProfileResolver.GetProfiles(s, item).Count);
        var d = ProfileResolver.GetDefault(s, item);
        Assert.Equal("B", d.Name);
        Assert.EndsWith("Admin", ProfileResolver.Summary(d, item));
    }

    [Fact]
    public void VisibilityAutoAndApproval()
    {
        var f = new FolderConfig { Path = @"C:\Scripts" };
        Assert.Equal(EntryVisibility.Visible, FolderVisibility.State(f, "a.ps1"));

        FolderVisibility.SetVisible(f, "Sub\\b.ps1", false);
        Assert.Equal(EntryVisibility.Hidden, FolderVisibility.State(f, "Sub/b.ps1"));
        FolderVisibility.SetVisible(f, "Sub\\b.ps1", true);
        Assert.Equal(EntryVisibility.Visible, FolderVisibility.State(f, "Sub\\b.ps1"));

        // Auto: alles Gefundene wird bekannt, Verschwundenes vergessen
        Assert.True(FolderVisibility.UpdateKnown(f, ["a.ps1", "Sub\\b.ps1", "c.ps1"]));
        Assert.False(FolderVisibility.UpdateKnown(f, ["a.ps1", "Sub\\b.ps1", "c.ps1"]));
        Assert.True(FolderVisibility.UpdateKnown(f, ["a.ps1", "c.ps1"]));
        Assert.Equal(new[] { "a.ps1", "c.ps1" }, f.KnownFiles);

        // Freigabe: neue Dateien warten
        f.NewFiles = NewFilesMode.RequireApproval;
        FolderVisibility.UpdateKnown(f, ["a.ps1", "c.ps1", "neu.ps1"]);
        Assert.Equal(EntryVisibility.Pending, FolderVisibility.State(f, "neu.ps1"));
        Assert.Equal(EntryVisibility.Visible, FolderVisibility.State(f, "a.ps1"));
        FolderVisibility.MarkKnown(f, "neu.ps1");
        Assert.Equal(EntryVisibility.Visible, FolderVisibility.State(f, "neu.ps1"));
    }

    [Fact]
    public void LegacyFileExcludesBecomeHidden()
    {
        var f = new FolderConfig { Path = @"C:\Scripts", Excludes = ["Blah.ps1", "*.exe", "*\\Archiv\\*"] };
        FolderVisibility.MigrateLegacyExcludes(f);
        Assert.Equal(new[] { "*.exe", "*\\Archiv\\*" }, f.Excludes);
        Assert.Equal(new[] { "Blah.ps1" }, f.Hidden);
        Assert.True(FolderVisibility.IsHidden(f, "Sub\\Blah.ps1")); // Dateiname gilt in allen Unterordnern
        FolderVisibility.SetVisible(f, "Sub\\Blah.ps1", true);
        Assert.Empty(f.Hidden);
    }

    [Fact]
    public void ScannerReportsPatternExcludedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "pe-excl-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Archiv"));
            File.WriteAllText(Path.Combine(root, "a.ps1"), "");
            File.WriteAllText(Path.Combine(root, "Archiv", "old.ps1"), "");
            var r = FolderScanner.Scan(new FolderConfig { Path = root, Excludes = ["*\\Archiv\\*"] }, new AppSettings());
            Assert.Single(r.Files);
            var ex = Assert.Single(r.Excluded);
            Assert.Equal("Archiv\\old.ps1", ex.RelativePath);
            Assert.Equal("*\\Archiv\\*", ex.Pattern);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExtraArgumentsArePassed()
    {
        var exe = new LaunchItem { FullPath = @"C:\Windows\mstsc.exe", RootFolder = @"C:\Windows", Type = ItemType.Executable };
        var spec = CommandBuilder.BuildLocal(new ExecutionRequest { Item = exe, ExtraArguments = "/v:SRV-APP02", Mode = ExecutionMode.Administrator });
        Assert.Equal("/v:SRV-APP02", spec.Arguments);
        Assert.Equal("runas", spec.Verb);

        var bat = new LaunchItem { FullPath = @"C:\T\x y.cmd", RootFolder = @"C:\T", Type = ItemType.Batch };
        var b = CommandBuilder.BuildLocal(new ExecutionRequest { Item = bat, ExtraArguments = "-a 1", NoExit = false });
        Assert.Equal("/c \"\"C:\\T\\x y.cmd\" -a 1\"", b.Arguments);
    }
}
