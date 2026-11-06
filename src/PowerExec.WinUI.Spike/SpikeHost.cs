using Microsoft.UI.Dispatching;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Updates;
using PowerExec.Infrastructure;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec.WinUI.Spike;

/// <summary><see cref="IUiDispatcher"/> für WinUI (DispatcherQueue).</summary>
public sealed class WinUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public void Post(Action action) => queue.TryEnqueue(() => action());

    public IUiTimer CreateTimer(TimeSpan interval, Action tick) => new Timer(queue, interval, tick);

    private sealed class Timer : IUiTimer
    {
        private readonly DispatcherQueueTimer _timer;

        public Timer(DispatcherQueue queue, TimeSpan interval, Action tick)
        {
            _timer = queue.CreateTimer();
            _timer.Interval = interval;
            _timer.Tick += (_, _) => tick();
        }

        public bool IsRunning => _timer.IsRunning;
        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
    }
}

/// <summary>Die eingelesenen Einträge des Prototyps: 500 erfundene Scripte und Programme in 20 Ordnern.</summary>
public sealed class SpikeItemSource : IItemSource
{
    public List<LaunchItem> List { get; } = new();
    public IReadOnlyList<LaunchItem> Items => List;
    public IReadOnlyList<FolderStatus> FolderStatuses { get; set; } = Array.Empty<FolderStatus>();
    public DateTime LastScan { get; } = DateTime.Now;
    public bool IsScanning => false;
    public event EventHandler? Changed;
    public Task ScanAsync() { Changed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
    public LaunchItem Reload(LaunchItem item) => item;
}

/// <summary>
/// App-Ersatz für das Panel im Prototyp: echte Einstellungen und echte Suche über <see cref="PanelViewModel"/>,
/// aber Starten, Dialoge und Fenster werden nur protokolliert.
/// </summary>
public sealed class SpikeHost : IPanelHost
{
    public const string DropFolder = @"C:\Spike\Abgelegt";
    private static readonly string[] Verbs = { "Get", "Set", "Reset", "Test", "Start", "Stop", "Export", "Import", "Clear", "Repair" };
    private static readonly string[] Nouns = { "DiskSpace", "ADUserPassword", "Service", "Printer", "Share", "Mailbox", "Certificate", "Hotfix", "Session", "Cache", "Spooler", "Proxy", "Dns", "Gpo", "Backup" };

    private readonly App _app;

    public SpikeHost(App app)
    {
        _app = app;
        var ui = new WinUiDispatcher(DispatcherQueue.GetForCurrentThread());
        UiDispatcher = ui;
        Health = new HealthService(ui);
        Activities = new ActivityList(ui);
        Settings.ShowProgramIcons = false;
        Seed();
    }

    public IUiDispatcher UiDispatcher { get; }
    public AppSettings Settings { get; } = new();
    public SpikeItemSource Items { get; } = new();
    public List<string> Calls { get; } = new();

    /// <summary>500 Einträge in 20 Gruppen (ähnlich dem Testbestand der Leistungsmessung der WPF-App).</summary>
    private void Seed()
    {
        var rnd = new Random(42);
        for (int g = 0; g < 20; g++)
        {
            var folder = $@"C:\Spike\Kunde {g + 1:00}";
            SmartGroups.Add(Settings, folder);
            for (int i = 0; i < 25; i++)
            {
                var name = $"{Verbs[rnd.Next(Verbs.Length)]}-{Nouns[rnd.Next(Nouns.Length)]}{(i % 3 == 0 ? "" : (i + 1).ToString())}";
                var exe = i % 7 == 0;
                Items.List.Add(new LaunchItem
                {
                    FullPath = $@"{folder}\{name}{(exe ? ".exe" : ".ps1")}",
                    RootFolder = folder,
                    Type = exe ? ItemType.Executable : ItemType.PowerShell,
                    DisplayName = name,
                    Synopsis = exe ? "Werkzeug" : $"{name} für Kunde {g + 1:00} – prüft, ändert oder exportiert Einstellungen"
                });
            }
        }
        SmartGroups.Add(Settings, DropFolder, "Abgelegt");
    }

    /// <summary>Aus dem Explorer oder Browser abgelegt: als Einträge in die Gruppe „Abgelegt“.</summary>
    public int AddDroppedPaths(IEnumerable<string> targets)
    {
        int n = 0;
        foreach (var t in targets)
        {
            var isUrl = t.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            var name = isUrl ? new Uri(t).Host : Path.GetFileName(t.TrimEnd('\\'));
            Items.List.Add(new LaunchItem
            {
                FullPath = $@"{DropFolder}\{Guid.NewGuid():N}-{name}",
                RootFolder = DropFolder,
                Type = isUrl ? ItemType.Other : LaunchItem.TypeFromExtension(t),
                DisplayName = string.IsNullOrEmpty(name) ? t : name,
                Synopsis = t
            });
            n++;
        }
        Log($"Abgelegt: {n}");
        return n;
    }

