using System.Globalization;
using System.Text.Json;
using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

/// <summary>Dynamische Rechnergruppen: Regeln → LDAP-Filter.</summary>
public class AdRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private static AdRule R(string a, AdOperator op, string v = "") => new() { Attribute = a, Operator = op, Value = v };

    [Fact]
    public void SingleBlockIsAnd()
    {
        var q = new AdGroupQuery { Blocks = { new AdRuleBlock { Rules = { R("name", AdOperator.StartsWith, "SRV-"), R("operatingSystem", AdOperator.Contains, "Server") } } } };
        Assert.Equal("(&(objectCategory=computer)(&(name=SRV-*)(operatingSystem=*Server*)))", AdRules.BuildFilter(q, Now));
    }

    [Fact]
    public void BlocksAreOr()
    {
        var q = new AdGroupQuery
        {
            Blocks =
            {
                new AdRuleBlock { Rules = { R("name", AdOperator.StartsWith, "WEB") } },
                new AdRuleBlock { Rules = { R("description", AdOperator.Equals, "IIS"), R("location", AdOperator.NotEquals, "Lab") } }
            }
        };
        Assert.Equal("(&(objectCategory=computer)(|(name=WEB*)(&(description=IIS)(!(location=Lab)))))", AdRules.BuildFilter(q, Now));
    }

    [Fact]
    public void ValuesAreEscaped()
    {
        var q = new AdGroupQuery { Blocks = { new AdRuleBlock { Rules = { R("description", AdOperator.Equals, "a*(b)\\c") } } } };
        Assert.Equal("(&(objectCategory=computer)(description=a\\2a\\28b\\29\\5cc))", AdRules.BuildFilter(q, Now));
    }

    [Fact]
    public void PseudoAttributes()
    {
        var enabled = AdRules.BuildFilter(new AdGroupQuery { Blocks = { new AdRuleBlock { Rules = { R(AdRules.Enabled, AdOperator.Equals, "Ja") } } } }, Now);
        Assert.Contains("(!(userAccountControl:1.2.840.113556.1.4.803:=2))", enabled);
        var disabled = AdRules.BuildFilter(new AdGroupQuery { Blocks = { new AdRuleBlock { Rules = { R(AdRules.Enabled, AdOperator.Equals, "nein") } } } }, Now);
        Assert.Contains("(userAccountControl:1.2.840.113556.1.4.803:=2)", disabled);
        Assert.DoesNotContain("(!", disabled);

        var ft = Now.AddDays(-30).ToFileTimeUtc().ToString(CultureInfo.InvariantCulture);
        var recent = AdRules.BuildFilter(new AdGroupQuery { Blocks = { new AdRuleBlock { Rules = { R(AdRules.LastLogonDays, AdOperator.AtMost, "30") } } } }, Now);
        Assert.Contains($"(lastLogonTimestamp>={ft})", recent);
        var stale = AdRules.BuildFilter(new AdGroupQuery { Blocks = { new AdRuleBlock { Rules = { R(AdRules.LastLogonDays, AdOperator.AtLeast, "30") } } } }, Now);
        Assert.Contains($"(lastLogonTimestamp<={ft})", stale);
        Assert.Contains("(!(lastLogonTimestamp=*))", stale);

        var member = AdRules.BuildFilter(new AdGroupQuery { Blocks = { new AdRuleBlock { Rules = { R(AdRules.MemberOf, AdOperator.Equals, "CN=Web,DC=x") } } } }, Now);
        Assert.Contains("(memberOf:1.2.840.113556.1.4.1941:=CN=Web,DC=x)", member);
    }

    [Fact]
    public void Validation()
    {
        Assert.NotNull(AdRules.Validate(new AdGroupQuery()));
        Assert.NotNull(AdRules.Validate(new AdGroupQuery { Blocks = { new AdRuleBlock { Rules = { R("name", AdOperator.Equals, "") } } } }));
        Assert.Null(AdRules.Validate(new AdGroupQuery { Blocks = { new AdRuleBlock { Rules = { R("name", AdOperator.Exists) } } } }));
        Assert.NotNull(AdRules.Validate(R(AdRules.LastLogonDays, AdOperator.AtMost, "viele")));
        Assert.NotNull(AdRules.Validate(R(AdRules.Enabled, AdOperator.Contains, "Ja")));
        Assert.NotNull(AdRules.Validate(R("na me", AdOperator.Equals, "x")));
        Assert.NotNull(AdRules.Validate(new AdGroupQuery { SearchBase = "Server", Blocks = { new AdRuleBlock { Rules = { R("name", AdOperator.Exists) } } } }));
        Assert.Null(AdRules.Validate(R("extensionAttribute1", AdOperator.Equals, "x"))); // eigenes Attribut
    }

    [Fact]
    public void DueAndSerialization()
    {
        var g = new MachineGroup { Name = "Web", AdQuery = new AdGroupQuery { Blocks = { new AdRuleBlock { Rules = { R("name", AdOperator.StartsWith, "WEB") } } } } };
        Assert.True(AdRules.IsDue(g, 4, Now));
        g.LastSyncUtc = Now.AddHours(-1);
        Assert.False(AdRules.IsDue(g, 4, Now));
        Assert.True(AdRules.IsDue(g, 1, Now));
        Assert.False(AdRules.IsDue(new MachineGroup { Name = "fest" }, 4, Now));

        var json = JsonSerializer.Serialize(g);
        var back = JsonSerializer.Deserialize<MachineGroup>(json)!;
        Assert.True(back.IsDynamic);
        Assert.Equal(AdOperator.StartsWith, back.AdQuery!.Blocks[0].Rules[0].Operator);
        Assert.Equal("Name beginnt mit WEB", AdRules.Describe(back.AdQuery));
    }
}
