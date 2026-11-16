using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;

namespace PowerExec.Core.Tests;

/// <summary>Vererbung zwischen Startprofilen, Variablen je Profil und Auswahl-Abfragen.</summary>
public class ProfileInheritanceTests
{
    private static LaunchProfile Standard() => new()
    {
        Name = "Standard",
        IsDefault = true,
        Dialog = DialogMode.WhenNeeded,
        Engine = PsEngine.PowerShell7,
        Parameters = { ["Mode"] = new ParameterSetting { Mode = ParameterMode.Fixed, Value = "Reset" } },
        Variables = { ["$Kunde"] = new ProfileVariable { Value = "Fabrikam" } }
    };

    [Fact]
    public void ProfileTakesEverythingFromStandardExceptItsOwnValues()
    {
        var standard = Standard();
        var server = new LaunchProfile
        {
            Name = "Server",
            RunAsAdmin = true,
            Target = ProfileTarget.Fixed,
            Machines = { "@DC" },
            Dialog = DialogMode.Never,
            Own = new List<string> { ProfileInheritance.RunAs }
        };
        var effective = ProfileInheritance.Effective(standard, server);

        Assert.True(effective.RunAsAdmin);                                   // eigen
        Assert.Equal(new[] { "@DC" }, effective.Machines);
        Assert.Equal(DialogMode.WhenNeeded, effective.Dialog);              // geerbt (Fenster und Verhalten)
        Assert.Equal("Reset", effective.Parameters["Mode"].Value);          // geerbter Parameter
        Assert.Equal("Fabrikam", effective.Variables["$Kunde"].Value);      // geerbte Variable
        Assert.Equal("Server", effective.Name);
        Assert.False(effective.IsDefault);

        // Der Standard ändert sich: wirkt im geerbten Wert, nicht im eigenen
        standard.Dialog = DialogMode.Always;
        standard.RunAsAdmin = false;
        var again = ProfileInheritance.Effective(standard, server);
        Assert.Equal(DialogMode.Always, again.Dialog);
        Assert.True(again.RunAsAdmin);
    }

    [Fact]
    public void OldProfilesWithoutInheritanceStayAsTheyAre()
    {
        var old = new LaunchProfile { Name = "Alt", Dialog = DialogMode.Never }; // Own == null
        Assert.Same(old, ProfileInheritance.Effective(Standard(), old));
        Assert.True(ProfileInheritance.Owns(old, ProfileInheritance.Window));
    }

    [Fact]
    public void MigrationMarksOnlyDifferingValuesAsOwn()
    {
        var standard = Standard();
        var test = standard.Clone();
        test.Name = "Test";
        test.IsDefault = false;
        test.Parameters["Mode"] = new ParameterSetting { Mode = ParameterMode.Fixed, Value = "Unlock" };
        test.Variables["$Kunde"] = new ProfileVariable { Value = "Contoso" };

        var own = ProfileInheritance.Differences(standard, test);
        Assert.Equal(new[] { "Param:Mode", "Var:$Kunde" }, own.OrderBy(k => k));

        test.Own = own;
        test.OwnBase = own.ToDictionary(k => k, k => ProfileInheritance.Snapshot(standard, k));
        Assert.Empty(ProfileInheritance.ChangedInStandard(standard, test));
        standard.Parameters["Mode"].Value = "Disable";                      // Standard nach dem Anpassen geändert
        Assert.Equal(new[] { "Param:Mode" }, ProfileInheritance.ChangedInStandard(standard, test));
    }

    [Fact]
    public void ResolverAppliesInheritanceAtLaunch()
    {
        var s = new AppSettings();
        var item = new LaunchItem { FullPath = @"C:\Scripts\x.ps1", Type = ItemType.PowerShell, RootFolder = @"C:\Scripts" };
        var standard = Standard();
        var server = new LaunchProfile { Name = "Server", RunAsAdmin = true, Own = new List<string> { ProfileInheritance.RunAs } };
        s.Entries.Add(new EntryConfig { Path = item.FullPath, Profiles = { standard, server } });

        var profiles = ProfileResolver.GetProfiles(s, item);
        var effective = profiles.Single(p => p.Name == "Server");
        Assert.True(effective.RunAsAdmin);
        Assert.Equal("Reset", effective.Parameters["Mode"].Value);
        Assert.Equal("Standard", ProfileResolver.GetDefault(s, item).Name);
    }

