using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Updates;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec.UI.Core.Tests;

internal sealed class FakeShell : IShell
{
    public List<(string Path, bool IsFolder)> Opened { get; } = new();
    public List<string> Urls { get; } = new();
    public List<string> Clipboard { get; } = new();
    public bool IsPwshInstalled { get; set; } = true;

    public void OpenLocation(string path, bool isFolder = false) => Opened.Add((path, isFolder));
    public void OpenUrl(string url) => Urls.Add(url);
    public void SetClipboardText(string text) => Clipboard.Add(text);
}

internal sealed class FakeTheme : IThemeInfo
{
    public bool IsHighContrast { get; set; }
    public bool IsDark { get; set; } = true;
}

internal sealed class FakeIcons : IIconProvider
{
    public IconImage? Get(string path, DateTime lastWriteUtc, Action<IconImage> onLoaded) => null;
    public IconImage? GetFolder(Action<IconImage> onLoaded) => null;
    public IconImage? GetProgram(string program, Action<IconImage> onLoaded) => null;
    public IconImage? GetFavicon(string url, Action<IconImage> onLoaded) => null;
}

internal sealed class FakeItemSource : IItemSource
{
    public List<LaunchItem> List { get; } = new();
    public IReadOnlyList<LaunchItem> Items => List;
    public IReadOnlyList<FolderStatus> FolderStatuses { get; set; } = Array.Empty<FolderStatus>();
    public DateTime LastScan { get; set; } = DateTime.Now;
    public bool IsScanning { get; set; }
    public int ScanCount { get; private set; }
    public Task ScanAsync() { ScanCount++; return Task.CompletedTask; }
}

internal sealed class FakePanelContext : IPanelContext
{
    public PanelConfig Config { get; } = new();
    public PanelViewModel ViewModel { get; set; } = null!;
}

/// <summary>App-Ersatz für das Panel: echte Einstellungen, Aufrufe werden mitgeschrieben.</summary>
internal sealed class FakePanelHost : IPanelHost
{
    private static readonly FakeUiDispatcher Ui = new();

    public AppSettings Settings { get; } = new();
    public FakeShell FakeShell { get; } = new();
    public FakeDialogs FakeDialogs { get; } = new();
    public FakeItemSource Items { get; } = new();
    public List<string> Calls { get; } = new();

    public IShell Shell => FakeShell;
    public IDialogs Dialogs => FakeDialogs;
    public IThemeInfo Theme { get; } = new FakeTheme();
    public IIconProvider IconProvider { get; } = new FakeIcons();
    public HealthService Health { get; } = new(Ui);
    public ActivityList Activities { get; } = new(Ui);
    public UpdateState Update { get; } = new();
    public IItemSource Repository => Items;
    public RunHistory History { get; } = new();
    public string? UndoToast { get; set; }
    public bool CanUndo => false;
    public ReleaseNote? WhatsNew { get; set; }
#pragma warning disable CS0067 // im Test nicht ausgelöst
    public event Action? HistoryChanged;
#pragma warning restore CS0067

    /// <summary>Panel mit diesem Host aufbauen (wie PanelHost in der App).</summary>
    public PanelViewModel CreatePanel(Action<PanelConfig>? configure = null)
    {
        var ctx = new FakePanelContext();
        configure?.Invoke(ctx.Config);
        ctx.ViewModel = new PanelViewModel(this, ctx);
        return ctx.ViewModel;
    }

    private void Log(string call) => Calls.Add(call);

