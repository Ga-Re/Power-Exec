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

/// <content>Einträge: finden, beschreiben, Platzhalter auflösen, anlegen, bearbeiten, exportieren, Favoriten.</content>
public partial class AppController
{
    // ---- Aktionen -------------------------------------------------------

    /// <summary>Eintrag zu einem Pfad (Datei aus einem Ordner, eigener Eintrag „custom:…“ oder eigene Datei).</summary>
    public LaunchItem? FindItem(string path)
    {
        var scanned = Repository.Items.FirstOrDefault(i => i.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (scanned != null) return scanned;
        var custom = Settings.CustomEntries.FirstOrDefault(c => c.Key.Equals(path, StringComparison.OrdinalIgnoreCase));
        return custom == null ? null : DescribeCustom(custom);
    }

    /// <summary>Eigener Eintrag als <see cref="LaunchItem"/>; bei Dateien und PowerShell-Snippets mit Metadaten (Synopsis, Parameter).</summary>
    public LaunchItem DescribeCustom(CustomEntry entry)
    {
        var item = entry.ToLaunchItem();
        if (entry.Kind == CustomEntryKind.File) return Repository.DescribeSingle(item);
        if (entry.Kind == CustomEntryKind.Snippet && entry.Language == SnippetLanguage.PowerShell) return DescribeSnippet(entry, item);
        return item;
    }

    private readonly Dictionary<string, (string Code, LaunchItem Item)> _snippetCache = new();

    /// <summary>Legt den Snippet-Code als .ps1 ab (UTF-8 mit BOM für Windows PowerShell) und liest param() und Hilfe daraus.</summary>
    private LaunchItem DescribeSnippet(CustomEntry entry, LaunchItem item)
    {
        try
        {
            if (!_snippetCache.TryGetValue(entry.Id, out var cached) || cached.Code != entry.Code || !File.Exists(entry.SnippetPath))
            {
                Directory.CreateDirectory(CustomEntry.SnippetFolder);
                File.WriteAllText(entry.SnippetPath, entry.Code, new System.Text.UTF8Encoding(true));
                var loaded = Repository.LoadFile(entry.SnippetPath, PanelConfig.CustomEntriesKey).Rebased(entry.Key, PanelConfig.CustomEntriesKey);
                cached = (entry.Code, loaded);
                _snippetCache[entry.Id] = cached;
            }
            var result = cached.Item.Rebased(entry.Key, PanelConfig.CustomEntriesKey);
            result.DisplayName = item.DisplayName;
            result.Synopsis = entry.Description.Trim().Length > 0 ? entry.Description.Trim() : (result.Synopsis.Length > 0 ? result.Synopsis : item.Synopsis);
            result.LaunchTarget = entry.SnippetPath;
            result.CustomKind = CustomEntryKind.Snippet;
            result.Source = entry;
            return result;
        }
        catch (Exception ex)
        {
            AppLog.Error("Snippet couldn't be prepared", ex);
            return item;
        }
    }

    /// <summary>Snippet-Ordner: unter ProgramData, damit auch andere Konten („Als …“) die Datei lesen können.</summary>
    private static void SetupSnippetFolder()
    {
        foreach (var root in new[] { Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(root), "Power-Exec", "Snippets");
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".probe");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                CustomEntry.SnippetFolder = dir;
                foreach (var f in Directory.EnumerateFiles(dir, "*-run-*.ps1"))
                    try { if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromDays(1)) File.Delete(f); } catch { /* alte Laufdatei noch in Benutzung – beim nächsten Start erneut */ }
                return;
            }
            catch { /* Ordner nicht beschreibbar: nächsten Kandidaten versuchen */ }
        }
    }

    /// <summary>Aktuellen Stand eines Eintrags holen (Parameter eines Scripts können sich geändert haben).</summary>
    private LaunchItem Refresh(LaunchItem item)
    {
        if (item.PlaceholdersResolved) return item;
        if (item.Source != null)
        {
            var entry = Settings.CustomEntries.FirstOrDefault(c => c.Id == item.Source.Id) ?? item.Source;
            return DescribeCustom(entry);
        }
        return item.IsPowerShell ? Repository.Reload(item) : item;
    }

    /// <summary>
    /// Platzhalter ({{Name}}) abfragen und einsetzen. Liefert null bei Abbruch.
    /// Snippets bekommen dafür eine eigene .ps1 für genau diesen Start.
    /// </summary>
    private LaunchItem? ResolvePlaceholders(LaunchItem item, IReadOnlyDictionary<string, string>? preset = null)
    {
        if (item.PlaceholdersResolved || item.Source is not { } entry || !entry.NeedsResolving) return item;
        // {{$Variablen}} müssen angelegt sein (normale {{Platzhalter}} werden abgefragt)
        var unknown = Variables.Unknown(Settings, entry.Target, entry.Arguments, entry.Code, entry.User, entry.RemoteCommand, entry.WorkingDirectory);
        if (unknown.Count > 0)
        {
            Dialogs.Warn(L.F("“{0}” uses {1} – this variable doesn't exist. Please create it under Settings › Content › Variables or correct the entry.", item.DisplayName, string.Join(", ", unknown.Select(n => "{{" + n + "}}"))), L.T("Variable missing"));
            return null;
        }
        IReadOnlyDictionary<string, string>? values = preset ?? new Dictionary<string, string>();
        if (preset == null && entry.Placeholders.Count > 0) // eingebaute Variablen ({{$Date}} …) brauchen keine Abfrage
            values = WithDialog(() => Dialogs.AskPlaceholders(item.DisplayName, entry.Placeholders,
                name => ParameterHistory.Get(Settings, entry.Key, name)));
        if (values == null) return null;
        foreach (var kv in values) ParameterHistory.Add(Settings, entry.Key, kv.Key, kv.Value);
        SaveSettings();
        CustomEntry resolved;
        try { resolved = entry.WithValues(values); }
        catch (Exception ex)
        {
            Dialogs.Warn(L.F("A variable of “{0}” couldn't be determined: {1}", item.DisplayName, ex.Message), L.T("Variable"));
            return null;
        }
        LaunchItem result;
        if (resolved.Kind == CustomEntryKind.Snippet && resolved.Language == SnippetLanguage.PowerShell)
        {
            var runPath = Path.Combine(CustomEntry.SnippetFolder, $"{entry.Id}-run-{DateTime.Now:yyyyMMddHHmmssfff}.ps1");
            File.WriteAllText(runPath, resolved.Code, new System.Text.UTF8Encoding(true));
            result = Repository.LoadFile(runPath, PanelConfig.CustomEntriesKey).Rebased(entry.Key, PanelConfig.CustomEntriesKey);
            result.DisplayName = item.DisplayName;
            result.LaunchTarget = runPath;
            result.CustomKind = CustomEntryKind.Snippet;
        }
        else
        {
            result = resolved.ToLaunchItem();
            result.DisplayName = item.DisplayName;
        }
        result.Source = resolved;
        result.PlaceholdersResolved = true;
        return result;
    }

    /// <summary>Snippet in die Zwischenablage (Platzhalter vorher abfragen).</summary>
    public void CopySnippet(LaunchItem item, bool record = true)
    {
        if (item.Source is not { Kind: CustomEntryKind.Snippet }) return;
        var resolved = ResolvePlaceholders(item);
        if (resolved?.Source == null) return;
        try
        {
            Shell.CopyToClipboard(resolved.Source.Code, resolved.Source.CopyAsHtml);
            if (record) RecordRun(item, ProfileResolver.GetDefault(Settings, item));
            HidePanels();
            if (resolved.Source.PasteAfterCopy) PasteAsync(item.DisplayName).Forget();
            else Ui.Notify(L.T("Copied to the clipboard"), item.DisplayName);
        }
        catch (Exception ex)
        {
            Dialogs.Warn(L.T("The clipboard is busy right now: ") + ex.Message, "Power-Exec");
        }
    }

    private async Task PasteAsync(string name)
    {
        if (!await Ui.PasteIntoPreviousAsync())
            Ui.Notify(L.T("Copied to the clipboard"), L.F("{0}: No target window found – paste with Ctrl+V.", name));
    }

    /// <summary>Neuer Eintrag aus einem Menü: Art-Schlüssel („Url“, „TextSnippet“ …) oder null = Art im Dialog wählen.</summary>
    public void NewCustomEntry(string? kindKey, string? group)
    {
        var template = CustomEntry.FromKindKey(kindKey);
        var result = WithDialog(() => Ui.EditEntry(Settings, template, isNew: true, group, null, null));
        HandleEditorResult(result, group);
    }

    /// <summary>Eintrag vorbelegt anlegen (Zwischenablage, Vorlage, Duplikat) – der Eintragsdialog zeigt ihn zum Bestätigen.</summary>
    public bool NewCustomEntry(CustomEntry template, string? group, IEnumerable<string>? tags = null)
    {
        var result = WithDialog(() => Ui.EditEntry(Settings, template, isNew: true, group, tags, null));
        return HandleEditorResult(result, group);
    }

    /// <summary>Ergebnis des Eintragsdialogs: gespeichert, oder Vorlagen / Dateien / smarte Gruppe gewählt.</summary>
    private bool HandleEditorResult(EntryEditorResult result, string? group)
    {
        if (result.SavedKey != null) { CommitUserChange(); return true; }
        switch (result.Alternative)
        {
            case "catalog": AddFromCatalog(group); break;
            case "files": AddFilesDialog(group); break;
        }
        if (result.SmartGroupPath is { } path) CreateSmartGroupFor(path, group);
        return false;
    }

    /// <summary>Dateien auswählen und als Einträge anlegen.</summary>
    public void AddFilesDialog(string? group)
    {
        var files = WithDialog(() => Dialogs.OpenFiles(
            L.T("Scripts and programs (*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc)|*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc|All files (*.*)|*.*"),
            group != null ? L.F("Add files to group “{0}”", GroupManager.LeafOf(group)) : L.T("Add files")));
        if (files.Count > 0)
            AddDropped(files.Select(f => new DroppedTarget(CustomEntryKind.File, f, Path.GetFileName(f))).ToList(), group);
    }

    /// <summary>Ordner als smarte Gruppe anlegen (aus dem Eintragsdialog).</summary>
    public void CreateSmartGroupFor(string folderPath, string? parent)
    {
        if (Settings.Folders.FirstOrDefault(f => f.Path.Equals(folderPath, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            Dialogs.Inform(L.F("This folder already belongs to group “{0}”.", existing.Group), L.T("Smart group"));
            return;
        }
        SmartGroups.Add(Settings, folderPath, null, string.IsNullOrEmpty(parent) ? null : parent);
        SaveSettings();
        Repository.UpdateSettings(Settings);
        CommitUserChange();
    }

    /// <summary>Eintrag aus dem Dialog testen – mit dem eingestellten Standardverhalten, ohne „Zuletzt ausgeführt“.</summary>
    public void TestRun(CustomEntry entry, LaunchProfile profile)
    {
        var item = DescribeCustom(entry);
        RunProfile(item, profile, null, record: false);
    }

    /// <summary>Eigenen Eintrag bearbeiten – im selben Dialog wie die Startprofile.</summary>
    public void EditCustomEntry(LaunchItem item)
    {
        if (item.Source is { Kind: CustomEntryKind.Workflow } wf) EditWorkflow(wf);
        else ShowProfiles(item);
    }

    /// <summary>Kopie eines eigenen Eintrags anlegen (gleiche Gruppe, Schlagworte, Profile), Dialog zum Anpassen.</summary>
    public void DuplicateCustomEntry(LaunchItem item)
    {
        var entry = Settings.CustomEntries.FirstOrDefault(c => c.Key.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        if (entry == null || entry.Kind == CustomEntryKind.File) return;
        var copy = entry.Duplicate();
        var key = WithDialog(() => Ui.EditEntry(Settings, copy, isNew: true,
            GroupManager.GroupOf(Settings, entry.Key), Tags.Of(Settings, entry.Key), null).SavedKey);
        if (key == null) return;
        var source = Settings.FindEntry(entry.Key);
        if (source != null && source.Profiles.Count > 0 && Settings.FindEntry(key) is { Profiles.Count: 0 } target)
            target.Profiles = source.Profiles.Select(p => p.Clone()).ToList();
        CommitUserChange();
    }

    private EntryConfig AddEntry(string key)
    {
        var e = new EntryConfig { Path = key };
        Settings.Entries.Add(e);
        return e;
    }

    public void ToggleSingleClick(LaunchItem item)
    {
        var entry = Settings.FindEntry(item.FullPath) ?? AddEntry(item.FullPath);
        entry.SingleClick = !entry.SingleClick;
        CommitUserChange();
    }

    /// <summary>Strg+V bzw. „Aus Zwischenablage“: Inhalt erkennen und als Eintrag vorschlagen.</summary>
    public void AddFromClipboard(string? group)
    {
        string? text = null;
        try
        {
            var files = Shell.GetClipboardFiles();
            if (files.Count > 0)
            {
                AddDropped(files.Select(f => DropClassifier.Classify(f, Directory.Exists, p => { try { return File.ReadAllText(p); } catch { return null; } })).ToList(), group);
                return;
            }
            text = Shell.GetClipboardText();
        }
        catch { /* Zwischenablage gerade von einem anderen Programm gesperrt: wie leer behandeln */ }
        if (string.IsNullOrWhiteSpace(text))
        {
            Ui.Notify(L.T("Clipboard empty"), L.T("Copy an address, a path, a computer name or code and try again."));
            return;
        }
        NewCustomEntry(ClipboardClassifier.Suggest(text, Directory.Exists, File.Exists), group);
    }

    /// <summary>Vorlagen-Katalog: ausgewählte Vorlagen in die Gruppe übernehmen.</summary>
    public void AddFromCatalog(string? group)
    {
        var chosen = WithDialog(() => Dialogs.PickCatalogTemplates());
        if (chosen == null || chosen.Count == 0) return;
        foreach (var t in chosen)
        {
            var e = ToolCatalog.Create(t);
            if (Settings.CustomEntries.Any(c => c.Kind == e.Kind && c.Target == e.Target && c.Arguments == e.Arguments && c.Code == e.Code)) continue;
            Settings.CustomEntries.Add(e);
            if (!string.IsNullOrEmpty(group)) GroupManager.Assign(Settings, e.Key, group);
        }
        CommitUserChange();
    }

    /// <summary>Eigene Einträge als Datei speichern (z. B. eine Gruppe für Kollegen).</summary>
    public void ExportEntries(IReadOnlyList<CustomEntry> entries, string? rootGroup, object? owner = null, AppSettings? source = null)
    {
        if (entries.Count == 0)
        {
            Dialogs.Inform(L.T("No custom entries are selected."), L.T("Export"), owner);
            return;
        }
        var fileName = WithDialog(() => Dialogs.SaveFile(EntryShare.FileFilter,
            (rootGroup != null ? GroupManager.LeafOf(rootGroup) : L.T("Custom entries")) + ".pxentries.json", owner: owner));
        if (fileName == null) return;
        try
        {
            File.WriteAllText(fileName, EntryShare.Export(source ?? Settings, entries, rootGroup));
            AppLog.Info(string.Format("{0} custom entries exported: {1}", entries.Count, fileName));
            Ui.Notify(L.T("Exported"), (entries.Count == 1 ? L.T("1 entry saved.") : L.F("{0} entries saved.", entries.Count)));
        }
        catch (Exception ex)
        {
            Dialogs.Warn(ex.Message, L.T("Export"), owner);
        }
    }

    public void ExportGroup(string groupPath) =>
        ExportEntries(Settings.CustomEntries.Where(c => GroupManager.GroupOf(Settings, c.Key) is { Length: > 0 } g && GroupManager.IsSameOrBelow(g, groupPath)).ToList(), groupPath);

    /// <returns>true, wenn etwas importiert wurde.</returns>
    public bool ImportEntries(string? intoGroup = null, object? owner = null)
    {
        var fileName = WithDialog(() => Dialogs.OpenFile(EntryShare.FileFilter + L.T("|All files (*.*)|*.*"), owner: owner));
        if (fileName == null) return false;
        try
        {
            var file = EntryShare.Parse(File.ReadAllText(fileName));
            var (added, updated) = EntryShare.Import(Settings, file, intoGroup);
            CommitUserChange();
            AppLog.Info(string.Format("Custom entries imported ({0} new, {1} updated): {2}", added, updated, fileName));
            Dialogs.Inform(L.F("{0} new and {1} updated entries imported.", added, updated), L.T("Import"), owner);
            return added + updated > 0;
        }
        catch (Exception ex)
        {
            Dialogs.Warn(L.T("The file couldn't be read:\n") + ex.Message, L.T("Import"), owner);
            return false;
        }
    }

    public void RemoveCustomEntry(LaunchItem item)
    {
        var entry = Settings.CustomEntries.FirstOrDefault(c => c.Key.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return;
        var name = Settings.FindEntry(item.FullPath)?.DisplayName is { Length: > 0 } dn ? dn : item.DisplayName;
        // Keine Rückfrage mehr: der Hinweis bietet „Rückgängig“
        RememberForUndo(L.F("“{0}” removed", name));
        Settings.CustomEntries.Remove(entry);
        Settings.Entries.RemoveAll(e => e.Path.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
        Settings.Favorites.RemoveAll(p => p.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
        Settings.RecentRuns.RemoveAll(r => r.Path.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
        RunStats.Remove(Settings, entry.Key);
        CommitUserChange();
        ShowUndoToast(L.F("“{0}” removed", name) + (entry.Kind is CustomEntryKind.File or CustomEntryKind.Folder ? L.T(" (the file or folder is kept)") : string.Empty));
    }

    public bool IsFavorite(string path) => Settings.Favorites.Contains(path, StringComparer.OrdinalIgnoreCase);

    public void ToggleFavorite(LaunchItem item)
    {
        var wasFavorite = IsFavorite(item.FullPath);
        if (wasFavorite)
        {
            RememberForUndo(L.T("Favorite removed"));
            Settings.Favorites.RemoveAll(p => p.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        }
        else
            Settings.Favorites.Add(item.FullPath);
        CommitUserChange();
        if (wasFavorite) ShowUndoToast(L.F("“{0}” is no longer a favorite", DisplayNameOf(item)));
    }

    public FolderConfig? FolderOf(LaunchItem item) =>
        Settings.Folders.FirstOrDefault(f => f.Path.Equals(item.RootFolder, StringComparison.OrdinalIgnoreCase));

    /// <summary>Blendet einen Eintrag aus (entfernt den Haken in den Ordner-Einstellungen).</summary>
    public void HideItem(LaunchItem item)
    {
        var folder = FolderOf(item);
        if (folder == null) return;
        RememberForUndo(L.F("“{0}” hidden", item.FileName));
        FolderVisibility.SetVisible(folder, FolderVisibility.RelativePath(folder, item.FullPath), false);
        Settings.Favorites.RemoveAll(p => p.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
        CommitUserChange();
        ShowUndoToast(L.F("“{0}” hidden", DisplayNameOf(item)));
    }
}
