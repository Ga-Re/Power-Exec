using System.Globalization;
using PowerExec.Core.Localization;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Search;
using PowerExec.Core.Updates;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI;

/// <content>Eigene Gruppen: anlegen, umbenennen, verschieben, Farbe, Symbol und Darstellung; Ablegen von außen.</content>
public partial class AppController
{
    // ---- Eigene Gruppen ---------------------------------------------------

    private int _dialogs;

    /// <summary>Solange ein Dialog aus dem Panel offen ist, fährt das Panel nicht ein.</summary>
    public bool IsDialogOpen => _dialogs > 0;

    /// <summary>Ja/Nein-Frage über dem offenen Panel (das Panel liegt sonst darüber).</summary>
    private bool Confirm(string text, string title)
    {
        var owner = _hosts.FirstOrDefault(h => h.View.IsOpen)?.View.Owner;
        return WithDialog(() => Dialogs.Confirm(text, title, owner: owner, defaultNo: true));
    }

    public T WithDialog<T>(Func<T> show)
    {
        _dialogs++;
        try { return show(); }
        finally { _dialogs--; }
    }

    private string? GroupNameError(string name, string? parent, string? exceptPath = null)
    {
        var clean = GroupManager.CleanName(name);
        var path = parent == null ? clean : parent + GroupManager.Separator + clean;
        return GroupManager.Exists(Settings, path) && !path.Equals(exceptPath, StringComparison.OrdinalIgnoreCase)
            ? L.T("A group with this name already exists here.") : null;
    }

    /// <param name="parent">Pfad der übergeordneten Gruppe (Untergruppe) oder null.</param>
    /// <returns>Pfad der neuen Gruppe oder null bei Abbruch.</returns>
    /// <summary>
    /// Dialog „Neue Gruppe“ (Name, Symbol/Bild, Ordner, Farbe, Darstellung). Mit <paramref name="parent"/> entsteht eine
    /// Untergruppe (Rechtsklick auf eine Gruppe), sonst eine Gruppe auf oberster Ebene am Ende. Mit Ordner: smarte Gruppe.
    /// </summary>
    public string? CreateGroup(string? parent = null)
    {
        var heading = parent == null ? L.T("New group") : L.F("New subgroup in “{0}”", GroupManager.LeafOf(parent));
        var spec = WithDialog(() => Dialogs.AskNewGroup(heading, parent == null ? L.T("New group") : L.T("New subgroup"),
            n => GroupNameError(n, parent), f => NewGroup.FolderError(Settings.Folders, f)));
        if (spec == null) return null;
        var path = NewGroup.Create(Settings, spec, parent);
        if (parent != null)
            Settings.CollapsedGroups.RemoveAll(k => k.Equals(GroupManager.GroupKey(parent), StringComparison.OrdinalIgnoreCase));
        CommitUserChange();
        if (spec.IsSmart) Repository.UpdateSettings(Settings); // Ordner einlesen und überwachen
        return path;
    }

    public void RenameGroup(string path)
    {
        var parent = GroupManager.ParentOf(path);
        var newName = WithDialog(() => Dialogs.Ask(L.T("Rename group"), string.Empty, GroupManager.LeafOf(path), L.T("Rename"), n => GroupNameError(n, parent, path)));
        if (newName == null || newName == GroupManager.LeafOf(path)) return;
        var before = SettingsStore.Clone(Settings);
        if (GroupManager.Rename(Settings, path, newName))
        {
            _undo.Push(before, L.T("Group renamed"));
            CommitUserChange();
            ShowUndoToast(L.F("Group renamed to “{0}”", GroupManager.CleanName(newName)));
        }
    }

    public void DeleteGroup(string path, int count)
    {
        var name = GroupManager.LeafOf(path);
        var subs = GroupManager.DescendantCount(Settings, path);
        var folders = Settings.Folders.Where(f => f.Group.Length > 0 && GroupManager.IsSameOrBelow(f.Group, path)).Select(f => f.Path).ToList();
        var text = (subs > 0 ? L.F("Delete group “{0}” with {1} subgroup(s)?", name, subs) : L.F("Delete group “{0}”?", name)) +
                   (folders.Count > 0 ? L.F("\n\nThe folder {0} will then no longer be scanned. The files themselves stay unchanged.", string.Join(", ", folders)) : string.Empty) +
                   (count > 0 && folders.Count == 0 ? L.F("\n\nThe {0} entries are kept and reappear in their smart group or under “Custom entries”.", count) : string.Empty);
        // Mit Rückgängig statt Rückfrage; nur bei Untergruppen oder Ordnern zusätzlich nachfragen
        if ((subs > 0 || folders.Count > 0) && !Confirm(text, L.T("Delete group"))) return;
        RememberForUndo(L.F("Group “{0}” deleted", name));
        GroupManager.Delete(Settings, path);
        CommitUserChange();
        ShowUndoToast(L.F("Group “{0}” deleted", name));
    }

