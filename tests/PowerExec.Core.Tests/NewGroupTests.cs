using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

public class NewGroupTests
{
    [Fact]
    public void PlainGroupWithIconColorAndLayout()
    {
        var s = new AppSettings();
        var path = NewGroup.Create(s, new NewGroupSpec("Kunde A", IconRef.Parse("fluent:people_team"), null, "#22C55E", ItemLayout.Grid));
        Assert.Equal("Kunde A", path);
        Assert.Contains("Kunde A", GroupManager.All(s));
        Assert.Equal("fluent:people_team", GroupManager.IconOf(s, path).ToString());
        Assert.Equal("#22C55E", GroupManager.OwnColorOf(s, path), ignoreCase: true);
        Assert.Equal(ItemLayout.Grid, GroupManager.LayoutOf(s, path));
        Assert.Empty(s.Folders);
    }

    [Fact]
    public void WithFolderBecomesSmartGroupUnderParent()
    {
        var s = new AppSettings();
        GroupManager.Create(s, "Kunde A");
        var path = NewGroup.Create(s, new NewGroupSpec("Scripte", IconRef.None, @"C:\Scripte\KundeA", string.Empty, null), "Kunde A");
        Assert.Equal("Kunde A/Scripte", path);
        var folder = Assert.Single(s.Folders);
        Assert.Equal((@"C:\Scripte\KundeA", "Kunde A/Scripte"), (folder.Path, folder.Group));
        Assert.True(GroupManager.IconOf(s, path).IsEmpty);
        Assert.Null(GroupManager.LayoutOf(s, path));
    }

    [Fact]
    public void NewTopLevelGroupGoesToTheEnd()
    {
        var s = new AppSettings();
        GroupManager.Create(s, "A");
        GroupManager.Create(s, "B", "A");
        GroupManager.Create(s, "C");
        NewGroup.Create(s, new NewGroupSpec("Neu", IconRef.None, null, string.Empty, null));
        Assert.Equal("A|A/B|C|Neu", string.Join("|", GroupManager.All(s)));
    }

    [Fact]
    public void FolderAlreadyUsedIsReported()
    {
        var folders = new[] { new FolderConfig { Path = @"C:\Scripte\", Group = "Server/Scripte" } };
        Assert.Equal("Dieser Ordner gehört bereits zur Gruppe „Scripte“.", NewGroup.FolderError(folders, @"c:\scripte"));
        Assert.Null(NewGroup.FolderError(folders, @"C:\Anderes"));
        Assert.Null(NewGroup.FolderError(folders, null));
    }

    [Theory]
    [InlineData(@"C:\Scripte\AD\", "AD")]
    [InlineData(@"\\fs01\Freigabe", "Freigabe")]
    [InlineData(@"D:\", "D")]
    public void NameIsSuggestedFromTheFolder(string folder, string expected) =>
        Assert.Equal(expected, NewGroup.NameFromFolder(folder));
}