    [Fact]
    public void ProfileVariablesFillValuesPresetsAndChoices()
    {
        var vars = new Dictionary<string, ProfileVariable>(StringComparer.OrdinalIgnoreCase)
        {
            ["$Kunde"] = new() { Value = "Contoso AG" },
            ["Umgebung"] = new() { Kind = ProfileVariableKind.Fixed, Value = "Test" },
            ["Ticket"] = new() { Kind = ProfileVariableKind.Ask, Value = "4711" },
            ["Server"] = new() { Kind = ProfileVariableKind.Choice, Value = "srv-02", Choices = { "srv-01", "srv-02", "srv-03" } }
        };
        var text = "https://esm/{{$Kunde}}/{{Umgebung}}/{{Ticket}}?srv={{Server}}&k={{$Kunde}}&x={{Frei}}";
        var applied = ProfileVariables.Apply(text, vars, urlEncode: true);
        Assert.Equal("https://esm/Contoso AG/Test/{{Ticket:4711}}?srv={{Server:srv-02;srv-01;srv-03}}&k=Contoso%20AG&x={{Frei}}", applied);

        // Danach erledigt die normale Abfrage den Rest: Auswahl mit vorausgewähltem ersten Wert
        var asked = Placeholders.Find(applied);
        Assert.Equal(new[] { "Ticket", "Server", "Frei" }, asked.Select(p => p.Name));
        var server = asked.Single(p => p.Name == "Server");
        Assert.Equal("srv-02", server.Default);
        Assert.Equal(new[] { "srv-02", "srv-01", "srv-03" }, server.Choices);
        Assert.Equal("4711", asked.Single(p => p.Name == "Ticket").Default);

        // PowerShell-Code: Werte für {{$Variablen}} als sicherer String
        Assert.Equal("Write-Host 'O''Neil'", ProfileVariables.Apply("Write-Host '{{$Kunde}}'",
            new Dictionary<string, ProfileVariable> { ["$Kunde"] = new() { Value = "O'Neil" } }, powerShell: true));
    }

    [Fact]
    public void ChoicePlaceholderInTextOffersAListAndDefaultsToTheFirst()
    {
        var p = Assert.Single(Placeholders.Find("ping {{Server:srv-01; srv-02 ;srv-03}}"));
        Assert.Equal("srv-01", p.Default);
        Assert.Equal(new[] { "srv-01", "srv-02", "srv-03" }, p.Choices);
        Assert.Equal("ping srv-01", Placeholders.Apply("ping {{Server:srv-01;srv-02}}", new Dictionary<string, string>()));
        Assert.Equal("ping srv-02", Placeholders.Apply("ping {{Server:srv-01;srv-02}}", new Dictionary<string, string> { ["Server"] = "srv-02" }));
        // Ein einzelner Wert mit Strichpunkt am Ende ist keine Auswahl
        Assert.Empty(Assert.Single(Placeholders.Find("{{Pfad:C:;}}")).Choices);
    }

    [Fact]
    public void CustomEntryTakesTargetCodeAndVariablesFromTheProfile()
    {
        var entry = new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://esm/{{$Kunde}}/{{Ticket}}" };
        var profile = new LaunchProfile
        {
            TargetText = "https://esm-admin/{{$Kunde}}/{{Ticket}}",
            Variables = { ["$Kunde"] = new ProfileVariable { Value = "Contoso" }, ["Ticket"] = new ProfileVariable { Kind = ProfileVariableKind.Fixed, Value = "42" } }
        };
        Assert.Equal("https://esm-admin/Contoso/42", entry.WithProfile(profile).Target);
        Assert.Equal("https://esm/{{$Kunde}}/{{Ticket}}", entry.Target);       // Original unverändert

        var snippet = new CustomEntry { Kind = CustomEntryKind.Snippet, Code = "ping a" };
        Assert.Equal("ping b", snippet.WithProfile(new LaunchProfile { Code = "ping b" }).Code);
    }

    [Fact]
    public void ScriptParameterValuesUseTheProfilesVariables()
    {
        var p = new LaunchProfile
        {
            Parameters = { ["Domain"] = new ParameterSetting { Mode = ParameterMode.Fixed, Value = "{{$Kunde}}.local" } },
            Arguments = "/k {{$Kunde}}",
            Variables = { ["$Kunde"] = new ProfileVariable { Value = "contoso" } }
        };
        var applied = ProfileVariables.ApplyToValues(p);
        Assert.Equal("contoso.local", applied.Parameters["Domain"].Value);
        Assert.Equal("/k contoso", applied.Arguments);
        Assert.Equal("{{$Kunde}}.local", p.Parameters["Domain"].Value);       // gespeichertes Profil unverändert
    }
}