    public void MoveGroup(string path, int delta)
    {
        RememberForUndo(L.T("Group moved"));
        GroupManager.Move(Settings, path, delta);
        CommitUserChange();
    }

    /// <summary>Drag &amp; Drop einer Gruppe auf eine andere: davor, dahinter oder hinein (Untergruppe).</summary>
    public void MoveGroupTo(string path, string target, GroupDropPosition position)
    {
        var before = SettingsStore.Clone(Settings);
        var result = GroupManager.MoveGroup(Settings, path, target, position);
        if (result == null)
        {
            Ui.Notify(L.T("Can't move"), L.T("There is already a group with this name at the target, or the group would be moved into its own subgroup."));
            return;
        }
        if (position == GroupDropPosition.Into)
            Settings.CollapsedGroups.RemoveAll(k => k.Equals(GroupManager.GroupKey(target), StringComparison.OrdinalIgnoreCase));
        _undo.Push(before, L.T("Group moved"));
        CommitUserChange();
        ShowUndoToast(position == GroupDropPosition.Into
            ? L.F("“{0}” moved to “{1}”", GroupManager.LeafOf(path), GroupManager.LeafOf(target))
            : L.F("“{0}” moved", GroupManager.LeafOf(path)));
    }

    /// <summary>Oberste Ebene umordnen – auch feste Gruppen (Zuletzt ausgeführt, Favoriten, Eigene Einträge).</summary>
    public bool MoveTopLevel(string key, string anchor, bool after)
    {
        var before = SettingsStore.Clone(Settings);
        if (FixedGroups.MoveTopLevel(Settings, key, anchor, after) is not { } moved) return false;
        _undo.Push(before, L.T("Group moved"));
        CommitUserChange();
        ShowUndoToast(L.F("“{0}” moved", (FixedGroups.IsFixed(moved) ? FixedGroups.Title(moved) : GroupManager.LeafOf(moved))));
        return true;
    }

    /// <summary>Gruppe im aktiven Arbeitsbereich ausblenden (wieder einblenden: Einstellungen › Arbeitsbereiche).</summary>
    public void HideGroupInWorkspace(string groupPath)
    {
        RememberForUndo(L.T("Group hidden"));
        WorkspaceManager.SetGroupHidden(WorkspaceManager.Active(Settings), groupPath, true);
        CommitUserChange();
        ShowUndoToast(L.F("“{0}” hidden in workspace “{1}”", GroupManager.LeafOf(groupPath), Settings.ActiveWorkspace));
    }

