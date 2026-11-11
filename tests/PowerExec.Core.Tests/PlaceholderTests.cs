using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;

namespace PowerExec.Core.Tests;

/// <summary>Platzhalter {{Name}} in Adresse, Argumenten und Code: finden, abfragen, einsetzen.</summary>
public class PlaceholderTests
{
    [Fact]
    public void PlaceholdersAreFoundAndApplied()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://t/{{Ticket}}?q={{Suche:alle offen}}" };
        var ph = e.Placeholders;
        Assert.Equal(2, ph.Count);
        Assert.Equal("alle offen", ph[1].Default);
        var applied = e.WithValues(new Dictionary<string, string> { ["ticket"] = "A 1" });
        Assert.Equal("https://t/A%201?q=alle%20offen", applied.Target);
        // PowerShell-Klammern stören nicht
        Assert.Empty(Placeholders.Find("Get-Process | Where-Object { $_.CPU -gt 10 }"));
    }

    [Fact]
    public void BuiltinVariablesAreNotAsked()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text, Code = "Gruß {{$User}} am {{$Date}} – Ticket {{Nr}}" };
        Assert.Single(e.Placeholders);
        Assert.True(e.NeedsResolving);
        var resolved = e.WithValues(new Dictionary<string, string> { ["Nr"] = "42" });
        Assert.Contains(Environment.UserName, resolved.Code);
        Assert.Contains("Ticket 42", resolved.Code);
        Assert.DoesNotContain("{{", resolved.Code);
        Assert.False(new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://x" }.NeedsResolving);
    }
}