    public void SaveSettings() => Log("SaveSettings");
    public LaunchItem? FindItem(string path) => Items.List.FirstOrDefault(i => i.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
    public Task<bool> RunWorkflowAsync(string name, IReadOnlyList<WorkflowStep> steps, bool stopOnError) { Log($"RunWorkflow {name} {steps.Count}"); return Task.FromResult(true); }
    public void OnWorkflowSaved(CustomEntry original, CustomEntry edited) => Log("OnWorkflowSaved");
    public bool Replay(HistoryEntry entry, bool onlyFailed = false) { Log("Replay"); return true; }
    public void OpenLocation(string path) => FakeShell.OpenLocation(path);

    public void RunProfile(LaunchItem item, LaunchProfile? profile, ExecutionMode? overrideMode = null, bool record = true) => Log($"RunProfile {item.DisplayName} {overrideMode}");
    public void OpenRunDialog(LaunchItem item, ExecutionMode mode, bool remote, LaunchProfile? profile = null) => Log($"OpenRunDialog {item.DisplayName} {mode} {remote}");
    public void CopySnippet(LaunchItem item, bool record = true) => Log("CopySnippet");
    public void OpenInEditor(LaunchItem item) => Log($"OpenInEditor {item.DisplayName}");
    public CustomEntry CreateWorkflow(string name, IEnumerable<LaunchItem> items, string? group = null) { Log($"CreateWorkflow {name} {items.Count()}"); return new CustomEntry { Name = name, Kind = CustomEntryKind.Workflow }; }

    public bool IsFavorite(string path) => Settings.Favorites.Contains(path, StringComparer.OrdinalIgnoreCase);
    public void ToggleFavorite(LaunchItem item) => Log($"ToggleFavorite {item.DisplayName}");
    public void ToggleSingleClick(LaunchItem item) => Log("ToggleSingleClick");
    public void HideItem(LaunchItem item) => Log($"HideItem {item.DisplayName}");
    public void UnblockItem(LaunchItem item) => Log("UnblockItem");
    public void AssignGroup(string path, string? group, string? targetKey = null, string? beforeOrAfterPath = null, bool after = false, IReadOnlyList<string>? targetOrder = null) => Log($"AssignGroup {path} {group}");
    public LaunchItem DescribeCustom(CustomEntry entry) => entry.ToLaunchItem();
    public CatalogSubscription? CatalogOf(CustomEntry? entry) => null;
    public void NewCustomEntry(string? kindKey, string? group) => Log($"NewCustomEntry {kindKey} {group}");
    public void EditCustomEntry(LaunchItem item) => Log("EditCustomEntry");
    public void DuplicateCustomEntry(LaunchItem item) => Log("DuplicateCustomEntry");
    public void RemoveCustomEntry(LaunchItem item) => Log("RemoveCustomEntry");
    public void AddDropped(IReadOnlyList<DroppedTarget> targets, string? group, bool askForFolders = true) => Log("AddDropped");
    public void AddFilesDialog(string? group) => Log("AddFilesDialog");
    public void AddFromCatalog(string? group) => Log("AddFromCatalog");
    public void AddFromClipboard(string? group) => Log("AddFromClipboard");
    public bool ImportEntries(string? intoGroup = null) { Log("ImportEntries"); return false; }
    public Task ApplyPendingCatalogsAsync() => Task.CompletedTask;

    public string? CreateGroup(string? parent = null) { Log($"CreateGroup {parent}"); return null; }
    public void RenameGroup(string path) => Log($"RenameGroup {path}");
    public void DeleteGroup(string path, int count) => Log($"DeleteGroup {path} {count}");
    public void MoveGroupTo(string path, string target, GroupDropPosition position) => Log($"MoveGroupTo {path} {target} {position}");
    public bool MoveTopLevel(string key, string anchor, bool after) { Log("MoveTopLevel"); return true; }
    public void HideGroupInWorkspace(string groupPath) => Log($"HideGroupInWorkspace {groupPath}");
    public void ChooseGroupIcon(string groupPath) => Log("ChooseGroupIcon");
    public void SetGroupColor(string groupPath, string? color) => Log($"SetGroupColor {groupPath} {color}");
    public void PickGroupColor(string groupPath) => Log($"PickGroupColor {groupPath}");
    public void SetGroupLayout(string groupPath, ItemLayout? layout) => Log($"SetGroupLayout {groupPath} {layout}");
    public void ExportGroup(string groupPath) => Log("ExportGroup");

    public void ShowSettings(string? folderPath = null, string? section = null) => Log($"ShowSettings {folderPath} {section}");
    public void ShowProfiles(LaunchItem item) => Log("ShowProfiles");
    public void ShowSchedule(LaunchItem item) => Log("ShowSchedule");
    public void OnProfilesSaved() => Log("OnProfilesSaved");
    public void SwitchWorkspace(string name) => Log($"SwitchWorkspace {name}");
    public void ShowPanelFromHotkey(IPanelContext? panel = null, bool forcePanel = false) => Log("ShowPanelFromHotkey");

    public void Undo() => Log("Undo");
    public void RememberForUndo(string description) => Log($"RememberForUndo {description}");
    public void ShowUndoToast(string text) => Log($"ShowUndoToast {text}");
    public void DismissUndoToast() => Log("DismissUndoToast");
    public void PauseUndoToast(bool paused) => Log("PauseUndoToast");

    public void DismissWhatsNew() => Log("DismissWhatsNew");
    public Task InstallUpdateAsync() { Log("InstallUpdate"); return Task.CompletedTask; }
}