    private void Log(string call)
    {
        Calls.Add(call);
        SpikeLog.Write(call);
    }

    // ── IPanelHost ───────────────────────────────────────────────────
    public IShell Shell { get; } = new SpikeShell();
    public IDialogs Dialogs { get; } = new SpikeDialogs();
    public IThemeInfo Theme { get; } = new SpikeTheme();
    public IIconProvider IconProvider { get; } = new NoIcons();
    public HealthService Health { get; }
    public ActivityList Activities { get; }
    public UpdateState Update { get; } = new();
    public IItemSource Repository => Items;
    public RunHistory History { get; } = new();
    public string? UndoToast => null;
    public bool CanUndo => false;
    public ReleaseNote? WhatsNew => null;

    /// <summary>„WinUI-Prototyp · Build abc1234 · installiert: WPF 0.19.5“.</summary>
    public string VersionText => _versionText ??= $"WinUI-Prototyp · Build {BuildId} · " +
        (InstalledVersion.Read() is { } v ? $"installiert: WPF {v}" : "WPF nicht installiert");
    private string? _versionText;

    /// <summary>Kurzer Commit aus CI (InformationalVersion „…+sha“), sonst Datum der EXE.</summary>
    public static string BuildId
    {
        get
        {
            var info = typeof(SpikeHost).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
            if (info?.IndexOf('+') is int plus and >= 0 && info.Length > plus + 1) return info[(plus + 1)..Math.Min(info.Length, plus + 8)];
            return File.GetLastWriteTime(Environment.ProcessPath ?? AppContext.BaseDirectory).ToString("dd.MM. HH:mm");
        }
    }
#pragma warning disable CS0067 // im Prototyp nicht ausgelöst
    public event Action? HistoryChanged;
#pragma warning restore CS0067

