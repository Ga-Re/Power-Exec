using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

public class TagSuggestionTests
{
    [Fact]
    public void RecentTagsComeFirstThenAllOthersAlphabetically()
    {
        var s = new AppSettings();
        s.Entries.Add(new EntryConfig { Path = "a", Tags = { "server", "admin" } });
        s.Entries.Add(new EntryConfig { Path = "b", Tags = { "netz", "Backup" } });
        Assert.Equal(new[] { "admin", "Backup", "netz", "server" }, Tags.Suggested(s));

        Tags.Remember(s, new[] { "netz" });
        Tags.Remember(s, new[] { "server", "NETZ" });
        Assert.Equal(new[] { "server", "NETZ" }, s.RecentTags);                     // neueste zuerst, ohne Doppelte
        Assert.Equal(new[] { "server", "NETZ", "admin", "Backup" }, Tags.Suggested(s));

        Tags.Remember(s, Enumerable.Range(0, 20).Select(i => "t" + i));
        Assert.Equal(Tags.RecentCount, s.RecentTags.Count);
    }
}
