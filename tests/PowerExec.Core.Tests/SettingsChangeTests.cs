using PowerExec.Core.Config;
using Xunit;

namespace PowerExec.Core.Tests;

public class SettingsChangeTests
{
    // Ein Panel trägt eine eigene Kennung: beide Stände gehen vom selben Panel aus
    private static readonly PanelConfig Panel = new() { Name = "Haupt" };

    private static AppSettings Sample()
    {
        var s = new AppSettings();
        s.Folders.Add(new FolderConfig { Path = @"C:\Scripts" });
        s.Panels.Add(Panel.Clone());
        return s;
    }

    [Fact]
    public void IdenticalSettingsChangeNothing()
    {
        var change = SettingsChange.Between(Sample(), Sample());
        Assert.Equal(new SettingsChange(false, false, false, false), change);
    }

    [Fact]
    public void FolderFilterCountsAsFolderChangeOnly()
    {
        var after = Sample();
        after.Folders[0].Excludes.Add("*.bak");
        Assert.Equal(new SettingsChange(Folders: true, Panels: false, Appearance: false, Autostart: false), SettingsChange.Between(Sample(), after));
    }

    [Fact]
    public void PinningAPanelIsNotAPanelChange()
    {
        var after = Sample();
        after.Panels[0].Pinned = true;
        Assert.False(SettingsChange.Between(Sample(), after).Panels);
        after.Panels[0].PanelWidth += 40;
        Assert.True(SettingsChange.Between(Sample(), after).Panels);
    }

    [Fact]
    public void CornersAndAutostartAreReported()
    {
        var after = Sample();
        after.Corners = CornerStyle.Square;
        after.StartWithWindows = !after.StartWithWindows;
        var change = SettingsChange.Between(Sample(), after);
        Assert.True(change.Appearance);
        Assert.True(change.Autostart);
        Assert.False(change.Folders);
    }
}
