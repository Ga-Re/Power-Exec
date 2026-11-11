using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Tests;

/// <summary>Einträge und Gruppen exportieren und importieren.</summary>
public class EntryShareTests
{
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
}