    /// <summary>Eintrag in eine Gruppe schieben (leer = aus der Gruppe nehmen), optional vor/hinter einen anderen Eintrag.</summary>
    public void AssignGroup(string path, string? group, string? targetKey = null, string? beforeOrAfterPath = null, bool after = false, IReadOnlyList<string>? targetOrder = null)
    {
        RememberForUndo(L.T("Entry moved"));
        GroupManager.Assign(Settings, path, group);
        if (targetKey != null && beforeOrAfterPath != null && targetOrder != null)
        {
            var order = targetOrder.Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)).Append(path).ToList();
            Settings.CustomOrder[targetKey] = ItemOrdering.Move(order, path, beforeOrAfterPath, after);
        }
        CommitUserChange();
        var label = FindItem(path) is { } moved ? DisplayNameOf(moved) : L.T("Entry");
        ShowUndoToast(group == null ? L.F("“{0}” removed from the group", label) : L.F("“{0}” moved to “{1}”", label, GroupManager.LeafOf(group)));
    }

    /// <summary>Aus dem Explorer oder Browser gezogene Ziele übernehmen.</summary>
    public void AddDropped(IReadOnlyList<DroppedTarget> targets, string? group, bool askForFolders = true)
    {
        if (targets.Count == 0) return;
        // Ordner: als smarte Gruppe (Inhalt einlesen) oder als Link (öffnet den Ordner)?
        var folders = askForFolders ? targets.Where(t => t.Kind == CustomEntryKind.Folder && Directory.Exists(t.Target)).ToList() : new List<DroppedTarget>();
        if (folders.Count > 0)
        {
            var asGroups = new List<DroppedTarget>();
            int? all = null;
            foreach (var f in folders)
            {
                var forAll = false;
                var choice = all ?? AskFolderDrop(f, folders.Count > 1, out forAll);
                if (choice < 0) return; // Abbruch: gar nichts hinzufügen
                if (all == null && forAll) all = choice;
                if (choice == 0) asGroups.Add(f);
            }
            foreach (var f in asGroups) AddFolderAsGroup(f.Target, group);
            targets = targets.Except(asGroups).ToList();
            if (targets.Count == 0) return;
        }
        var scanned = new HashSet<string>(Repository.Items.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
        var keys = DropClassifier.AddToSettings(Settings, targets, group, scanned.Contains);
        CommitUserChange();
        AppLog.Info($"Added by drag & drop{(group != null ? " (group " + group + ")" : string.Empty)}: {string.Join(", ", targets.Select(t => t.Target))}");
        if (group == null && keys.Count > 0 && Hosts.Any(h => !h.Config.ShowsFolder(PanelConfig.CustomEntriesKey)))
            Ui.Notify(L.T("Added"), L.T("The entries are under “Custom entries”. Panels with their own content selection only show them if “Custom entries” is checked."));
    }

    private int AskFolderDrop(DroppedTarget folder, bool several, out bool forAll)
    {
        var (choice, all) = WithDialog(() => Dialogs.Choose(L.F("Add folder “{0}”", folder.Name), L.T("How should the folder appear in the panel?"),
            ChoiceOption.FolderDrop, several ? L.T("Apply to all dragged folders") : null));
        forAll = all;
        return choice;
    }

    /// <summary>
    /// Gezogenen Ordner als smarte Gruppe anlegen (in der Gruppe, auf die er gezogen wurde): über den Dialog
    /// „Neue Gruppe“ mit vorausgefülltem Ordner und Namen – so lassen sich gleich Farbe, Symbol und Darstellung wählen.
    /// </summary>
    private void AddFolderAsGroup(string folderPath, string? parent)
    {
        if (NewGroup.FolderError(Settings.Folders, folderPath) is { } error)
        {
            Ui.Notify(L.T("Folder already exists"), error, warning: true);
            return;
        }
        parent = string.IsNullOrEmpty(parent) ? null : parent;
        var name = NewGroup.NameFromFolder(folderPath);
        for (int i = 2; GroupManager.Exists(Settings, parent == null ? name : parent + GroupManager.Separator + name); i++) name = $"{NewGroup.NameFromFolder(folderPath)} {i}";
        var heading = parent == null ? L.T("Add folder as group") : L.F("Folder as subgroup in “{0}”", GroupManager.LeafOf(parent));
        var spec = WithDialog(() => Dialogs.AskNewGroup(heading, name, n => GroupNameError(n, parent),
            f => NewGroup.FolderError(Settings.Folders, f), folderPath));
        if (spec == null) return;
        RememberForUndo(L.T("Folder added as group"));
        var path = NewGroup.Create(Settings, spec, parent);
        SaveSettings();
        Repository.UpdateSettings(Settings);
        CommitUserChange();
        ShowUndoToast(L.F("Folder added as group “{0}”", GroupManager.LeafOf(path)));
        AppLog.Info(string.Format(CultureInfo.InvariantCulture, "Folder added by drag & drop as smart group: {0} → {1}", folderPath, path));
    }

    /// <summary>Symbol einer eigenen Gruppe wählen.</summary>
    /// <summary>Darstellung einer Gruppe: null = wie das Panel.</summary>
    public void SetGroupLayout(string groupPath, ItemLayout? layout)
    {
        GroupManager.SetLayout(Settings, groupPath, layout);
        SaveSettings();
        foreach (var h in Hosts) h.ViewModel.GroupLayoutsChanged();
        SettingsChangedOutside?.Invoke();
    }

    public void SetGroupItemSize(string groupPath, ItemSize? size)
    {
        GroupManager.SetItemSize(Settings, groupPath, size);
        SaveSettings();
        foreach (var h in Hosts) h.ViewModel.GroupLayoutsChanged();
        SettingsChangedOutside?.Invoke();
    }

    /// <summary>Gruppenfarbe setzen (leer = keine bzw. von der übergeordneten Gruppe).</summary>
    public void SetGroupColor(string groupPath, string? color)
    {
        GroupManager.SetColor(Settings, groupPath, color);
        CommitUserChange();
    }

    /// <summary>Eigene Gruppenfarbe als #RRGGBB.</summary>
    public void PickGroupColor(string groupPath)
    {
        var current = GroupManager.OwnColorOf(Settings, groupPath);
        var text = WithDialog(() => Dialogs.PickColor(L.F("Color for “{0}”", GroupManager.LeafOf(groupPath)), current));
        if (text != null) SetGroupColor(groupPath, text);
    }

    public void ChooseGroupIcon(string groupPath)
    {
        var current = GroupManager.IconOf(Settings, groupPath);
        var picked = WithDialog(() => Dialogs.PickIcon(current, L.F("Icon for “{0}”", GroupManager.LeafOf(groupPath))));
        if (picked == null) return;
        GroupManager.SetIcon(Settings, groupPath, picked.Value);
        CommitUserChange();
    }
}
