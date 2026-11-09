using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Updates;
using PowerExec.Services;

namespace PowerExec.ViewModels;

/// <summary>Ein Panel der Oberfläche: seine Einstellungen und sein ViewModel (WPF: <c>PanelHost</c>).</summary>
public interface IPanelContext
{
    PanelConfig Config { get; }
    PanelViewModel ViewModel { get; }
}

/// <summary>
/// Was das Panel mit seinen Gruppen und Einträgen (<see cref="PanelViewModel"/>, <see cref="GroupViewModel"/>,
/// <see cref="ItemViewModel"/>, Vorschau, Parameter im Panel, Spotlight) von der App braucht.
/// Fenster und Rückfragen öffnet die App selbst; die ViewModels lösen sie nur aus.
/// </summary>
public interface IPanelHost : IHistoryHost, IWorkflowHost
{
    // ── Dienste ──────────────────────────────────────────────────────
    IShell Shell { get; }
    IDialogs Dialogs { get; }
    IThemeInfo Theme { get; }
    IIconProvider IconProvider { get; }
    HealthService Health { get; }
    ActivityList Activities { get; }
    UpdateState Update { get; }

    void SaveSettings();

    // ── Gefundene Einträge, Verlauf ──────────────────────────────────
    /// <summary>Eingelesene Dateien und Status der Ordner.</summary>
    IItemSource Repository { get; }
    RunHistory History { get; }
    event Action? HistoryChanged;

    // ── Starten ──────────────────────────────────────────────────────
    void RunProfile(LaunchItem item, LaunchProfile? profile, ExecutionMode? overrideMode = null, bool record = true);
    void OpenRunDialog(LaunchItem item, ExecutionMode mode, bool remote, LaunchProfile? profile = null);
    void CopySnippet(LaunchItem item, bool record = true);
    void OpenInEditor(LaunchItem item);
    CustomEntry CreateWorkflow(string name, IEnumerable<LaunchItem> items, string? group = null);

    // ── Einträge ─────────────────────────────────────────────────────
    bool IsFavorite(string path);
    void ToggleFavorite(LaunchItem item);
    void ToggleSingleClick(LaunchItem item);
    void HideItem(LaunchItem item);
    void UnblockItem(LaunchItem item);
    void AssignGroup(string path, string? group, string? targetKey = null, string? beforeOrAfterPath = null, bool after = false, IReadOnlyList<string>? targetOrder = null);
    LaunchItem DescribeCustom(CustomEntry entry);
    CatalogSubscription? CatalogOf(CustomEntry? entry);
    void NewCustomEntry(string? kindKey, string? group);
    void EditCustomEntry(LaunchItem item);
    void DuplicateCustomEntry(LaunchItem item);
    void RemoveCustomEntry(LaunchItem item);
    void AddDropped(IReadOnlyList<DroppedTarget> targets, string? group, bool askForFolders = true);
    void AddFilesDialog(string? group);
    void AddFromCatalog(string? group);
    void AddFromClipboard(string? group);
    bool ImportEntries(string? intoGroup = null);
    Task ApplyPendingCatalogsAsync();

    // ── Gruppen ──────────────────────────────────────────────────────
    string? CreateGroup(string? parent = null);
    void RenameGroup(string path);
    void DeleteGroup(string path, int count);
    void MoveGroupTo(string path, string target, GroupDropPosition position);
    bool MoveTopLevel(string key, string anchor, bool after);
    void HideGroupInWorkspace(string groupPath);
    void ChooseGroupIcon(string groupPath);
    void SetGroupColor(string groupPath, string? color);
    void PickGroupColor(string groupPath);
    void SetGroupLayout(string groupPath, ItemLayout? layout);
    /// <summary>Elementgröße einer Gruppe (null = wie das Panel).</summary>
    void SetGroupItemSize(string groupPath, ItemSize? size);

    /// <summary>Anzeigewert einer {{$Variable}} für die Vorschau (Geheimes verdeckt, PowerShell nur aus dem Zwischenspeicher).</summary>
    string VariablePreview(string name) => Placeholders.BuiltinValue(name) ?? name;
    void ExportGroup(string groupPath);

    // ── Fenster und Wechsel ──────────────────────────────────────────
    void ShowSettings(string? folderPath = null, string? section = null);
    void ShowProfiles(LaunchItem item);
    void ShowSchedule(LaunchItem item);
    void OnProfilesSaved();
    void SwitchWorkspace(string name);
    void ShowPanelFromHotkey(IPanelContext? panel = null, bool forcePanel = false);

    // ── Rückgängig ───────────────────────────────────────────────────
    string? UndoToast { get; }
    bool CanUndo { get; }
    void Undo();
    void RememberForUndo(string description);
    void ShowUndoToast(string text);
    void DismissUndoToast();
    void PauseUndoToast(bool paused);

    // ── Version ──────────────────────────────────────────────────────
    /// <summary>Kurz unten im Panel: welche Oberfläche und Version läuft, welche ist installiert.</summary>
    string VersionText { get; }

    // ── Updates und Neuerungen ───────────────────────────────────────
    ReleaseNote? WhatsNew { get; }
    void DismissWhatsNew();
    Task InstallUpdateAsync();
}
