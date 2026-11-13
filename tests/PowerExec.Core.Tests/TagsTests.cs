using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Tests;

/// <summary>Schlagworte: einlesen und abgleichen.</summary>
public class TagsTests
{
    [Fact]
    public void TagsParseAndMatch()
    {
        Assert.Equal(new[] { "AD", "Exchange" }, Tags.Parse("#AD, Exchange ad"));
        var (tags, text) = Tags.SplitQuery("#ex reset");
        Assert.Equal("reset", text);
        Assert.True(Tags.Matches(new[] { "Exchange" }, tags));
        Assert.False(Tags.Matches(new[] { "AD" }, tags));
    }
}
