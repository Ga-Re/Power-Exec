using PowerExec.Core.Config;
using PowerExec.Core.Updates;
using PowerExec.Core.Models;
using PowerExec.Infrastructure;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec.UI.Core.Tests;

/// <summary>App-Ersatz für Eintrags-, Profil-Dialog und die Liste „Eigene Einträge“.</summary>
internal sealed class FakeEntryHost : ISettingsWindowHost
{
    private static readonly FakeUiDispatcher Ui = new();

    public AppSettings Settings { get; } = new();
    public FakeShell FakeShell { get; } = new();
    public FakeDialogs FakeDialogs { get; } = new();
    public IShell Shell => FakeShell;
    public IDialogs Dialogs => FakeDialogs;
    public IIconProvider IconProvider { get; } = new FakeIcons();
    public IUiDispatcher UiDispatcher => Ui;
    public HealthService Health { get; } = new(Ui);
    public object? SettingsDialogOwner => null;
    public IThemeInfo Theme { get; } = new FakeTheme();
    public FakeItemSource Items { get; } = new();
    public IItemSource Repository => Items;
    public List<string> AdSyncs { get; } = new();
    public IReadOnlyList<string> AdAnswer { get; set; } = Array.Empty<string>();

    public Task<IReadOnlyList<string>?> SyncMachineGroupAsync(MachineGroup group)
    {
        AdSyncs.Add(group.Name);
        group.LastSyncUtc = DateTime.UtcNow;
        return Task.FromResult<IReadOnlyList<string>?>(AdAnswer);
    }

    public Task<IReadOnlyList<string>> FindComputersAsync(string query) => Task.FromResult(AdAnswer);

    public List<(CustomEntry Entry, LaunchProfile Profile)> TestRuns { get; } = new();
    public int ProfilesSaved { get; private set; }
    public EntryEditorResult EditorAnswer { get; set; } = new(null, null, null);
    public List<string> Calls { get; } = new();

    public string ToUnc(string path) => path;
    public void TestRun(CustomEntry entry, LaunchProfile profile) => TestRuns.Add((entry, profile));
    public void OnProfilesSaved() => ProfilesSaved++;

    public EntryEditorResult EditEntryModal(AppSettings target, CustomEntry? entry, bool isNew, string? group, object? owner)
    {
        Calls.Add($"EditEntryModal {(entry == null ? "neu" : entry.Kind.ToString())} {group}");
        return EditorAnswer;
    }

    public void ExportEntries(IReadOnlyList<CustomEntry> entries, string? rootGroup, object? owner, AppSettings? source) =>
        Calls.Add($"ExportEntries {entries.Count}");

    public void CreateSmartGroupFor(string folderPath, string? parent) => Calls.Add($"CreateSmartGroupFor {folderPath}");

    // ── Einstellungsfenster ──────────────────────────────────────────
    public UpdateState Update { get; } = new();
    public List<AppSettings> Applied { get; } = new();
    public void SaveSettings() => Calls.Add("SaveSettings");
    public string? ApplySettings(AppSettings updated) { Applied.Add(updated); return null; }
    public string SettingsFilePath => @"C:\Users\test\AppData\Local\Power-Exec\settings.json";
    public Task CheckCatalogsAsync(bool force = false) { Calls.Add("CheckCatalogs"); return Task.CompletedTask; }
    public string? SubscribeAnswer { get; set; }
    public Task<string?> SubscribeCatalogAsync(string path, string? name = null) { Calls.Add("Subscribe " + path); return Task.FromResult(SubscribeAnswer); }
    public void UnsubscribeCatalog(CatalogSubscription subscription) => Calls.Add("Unsubscribe " + subscription.Name);
    public Task ApplyPendingCatalogsAsync() => Task.CompletedTask;
    public ReleaseInfo? AvailableUpdate { get; set; }
    public string? UpdateError { get; set; }
    public DateTime? LastUpdateCheck { get; set; }
    public ReleaseInfo? CheckAnswer { get; set; }
    public Task<ReleaseInfo?> CheckUpdatesAsync(bool manual, bool includePrereleases) { Calls.Add("CheckUpdates"); AvailableUpdate = CheckAnswer; return Task.FromResult(CheckAnswer); }
    public Task InstallUpdateAsync() { Calls.Add("InstallUpdate"); return Task.CompletedTask; }
    public string CurrentVersionText => "0.19.5";
    public bool IsInstalledViaSetup => false;
    public void ShowWelcome() => Calls.Add("ShowWelcome");
    public IReadOnlyList<KeyValuePair<int, string>> Monitors { get; } = new[] { new KeyValuePair<int, string>(0, "Monitor 1 (Hauptmonitor)") };
    public bool IsMicaSupported => true;
    public void ShowProfilesDialog(ProfilesViewModel vm, object? owner) => Calls.Add("ShowProfilesDialog " + vm.Item.FullPath);
}

