using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Tests;

/// <summary>Zwischenablage: Vorschlag für einen Eintrag (URL, Pfad, Rechner, Code).</summary>
public class ClipboardClassifierTests
{
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
}
