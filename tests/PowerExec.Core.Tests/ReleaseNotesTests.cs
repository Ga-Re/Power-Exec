using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Neuerungen seit der zuletzt gesehenen Version.</summary>
public class ReleaseNotesTests
{
    [Fact]
    public void ReleaseNotesSinceLastSeenVersion()
    {
        var current = new Version(0, 16, 0);
        Assert.NotEmpty(PowerExec.Core.Updates.ReleaseNotes.Since(new Version(0, 15, 0), current));
        Assert.NotEmpty(PowerExec.Core.Updates.ReleaseNotes.Since(null, current));
        Assert.Empty(PowerExec.Core.Updates.ReleaseNotes.Since(new Version(0, 16, 0), current));
        Assert.Empty(PowerExec.Core.Updates.ReleaseNotes.Since(new Version(0, 14, 0), new Version(0, 15, 2)));
        Assert.Equal(new Version(0, 16, 0), PowerExec.Core.Updates.ReleaseNotes.Parse("0.16.0"));
        Assert.Null(PowerExec.Core.Updates.ReleaseNotes.Parse(""));
    }
}