public class CustomEntryViewModelTests
{
    [Fact]
    public void UrlNeedsTargetToBeValid()
    {
        var vm = new CustomEntryViewModel(new CustomEntry { Kind = CustomEntryKind.Url }, new FakeEntryHost());
        Assert.False(vm.IsValid);
        Assert.NotNull(vm.FirstError);

        var changed = 0;
        vm.Changed += () => changed++;
        vm.Target = "https://intranet.example/";

        Assert.True(vm.IsValid);
        Assert.Null(vm.FirstError);
        Assert.True(changed > 0);
    }

    [Fact]
    public void IconComesFromPickerAndCanBeCleared()
    {
        var host = new FakeEntryHost();
        var vm = new CustomEntryViewModel(new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://a.example" }, host);

        host.FakeDialogs.IconAnswer = null;
        vm.ChooseIconCommand.Execute(null);
        Assert.False(vm.HasIcon);

        host.FakeDialogs.IconAnswer = IconRef.Parse("glyph:E8B7");
        vm.ChooseIconCommand.Execute(null);
        Assert.True(vm.HasIcon);
        Assert.Equal(IconRef.Parse("glyph:E8B7").ToString(), vm.Icon);

        vm.ClearIconCommand.Execute(null);
        Assert.False(vm.HasIcon);
    }

    [Fact]
    public void BrowseForFolderUsesFolderDialog()
    {
        var host = new FakeEntryHost { FakeDialogs = { FolderAnswer = @"\\srv\share" } };
        var vm = new CustomEntryViewModel(new CustomEntry { Kind = CustomEntryKind.Folder }, host);

        vm.BrowseCommand.Execute(null);

        Assert.Equal(@"\\srv\share", vm.Target);
        Assert.Contains("PickFolder", host.FakeDialogs.Asked);
    }

    [Fact]
    public void TestingTextSnippetCopiesInsteadOfStarting()
    {
        var host = new FakeEntryHost();
        var vm = new CustomEntryViewModel(new CustomEntry { Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text, Name = "Gruß", Code = "Viele Grüße" }, host);

        Assert.True(vm.TestCommand.CanExecute(null));
        vm.TestCommand.Execute(null);

        Assert.Equal(new[] { "Viele Grüße" }, host.FakeShell.Clipboard);
    }

    [Fact]
    public void TestingFileShowsItInExplorer()
    {
        var file = Path.GetTempFileName();
        try
        {
            var host = new FakeEntryHost();
            var vm = new CustomEntryViewModel(new CustomEntry { Kind = CustomEntryKind.File, Target = file }, host);

            vm.TestCommand.Execute(null);

            Assert.Equal(file, Assert.Single(host.FakeShell.Opened).Path);
        }
        finally
        {
            File.Delete(file);
        }
    }
}

public class ProfilesViewModelTests
{
    private static ProfilesViewModel NewEntryDialog(FakeEntryHost host, bool saveImmediately = false, bool pickKind = false)
    {
        var entry = new CustomEntry { Kind = CustomEntryKind.Url };
        return new ProfilesViewModel(host, entry.ToLaunchItem(), host.Settings, saveImmediately, entry, isNew: true, pickKind: pickKind);
    }

    [Fact]
    public void SavingNewEntryWritesEntryAndConfig()
    {
        var host = new FakeEntryHost();
        var vm = NewEntryDialog(host, saveImmediately: true);
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.Entry!.Target = "https://wiki.example/";
        vm.Entry.Name = "Wiki";
        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);

        var saved = Assert.Single(host.Settings.CustomEntries);
        Assert.Equal("Wiki", saved.Name);
        Assert.Equal(saved.Key, vm.SavedKey);
        Assert.Contains(host.Settings.Entries, e => e.Path == saved.Key);
        Assert.Equal(1, host.ProfilesSaved);
    }

    [Fact]
    public void ProfilesCanBeAddedDuplicatedAndDeleted()
    {
        var host = new FakeEntryHost();
        var vm = NewEntryDialog(host);
        Assert.Single(vm.Profiles);
        Assert.False(vm.DeleteCommand.CanExecute(null));

        vm.AddCommand.Execute(null);
        vm.DuplicateCommand.Execute(null);

        Assert.Equal(3, vm.Profiles.Count);
        Assert.EndsWith("(Kopie)", vm.Selected!.Name);
        Assert.Single(vm.Profiles, p => p.IsDefault);

        vm.DeleteCommand.Execute(null);
        Assert.Equal(2, vm.Profiles.Count);
    }

