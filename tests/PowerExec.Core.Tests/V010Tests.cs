using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Tests;

public class V010Tests
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
    public void SshCommandLines()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Ssh, Target = "srv", User = "root", Port = 2222 };
        var item = e.ToLaunchItem();
        Assert.Equal("wt.exe", item.LaunchTarget);
        Assert.Equal("ssh root@srv -p 2222", item.LaunchArguments);
        e.Client = SshClient.Putty;
        Assert.Equal("-ssh root@srv -P 2222", e.ToLaunchItem().LaunchArguments);
        Assert.Equal("SSH · root@srv:2222", e.SummaryText());
    }

    [Fact]
    public void BrowserChoiceBuildsArguments()
    {
        var e = new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://x", Browser = BrowserChoice.Edge, Private = true, BrowserProfile = "Profile 2" };
        var item = e.ToLaunchItem();
        Assert.Equal("msedge.exe", item.LaunchTarget);
        Assert.Equal("--profile-directory=\"Profile 2\" --inprivate https://x", item.LaunchArguments);
        Assert.Equal(ItemType.Executable, item.Type);
        Assert.Equal("x", new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://x/a" }.ToLaunchItem().DisplayName);
    }

    [Fact]
    public void ToolTypes()
    {
        Assert.Equal(ItemType.ConsoleSnapIn, new CustomEntry { Kind = CustomEntryKind.Tool, Target = "dsa.msc" }.ToLaunchItem().Type);
        Assert.Equal(ItemType.Other, new CustomEntry { Kind = CustomEntryKind.Tool, Target = "ms-settings:display" }.ToLaunchItem().Type);
        var cpl = new CustomEntry { Kind = CustomEntryKind.Tool, Target = "control.exe", Arguments = "ncpa.cpl" }.ToLaunchItem();
        Assert.Equal(ItemType.Executable, cpl.Type);
        Assert.Equal("ncpa.cpl", cpl.LaunchArguments);
    }

    [Fact]
    public void SnippetItems()
    {
        var ps = new CustomEntry { Kind = CustomEntryKind.Snippet, Code = "# Kommentar\nGet-Date" };
        var item = ps.ToLaunchItem();
        Assert.True(item.IsPowerShell);
        Assert.Equal(ps.SnippetPath, item.LaunchTarget);
        Assert.Equal("Get-Date", item.Synopsis);
        Assert.False(ps.CopiesToClipboard);
        Assert.True(new CustomEntry { Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text }.CopiesToClipboard);
        Assert.True(new CustomEntry { Kind = CustomEntryKind.Snippet, Action = SnippetAction.Copy }.CopiesToClipboard);
    }

    [Fact]
    public void InfoTextIsNotRepeated()
    {
        var item = new CustomEntry { Kind = CustomEntryKind.Url, Name = "Google", Target = "https://www.google.de/index.html" }.ToLaunchItem();
        Assert.Equal("https://www.google.de/index.html", item.Synopsis);
        Assert.Equal(string.Empty, item.Description);
    }

    [Fact]
    public void TagsParseAndMatch()
    {
        Assert.Equal(new[] { "AD", "Exchange" }, Tags.Parse("#AD, Exchange ad"));
        var (tags, text) = Tags.SplitQuery("#ex reset");
        Assert.Equal("reset", text);
        Assert.True(Tags.Matches(new[] { "Exchange" }, tags));
        Assert.False(Tags.Matches(new[] { "AD" }, tags));
    }

    [Fact]
    public void ClipboardSuggestions()
    {
        Assert.Equal(CustomEntryKind.Url, ClipboardClassifier.Suggest("https://a.b", _ => false, _ => false).Kind);
        Assert.Equal(CustomEntryKind.Folder, ClipboardClassifier.Suggest(@"\\srv\share", _ => true, _ => false).Kind);
        Assert.Equal(CustomEntryKind.File, ClipboardClassifier.Suggest(@"C:\x\a.ps1", _ => false, _ => true).Kind);
        Assert.Equal(CustomEntryKind.Rdp, ClipboardClassifier.Suggest("SRV-01", _ => false, _ => false).Kind);
        var ssh = ClipboardClassifier.Suggest("ssh admin@host", _ => false, _ => false);
        Assert.Equal((CustomEntryKind.Ssh, "host", "admin"), (ssh.Kind, ssh.Target, ssh.User));
        Assert.Equal(CustomEntryKind.Snippet, ClipboardClassifier.Suggest("Get-Date\nGet-Process", _ => false, _ => false).Kind);
    }

    [Fact]
    public void ShareRoundTrip()
    {
        var s = new AppSettings();
        GroupManager.Create(s, "Web");
        GroupManager.SetIcon(s, "Web", IconRef.Parse("hero:globe-alt"));
        var e = new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://wiki" };
        s.CustomEntries.Add(e);
        GroupManager.Assign(s, e.Key, "Web");
        Tags.Set(s, e.Key, new[] { "doku" });
        var json = EntryShare.Export(s, s.CustomEntries);

        var other = new AppSettings();
        var (added, updated) = EntryShare.Import(other, EntryShare.Parse(json), "Kollegen");
        Assert.Equal((1, 0), (added, updated));
        Assert.Equal("Kollegen/Web", GroupManager.GroupOf(other, e.Key));
        Assert.Equal("hero:globe-alt", GroupManager.IconOf(other, "Kollegen/Web").ToString());
        Assert.Equal(new[] { "doku" }, Tags.Of(other, e.Key));
        Assert.Equal((0, 1), EntryShare.Import(other, EntryShare.Parse(json), "Kollegen"));
    }

    [Fact]
    public void GroupIconsFollowRename()
    {
        var s = new AppSettings();
        GroupManager.Create(s, "A");
        GroupManager.Create(s, "B", "A");
        GroupManager.SetIcon(s, "A/B", IconRef.Parse("fluent:server"));
        GroupManager.Rename(s, "A", "X");
        Assert.Equal("fluent:server", GroupManager.IconOf(s, "X/B").ToString());
        GroupManager.Delete(s, "X");
        Assert.Empty(s.GroupIcons);
    }

    [Fact]
    public void IconRefParse()
    {
        Assert.Equal(IconSource.Hero, IconRef.Parse("hero:server").Source);
        Assert.Equal(@"C:\a.png", IconRef.Parse(@"file:C:\a.png").Value);
        Assert.True(IconRef.Parse("quatsch").IsEmpty);
    }

    [Fact]
    public void CatalogIsValid()
    {
        Assert.True(ToolCatalog.All.Count > 50);
        foreach (var t in ToolCatalog.All)
        {
            var e = ToolCatalog.Create(t);
            Assert.False(string.IsNullOrWhiteSpace(e.Target + e.Code), t.Name);
            Assert.False(IconRef.Parse(t.Icon).IsEmpty, t.Name);
        }
    }

    [Fact]
    public void LayoutMigration09()
    {
        var p = new PanelConfig { Layout = ItemLayout.Compact };
        p.Normalize();
        Assert.Equal(ItemLayout.List, p.Layout);
        Assert.Equal(ItemSize.Small, p.ItemSize);
    }
}
