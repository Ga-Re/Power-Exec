using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Scanning;
using PowerExec.Core.Search;
using System.Drawing;

namespace PowerExec.Core.Tests;

/// <summary>Zone.Identifier („aus dem Internet“) lesen.</summary>
public class ZoneIdentifierTests
{
    [Fact]
    public void ZoneIdentifierParsing()
    {
        var z = ZoneIdentifier.ParseZoneId("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.com/\r\n");
        Assert.Equal(3, z);
        Assert.True(ZoneIdentifier.IsBlocked(z));
        Assert.False(ZoneIdentifier.IsBlocked(ZoneIdentifier.ParseZoneId("[ZoneTransfer]\nZoneId=1")));
        Assert.False(ZoneIdentifier.IsBlocked(null));
    }
}