    public void SaveSettings() => Log("SaveSettings");
    public LaunchItem? FindItem(string path) => Items.List.FirstOrDefault(i => i.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
    public Task<bool> RunWorkflowAsync(string name, IReadOnlyList<WorkflowStep> steps, bool stopOnError) { Log($"RunWorkflow {name}"); return Task.FromResult(true); }
    public void OnWorkflowSaved(CustomEntry original, CustomEntry edited) => Log("OnWorkflowSaved");
    public bool Replay(HistoryEntry entry, bool onlyFailed = false) { Log("Replay"); return true; }
    public void OpenLocation(string path) => Shell.OpenLocation(path);
    public void RunProfile(LaunchItem item, LaunchProfile? profile, ExecutionMode? overrideMode = null, bool record = true) => Log($"Starten: {item.DisplayName} {overrideMode}");
    public void OpenRunDialog(LaunchItem item, ExecutionMode mode, bool remote, LaunchProfile? profile = null) => Log($"Ausführen-Dialog: {item.DisplayName} {mode} remote={remote}");
    public void CopySnippet(LaunchItem item, bool record = true) => Log("CopySnippet");
    public void OpenInEditor(LaunchItem item) => Log($"Editor: {item.DisplayName}");
    public CustomEntry CreateWorkflow(string name, IEnumerable<LaunchItem> items, string? group = null) { Log($"Ablauf {name}"); return new CustomEntry { Name = name, Kind = CustomEntryKind.Workflow }; }
    public bool IsFavorite(string path) => Settings.Favorites.Contains(path, StringComparer.OrdinalIgnoreCase);
    public void ToggleFavorite(LaunchItem item) => Log($"Favorit: {item.DisplayName}");
    public void ToggleSingleClick(LaunchItem item) => Log("ToggleSingleClick");
    public void HideItem(LaunchItem item) => Log($"Ausblenden: {item.DisplayName}");
    public void UnblockItem(LaunchItem item) => Log("UnblockItem");
    public void AssignGroup(string path, string? group, string? targetKey = null, string? beforeOrAfterPath = null, bool after = false, IReadOnlyList<string>? targetOrder = null) => Log($"Gruppe: {path} → {group}");
    public LaunchItem DescribeCustom(CustomEntry entry) => entry.ToLaunchItem();
    public CatalogSubscription? CatalogOf(CustomEntry? entry) => null;
    public void NewCustomEntry(string? kindKey, string? group) => Log($"Neuer Eintrag {kindKey}");
    public void EditCustomEntry(LaunchItem item) => Log("EditCustomEntry");
    public void DuplicateCustomEntry(LaunchItem item) => Log("DuplicateCustomEntry");
    public void RemoveCustomEntry(LaunchItem item) => Log("RemoveCustomEntry");
    public void AddDropped(IReadOnlyList<DroppedTarget> targets, string? group, bool askForFolders = true) => AddDroppedPaths(targets.Select(t => t.Target));
    public void AddFilesDialog(string? group) => Log("AddFilesDialog");
    public void AddFromCatalog(string? group) => Log("AddFromCatalog");
    public void AddFromClipboard(string? group) => Log("AddFromClipboard");
    public bool ImportEntries(string? intoGroup = null) { Log("ImportEntries"); return false; }
    public Task ApplyPendingCatalogsAsync() => Task.CompletedTask;
    public string? CreateGroup(string? parent = null) { Log("CreateGroup"); return null; }
    public void RenameGroup(string path) => Log($"RenameGroup {path}");
    public void DeleteGroup(string path, int count) => Log($"DeleteGroup {path}");
    public void MoveGroupTo(string path, string target, GroupDropPosition position) => Log($"MoveGroupTo {path} {target} {position}");
    public bool MoveTopLevel(string key, string anchor, bool after) { Log($"MoveTopLevel {key} {anchor} {after}"); return true; }
    public void HideGroupInWorkspace(string groupPath) => Log($"HideGroup {groupPath}");
    public void ChooseGroupIcon(string groupPath) => Log("ChooseGroupIcon");
    public void SetGroupColor(string groupPath, string? color) => Log($"SetGroupColor {groupPath} {color}");
    public void PickGroupColor(string groupPath) => Log("PickGroupColor");
    public void SetGroupLayout(string groupPath, ItemLayout? layout) => Log("SetGroupLayout");
    public void ExportGroup(string groupPath) => Log("ExportGroup");
    public void ShowSettings(string? folderPath = null, string? section = null) => Log("Einstellungen");
    public void ShowProfiles(LaunchItem item) => Log("ShowProfiles");
    public void ShowSchedule(LaunchItem item) => Log("ShowSchedule");
    public void OnProfilesSaved() => Log("OnProfilesSaved");
    public void SwitchWorkspace(string name) => Log($"Arbeitsbereich {name}");
    public void ShowPanelFromHotkey(IPanelContext? panel = null, bool forcePanel = false) => Log("ShowPanelFromHotkey");
    public void Undo() => Log("Undo");
    public void RememberForUndo(string description) => Log($"RememberForUndo {description}");
    public void ShowUndoToast(string text) => Log($"Toast {text}");
    public void DismissUndoToast() { }
    public void PauseUndoToast(bool paused) { }
    public void DismissWhatsNew() { }
    public Task InstallUpdateAsync() => Task.CompletedTask;
}

internal sealed class SpikeShell : IShell
{
    public void OpenLocation(string path, bool isFolder = false) => SpikeLog.Write($"Explorer: {path}");
    public void OpenUrl(string url) => SpikeLog.Write($"Browser: {url}");
    public void SetClipboardText(string text) => SpikeLog.Write($"Zwischenablage: {text}");
    public void CopyToClipboard(string text, bool asHtml) => SetClipboardText(text);
    public string GetClipboardText() => string.Empty;
    public bool IsPwshInstalled => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"));
}

internal sealed class SpikeDialogs : IDialogs
{
    public string? Ask(string heading, string prompt, string initial = "", string okText = "OK", Func<string, string?>? validate = null) => null;
    public void Inform(string message, string title, object? owner = null) => SpikeLog.Write($"Hinweis {title}: {message}");
    public void Warn(string message, string title, object? owner = null) => SpikeLog.Write($"Warnung {title}: {message}");
    public bool Confirm(string message, string title, bool destructive = false, object? owner = null, bool defaultNo = false) => false;
    public bool? ConfirmOrCancel(string message, string title, object? owner = null) => null;
    public string? OpenFile(string filter, string? title = null, object? owner = null) => null;
    public IReadOnlyList<string> OpenFiles(string filter, string? title = null, object? owner = null) => Array.Empty<string>();
    public string? SaveFile(string filter, string fileName, string? title = null, object? owner = null) => null;
    public string? PickFolder(string? title = null, object? owner = null) => null;
    public string? PickColor(string title, string? initial, object? owner = null) => null;
    public IconRef? PickIcon(IconRef current, string heading) => null;
    public NewGroupSpec? AskNewGroup(string heading, string initialName, Func<string, string?> validateName, Func<string?, string?>? validateFolder) => null;
    public IReadOnlyList<CatalogTemplate>? PickCatalogTemplates() => null;
}

internal sealed class SpikeTheme : IThemeInfo
{
    public bool IsHighContrast => new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
    public bool IsDark => Microsoft.UI.Xaml.Application.Current.RequestedTheme == Microsoft.UI.Xaml.ApplicationTheme.Dark;
}

internal sealed class NoIcons : IIconProvider
{
    public IconImage? Get(string path, DateTime lastWriteUtc, Action<IconImage> onLoaded) => null;
    public IconImage? GetFolder(Action<IconImage> onLoaded) => null;
    public IconImage? GetProgram(string program, Action<IconImage> onLoaded) => null;
    public IconImage? GetFavicon(string url, Action<IconImage> onLoaded) => null;
}
