using PowerExec.Core.Config;
using PowerExec.ViewModels;

namespace PowerExec.UI.Core.Tests;

public class MarketplaceViewModelTests
{
    private const string DefaultIndex = "https://raw.githubusercontent.com/Ga-Re/Power-Exec/main/marketplace/index.json";
    private const string TeamIndex = "https://raw.githubusercontent.com/firma/px-katalog/main/index.json";

    private static string Entries(string id, string name, string target) => System.Text.Json.JsonSerializer.Serialize(new EntryShare.SharedFile
    {
        Groups = { "Team" },
        Entries = { new EntryShare.SharedEntry { Group = "Team", Entry = new CustomEntry { Id = id, Name = name, Kind = CustomEntryKind.Url, Target = target } } }
    });

    private static FakeEntryHost Host()
    {
        var host = new FakeEntryHost();
        host.Downloads[DefaultIndex] = """{ "Name": "Power-Exec", "Items": [ { "Id": "g", "Type": "Entry", "Name": "Google", "File": "entries/g.json" } ] }""";
        host.Downloads["https://raw.githubusercontent.com/Ga-Re/Power-Exec/main/marketplace/entries/g.json"] = Entries("mk-g", "Google", "https://google.com/search?q={{Q}}");
        host.Downloads[TeamIndex] = """{ "Name": "Team-Katalog", "Items": [ { "Id": "wiki", "Type": "Entry", "Name": "Wiki", "Version": "3", "File": "wiki.json" } ] }""";
        host.Downloads["https://raw.githubusercontent.com/firma/px-katalog/main/wiki.json"] = Entries("team-wiki", "Wiki", "https://wiki.firma.local");
        return host;
    }

    [Fact]
    public async Task AddSourceAskTrustOnceAndInstall()
    {
        var host = Host();
        var s = new AppSettings();
        var changes = 0;
        var vm = new MarketplaceViewModel(host, s, () => changes++);
        await vm.LoadAsync();
        Assert.Contains(vm.Items, i => i.Name == "Google");
        Assert.False(vm.HasSeveralSources);

        // ungültige bzw. doppelte Adresse wird abgelehnt
        host.FakeDialogs.Answer = "keine url";
        vm.AddSourceCommand.Execute(null);
        Assert.Empty(s.MarketplaceSources);

        host.FakeDialogs.Answer = "https://github.com/firma/px-katalog";
        vm.AddSourceCommand.Execute(null);
        for (int i = 0; i < 50 && vm.IsLoading; i++) await Task.Delay(20);
        var src = Assert.Single(s.MarketplaceSources);
        Assert.False(src.Trusted);
        Assert.Equal("Team-Katalog", src.Name);
        Assert.True(vm.HasSeveralSources);
        Assert.Equal(2, vm.SourceList.Count);
        Assert.False(vm.SourceList[1].Trusted);
        var wiki = Assert.Single(vm.Items, i => i.Name == "Wiki");
        Assert.Contains("aus Team-Katalog", wiki.Details);

        vm.SourceFilter = TeamIndex.Length > 0 ? src.Url : "*";
        Assert.Single(vm.Items);
        vm.SourceFilter = "*";

        // Vertrauen abgelehnt: nichts installiert
        host.FakeDialogs.ConfirmAnswer = false;
        await vm.InstallAsync(wiki);
        Assert.Empty(s.CustomEntries);
        Assert.False(src.Trusted);

        // bestätigt: Quelle vertrauenswürdig, Eintrag installiert
        host.FakeDialogs.ConfirmAnswer = true;
        await vm.InstallAsync(wiki);
        Assert.True(src.Trusted);
        Assert.Contains(s.CustomEntries, e => e.Id == "team-wiki");
        Assert.True(wiki.IsInstalled);
        Assert.Equal("3", Marketplace.FindInstall(s, src.Url, "wiki")!.Version);
        Assert.True(changes >= 2);

        // die mitgelieferte Quelle fragt nie nach Vertrauen
        var asked = host.FakeDialogs.Messages.Count;
        await vm.InstallAsync(vm.Items.Single(i => i.Name == "Google"));
        Assert.Contains(s.CustomEntries, e => e.Id == "mk-g");

        // entfernen: Quelle weg, Installiertes bleibt
        vm.SourceList.Single(x => !x.IsDefault).RemoveCommand.Execute(null);
        Assert.Empty(s.MarketplaceSources);
        Assert.DoesNotContain(vm.Items, i => i.Name == "Wiki");
        Assert.Contains(s.CustomEntries, e => e.Id == "team-wiki");
        Assert.False(vm.SourceList.Single().CanRemove);
    }

    [Fact]
    public async Task UnreachableSourceDoesNotBlockOthers()
    {
        var host = Host();
        var s = new AppSettings { MarketplaceSources = { new MarketplaceSource { Url = "https://github.com/firma/weg", Name = "Weg" } } };
        var vm = new MarketplaceViewModel(host, s, () => { });
        await vm.LoadAsync();
        Assert.Contains(vm.Items, i => i.Name == "Google");
        Assert.Equal("Warning", vm.StatusSeverity);
        Assert.Contains("nicht erreichbar", vm.Status);
        Assert.True(vm.SourceList.Single(x => x.Name == "Weg").HasError);
    }
}