    [Fact]
    public void KindPickerSuggestsClipboardContent()
    {
        var host = new FakeEntryHost();
        host.FakeShell.ClipboardContent = "https://status.example/dashboard";
        var vm = NewEntryDialog(host, pickKind: true);

        Assert.True(vm.ShowKindPicker);
        Assert.True(vm.HasClipboardSuggestion);
        vm.UseClipboardCommand.Execute(null);

        Assert.False(vm.ShowKindPicker);
        Assert.Equal("https://status.example/dashboard", vm.Entry!.Target);
    }

    [Fact]
    public void TestButtonRunsEntryWithDefaultProfile()
    {
        var host = new FakeEntryHost();
        var vm = NewEntryDialog(host);
        vm.Entry!.Target = "https://a.example";

        vm.Entry.TestCommand.Execute(null);

        var (entry, profile) = Assert.Single(host.TestRuns);
        Assert.Equal("https://a.example", entry.Target);
        Assert.True(profile.IsDefault);
    }
}

public class CustomEntryListViewModelTests
{
    private static (FakeEntryHost host, AppSettings s, CustomEntryListViewModel vm) List()
    {
        var host = new FakeEntryHost();
        var s = new AppSettings();
        s.CustomEntries.Add(new CustomEntry { Kind = CustomEntryKind.Url, Name = "Wiki", Target = "https://wiki.example" });
        s.CustomEntries.Add(new CustomEntry { Kind = CustomEntryKind.Rdp, Name = "Applikationsserver", Target = "app01" });
        s.CustomEntries.Add(new CustomEntry { Kind = CustomEntryKind.Url, Name = "Monitoring", Target = "https://mon.example" });
        return (host, s, new CustomEntryListViewModel(host, s, () => Array.Empty<string>()));
    }

    [Fact]
    public void RowsAreSortedAndFilterable()
    {
        var (_, _, vm) = List();

        Assert.Equal(new[] { "Applikationsserver", "Monitoring", "Wiki" }, vm.VisibleRows.Select(r => r.Name));
        Assert.Equal("3 Einträge", vm.SummaryText);

        vm.SetKindFilterCommand.Execute("Url");
        Assert.Equal(new[] { "Monitoring", "Wiki" }, vm.VisibleRows.Select(r => r.Name));

        vm.Query = "wiki";
        Assert.Equal(new[] { "Wiki" }, vm.VisibleRows.Select(r => r.Name));
        Assert.Equal("1 von 3 Einträgen", vm.SummaryText);

        vm.Query = "gibtsnicht";
        Assert.True(vm.NoMatches);
    }

    [Fact]
    public void RemoveAsksAndKeepsRowsWhenDeclined()
    {
        var (host, _, vm) = List();
        var row = vm.VisibleRows[0];

        host.FakeDialogs.ConfirmAnswer = false;
        row.RemoveCommand.Execute(null);
        Assert.Equal(3, vm.Count);
        Assert.Equal("Entfernen", host.FakeDialogs.Messages.Single().Title);

        host.FakeDialogs.ConfirmAnswer = true;
        row.RemoveCommand.Execute(null);
        Assert.Equal(2, vm.Count);
        Assert.DoesNotContain(row, vm.VisibleRows);
    }

    [Fact]
    public void SelectAllSelectsOnlyVisibleRowsAndExportUsesThem()
    {
        var (host, _, vm) = List();
        vm.SetKindFilterCommand.Execute("Rdp");

        vm.SelectAllCommand.Execute(null);
        Assert.Equal("1 Eintrag ausgewählt", vm.SelectionText);

        vm.ExportCommand.Execute(null);
        Assert.Contains("ExportEntries 1", host.Calls);
    }

    [Fact]
    public void UnreadableImportFileWarns()
    {
        var (host, _, vm) = List();
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "kein JSON");
            host.FakeDialogs.FileAnswer = file;

            vm.ImportCommand.Execute(null);

            var msg = host.FakeDialogs.Messages.Single();
            Assert.Equal(("warn", "Importieren"), (msg.Kind, msg.Title));
            Assert.Equal(3, vm.Count);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void NewEntryOpensEditorAndFollowsAlternative()
    {
        var (host, _, vm) = List();
        host.EditorAnswer = new EntryEditorResult(null, null, @"D:\Tools");

        vm.NewCommand.Execute(null);

        Assert.Contains(host.Calls, c => c.StartsWith("EditEntryModal"));
        Assert.Contains(@"CreateSmartGroupFor D:\Tools", host.Calls);
        Assert.Equal(3, vm.Count);
    }
}
