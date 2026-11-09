using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

/// <summary>Eigene und eingebaute {{$Variablen}}.</summary>
[Collection("Placeholders")] // setzt den statischen Resolver
public class VariablesTests : IDisposable
{
    private readonly AppSettings _s = new() { ActiveWorkspace = "Kunde A" };
    private int _psRuns;
    private DateTime _now = new(2026, 11, 9, 14, 5, 0);
    private readonly VariableResolver _resolver;

    public VariablesTests()
    {
        _s.Variables.Add(new UserVariable { Name = "CompanyName", Value = "Med360°" });
        _s.Variables.Add(new UserVariable { Name = "Domain", Kind = VariableKind.Workspace, Value = "standard.local", WorkspaceValues = { ["Kunde A"] = "kunde-a.local" } });
        _s.Variables.Add(new UserVariable { Name = "Token", Kind = VariableKind.Secret, Value = "enc:geheim" });
        _s.Variables.Add(new UserVariable { Name = "Pfad", Kind = VariableKind.Environment, Value = "%PE_TEST_VAR%" });
        _s.Variables.Add(new UserVariable { Name = "AdDomain", Kind = VariableKind.PowerShell, Value = "(Get-ADDomain).DNSRoot", Refresh = VariableRefresh.Cached, CacheMinutes = 30 });
        _resolver = new VariableResolver(() => _s, cmd => { _psRuns++; return $" firma.local{_psRuns} \n"; },
            enc => enc.Replace("enc:", ""), () => "aus der Ablage", () => _now);
        Placeholders.Resolver = _resolver.Resolve;
        Environment.SetEnvironmentVariable("PE_TEST_VAR", @"C:\Daten");
    }

    public void Dispose() => Placeholders.Resolver = null;

    [Fact]
    public void ResolvesAllKinds()
    {
        Assert.Equal("Med360°", _resolver.Resolve("$CompanyName"));
        Assert.Equal("Med360°", _resolver.Resolve("companyname"));
        Assert.Equal("kunde-a.local", _resolver.Resolve("Domain"));
        _s.ActiveWorkspace = "Kunde B";
        Assert.Equal("standard.local", _resolver.Resolve("Domain"));
        Assert.Equal("geheim", _resolver.Resolve("Token"));
        Assert.Equal(@"C:\Daten", _resolver.Resolve("Pfad"));
        Assert.Equal("aus der Ablage", _resolver.Resolve("Zwischenablage"));
        Assert.Null(_resolver.Resolve("Gibtsnicht"));
    }

    [Fact]
    public void DateAndTimeUseConfiguredFormat()
    {
        Assert.Equal("09.11.2026", _resolver.Resolve("Datum"));
        Assert.Equal("14:05", _resolver.Resolve("Uhrzeit"));
        _s.DateFormat = "yyyy-MM-dd";
        _s.TimeFormat = "HH.mm.ss";
        Assert.Equal("2026-11-09", _resolver.Resolve("$Datum"));
        Assert.Equal("14.05.00", _resolver.Resolve("$Uhrzeit"));
        _s.DateFormat = "%"; // ungültig: Standardformat
        Assert.Equal("09.11.2026", _resolver.Resolve("$Datum"));
    }

    [Fact]
    public void PowerShellVariableHonorsRefreshMode()
    {
        Assert.Equal("firma.local1", _resolver.Resolve("AdDomain"));
        Assert.Equal("firma.local1", _resolver.Resolve("AdDomain")); // zwischengespeichert
        _now = _now.AddMinutes(31);
        Assert.Equal("firma.local2", _resolver.Resolve("AdDomain")); // abgelaufen

        var v = Variables.Find(_s, "AdDomain")!;
        v.Refresh = VariableRefresh.EveryUse;
        _resolver.Resolve("AdDomain");
        _resolver.Resolve("AdDomain");
        Assert.Equal(4, _psRuns);

        v.Refresh = VariableRefresh.Session;
        var once = _resolver.Resolve("AdDomain");
        _now = _now.AddDays(1);
        Assert.Equal(once, _resolver.Resolve("AdDomain"));

        v.Value = "anderer Befehl"; // geänderter Befehl: neu ausführen
        Assert.NotEqual(once, _resolver.Resolve("AdDomain"));
        Assert.Equal("••••••••", _resolver.Preview("Token"));
    }

    [Fact]
    public void EntriesReplaceVariablesAndAskOnlyPlaceholders()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://{{$Domain}}/suche?q={{Begriff}}&von={{$CompanyName}}" };
        Assert.Equal(new[] { "Begriff" }, e.Placeholders.Select(p => p.Name));
        Assert.True(e.NeedsResolving);
        var r = e.WithValues(new Dictionary<string, string> { ["Begriff"] = "a b" });
        Assert.Equal("https://kunde-a.local/suche?q=a%20b&von=Med360%C2%B0", r.Target);

        var ps = new CustomEntry { Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.PowerShell, Code = "Write-Host '{{$Firma}} {{$CompanyName}}'" };
        _s.Variables.Add(new UserVariable { Name = "Firma", Value = "O'Brien & Co" });
        Assert.Equal("Write-Host 'O''Brien & Co Med360°'", ps.WithValues(new Dictionary<string, string>()).Code);

        var text = new CustomEntry { Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text, Code = "Grüße von {{$Firma}}" };
        Assert.Equal("Grüße von O'Brien & Co", text.WithValues(new Dictionary<string, string>()).Code);
    }

    [Fact]
    public void UnknownVariablesAreReportedAndKept()
    {
        Assert.Equal(new[] { "$Tippfehler" }, Variables.Unknown(_s, "{{$CompanyName}} {{$Tippfehler}} {{Frage}} {{$Datum}}"));
        Assert.Equal("x {{$Tippfehler}}", Placeholders.Apply("x {{$Tippfehler}}", new Dictionary<string, string>()));
    }

    [Fact]
    public void NamesAreValidated()
    {
        Assert.Null(Variables.ValidateName(_s, "$NeueVar"));
        Assert.NotNull(Variables.ValidateName(_s, "Datum"));
        Assert.NotNull(Variables.ValidateName(_s, "companyname"));
        Assert.NotNull(Variables.ValidateName(_s, "mit leer"));
        Assert.NotNull(Variables.ValidateName(_s, "1abc"));
        Assert.Null(Variables.ValidateName(_s, "CompanyName", Variables.Find(_s, "CompanyName")));
        Assert.Contains("{{$CompanyName}}", Variables.Tokens(_s));
        Assert.Equal("{{$Datum}}", Variables.Tokens(_s)[0]);
    }

    [Fact]
    public void ScriptParametersGetVariables()
    {
        var p = Placeholders.ApplyVariables(new Dictionary<string, object> { ["Firma"] = "{{$CompanyName}}", ["Anzahl"] = 3, ["Liste"] = new[] { "{{$Domain}}", "x" } });
        Assert.Equal("Med360°", p["Firma"]);
        Assert.Equal(3, p["Anzahl"]);
        Assert.Equal(new[] { "kunde-a.local", "x" }, (string[])p["Liste"]);
    }

    [Fact]
    public void VariablesSurviveJson()
    {
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(_s))!;
        Assert.Equal(5, back.Variables.Count);
        Assert.Equal("kunde-a.local", VariableResolver.WorkspaceValue(Variables.Find(back, "Domain")!, "kunde a"));
    }
}
