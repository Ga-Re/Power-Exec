using PowerExec.Core.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;

/// <summary>Eine Zeile der Liste „Eigene Einträge“ in den Einstellungen.</summary>
public sealed class CustomEntryRowViewModel : ObservableObject
{
    private readonly CustomEntryListViewModel _owner;
    private bool _isSelected;

    public CustomEntryRowViewModel(CustomEntryListViewModel owner, CustomEntry entry, string group, IReadOnlyList<string> tags)
    {
        _owner = owner;
        Entry = entry;
        Group = group;
        Tags = tags;
        EditCommand = new RelayCommand(() => _owner.Edit(this));
        RemoveCommand = new RelayCommand(() => _owner.Remove(new[] { this }));
    }

    public CustomEntry Entry { get; private set; }
    public string Group { get; private set; }
    public IReadOnlyList<string> Tags { get; private set; }

    public string Name => string.IsNullOrWhiteSpace(Entry.Name) ? Entry.ToLaunchItem().DisplayName : Entry.Name;
    public string Detail => Entry.Kind == CustomEntryKind.Snippet
        ? (Entry.Language == SnippetLanguage.Text ? "Text · " : "PowerShell · ") + Entry.ToLaunchItem().Synopsis
        : Entry.SummaryText();
    public string KindText => CustomEntry.KindLabel(Entry.Kind);
    public string GroupText => Group.Length == 0 ? "–" : GroupManager.Display(Group);
    public string TagsText => Tags.Count == 0 ? string.Empty : "#" + string.Join(" #", Tags);
    public string Badge => CustomEntryViewModel.PanelBadge(Entry.Kind, Entry.Target);
    public string Icon => Entry.Icon;
    public bool HasIcon => Entry.Icon.Length > 0;

    private IconImage? _image;
    private bool _imageLoaded;
    /// <summary>Automatisches Symbol wie im Panel (Favicon, Programm-, Ordner-, Verknüpfungssymbol).</summary>
    public IconImage? Image
    {
        get
        {
            if (!_imageLoaded && !HasIcon && _owner.ShowImages)
            {
                _imageLoaded = true;
                try
                {
                    _image = ItemViewModel.LoadIcon(_owner.App.IconProvider, Entry.ToLaunchItem(), img =>
                    {
                        _image = img;
                        OnPropertyChanged(nameof(Image));
                        OnPropertyChanged(nameof(HasImage));
                        OnPropertyChanged(nameof(ShowBadge));
                    });
                }
                catch { _image = null; }
            }
            return _image;
        }
    }
    public bool HasImage => !HasIcon && Image != null;
    public bool ShowBadge => !HasIcon && !HasImage;
    public bool SingleClick => _owner.IsSingleClick(Entry.Key);
    public bool HasPlaceholders => Entry.Placeholders.Count > 0;

    private HealthResult? _health;
    public string HealthText => _health == null ? (HealthService.CanCheck(Entry) ? L.T("not checked") : "–") : _health.Message;
    public bool? HealthOk => _health?.Ok;

    public bool IsSelected { get => _isSelected; set { if (Set(ref _isSelected, value)) _owner.OnSelectionChanged(); } }

    public ICommand EditCommand { get; }
    public ICommand RemoveCommand { get; }

    internal void Update(CustomEntry entry, string group, IReadOnlyList<string> tags)
    {
        Entry = entry;
        Group = group;
        Tags = tags;
        _imageLoaded = false;
        _image = null;
        Refresh();
    }

    internal void SetHealth(HealthResult? h)
    {
        _health = h;
        OnPropertyChanged(nameof(HealthText));
        OnPropertyChanged(nameof(HealthOk));
    }

    internal void Refresh()
    {
        foreach (var p in new[] { nameof(Name), nameof(Detail), nameof(KindText), nameof(GroupText), nameof(TagsText), nameof(Tags), nameof(Badge), nameof(Icon),
                     nameof(HasIcon), nameof(Image), nameof(HasImage), nameof(ShowBadge), nameof(SingleClick), nameof(HasPlaceholders), nameof(HealthText), nameof(HealthOk) })
            OnPropertyChanged(p);
    }
}

/// <summary>
/// Kompakte, durchsuch- und filterbare Liste der eigenen Einträge (Arbeitskopie der Einstellungen):
/// ⚙ bearbeitet im Dialog, Mehrfachauswahl für Löschen, Gruppe, Export; Erreichbarkeit prüfen; Import.
/// </summary>
public sealed class CustomEntryListViewModel : ObservableObject
{
    private readonly ICustomEntryListHost _app;
    private readonly AppSettings _s;

    internal ICustomEntryListHost App => _app;
    internal bool ShowImages => _app.Settings.ShowProgramIcons;
    internal bool IsSingleClick(string key) => _s.FindEntry(key)?.SingleClick == true;
    private readonly Func<IReadOnlyList<string>> _groups;
    private string _query = string.Empty;
    private string _kindFilter = "all";
    private string _groupFilter = "*";
    private string _tagFilter = "*";
    private string _moveTarget = string.Empty;
    private bool _checking;
    private readonly HashSet<string> _removedKeys = new(StringComparer.OrdinalIgnoreCase);

    public CustomEntryListViewModel(ICustomEntryListHost app, AppSettings s, Func<IReadOnlyList<string>> groups)
    {
        _app = app;
        _s = s;
        _groups = groups;
        foreach (var e in s.CustomEntries)
            Rows.Add(new CustomEntryRowViewModel(this, e.Clone(), GroupManager.GroupOf(s, e.Key), Core.Config.Tags.Of(s, e.Key).ToList()));
        _rowsView = new FilteredView<CustomEntryRowViewModel>(Rows, Matches, rows => rows.OrderBy(r => r.Name, StringComparer.CurrentCulture));
        foreach (var r in Rows) r.SetHealth(app.Health?.Get(r.Entry.Key));
        _rowsView.Refresh();

        NewCommand = new RelayCommand(p => New(p?.ToString()));
        CatalogCommand = new RelayCommand(AddFromCatalog);
        ImportCommand = new RelayCommand(Import);
        ExportCommand = new RelayCommand(() => _app.ExportEntries(SelectedOrVisible().Select(r => r.Entry).ToList(), null, Owner(), BuildPreview()));
        RemoveSelectedCommand = new RelayCommand(() => Remove(Selected.ToList()), () => Selected.Any());
        MoveSelectedCommand = new RelayCommand(MoveSelected, () => Selected.Any());
        ClearSelectionCommand = new RelayCommand(() => { foreach (var r in Selected.ToList()) r.IsSelected = false; });
        SelectAllCommand = new RelayCommand(() => { foreach (var r in VisibleRows.ToList()) r.IsSelected = true; });
        CheckCommand = new RelayCommand(() => _ = CheckAsync(), () => !_checking);
        SetKindFilterCommand = new RelayCommand(p => KindFilter = p?.ToString() ?? "all");
    }

    public ObservableCollection<CustomEntryRowViewModel> Rows { get; } = new();
    private readonly FilteredView<CustomEntryRowViewModel> _rowsView;
    /// <summary>Einträge, die zu Suche und Filtern passen, nach Name sortiert.</summary>
    public ObservableCollection<CustomEntryRowViewModel> VisibleRows => _rowsView.Items;
    public int Count => Rows.Count;
    public bool IsEmpty => Rows.Count == 0;
    public bool NoMatches => Rows.Count > 0 && VisibleRows.Count == 0;

    public string Query { get => _query; set { if (Set(ref _query, value ?? string.Empty)) Refilter(); } }
    public string KindFilter
    {
        get => _kindFilter;
        set { if (Set(ref _kindFilter, value ?? "all")) { OnPropertyChanged(nameof(MoreKind)); Refilter(); } }
    }

    /// <summary>Weitere Typen im Dropdown neben den vier Chips (Alle, Webseiten, Snippets, RDP/SSH).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> MoreKinds { get; } = new[]
    {
        new KeyValuePair<string, string>("more", L.T("More types …")),
        new KeyValuePair<string, string>("Folder", L.T("Folder")),
        new KeyValuePair<string, string>("Tool", L.T("Tools")),
        new KeyValuePair<string, string>("Command", L.T("Commands")),
        new KeyValuePair<string, string>("File", L.T("Files")),
        new KeyValuePair<string, string>("Rdp", L.T("RDP only")),
        new KeyValuePair<string, string>("Ssh", L.T("SSH only")),
        new KeyValuePair<string, string>("problems", L.T("With problems"))
    };

    /// <summary>Gewählter weiterer Typ („more“ = Hinweistext, solange ein Chip aktiv ist).</summary>
    public string? MoreKind
    {
        get => MoreKinds.Any(k => k.Key == _kindFilter) ? _kindFilter : "more";
        set { if (value != null && value != "more") KindFilter = value; }
    }
    public string GroupFilter
    {
        get => _groupFilter;
        set
        {
            if (value == SeparatorKey) { OnPropertyChanged(); return; } // Trennlinie ist nicht wählbar
            if (Set(ref _groupFilter, value ?? "*")) Refilter();
        }
    }
    public string TagFilter { get => _tagFilter; set { if (Set(ref _tagFilter, value ?? "*")) Refilter(); } }

    /// <summary>Feste Filter, Trennlinie (Schlüssel „—“, nicht wählbar), dann die Gruppen.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> GroupFilters
    {
        get
        {
            var fixedOnes = new[] { new KeyValuePair<string, string>("*", L.T("All groups")), new KeyValuePair<string, string>("", L.T("Without group")) };
            var groups = _groups().Select(g => new KeyValuePair<string, string>(g, GroupManager.Display(g))).ToList();
            return groups.Count == 0 ? fixedOnes : fixedOnes.Append(new KeyValuePair<string, string>(SeparatorKey, string.Empty)).Concat(groups).ToList();
        }
    }
    public const string SeparatorKey = "\u2014sep";

    public IReadOnlyList<KeyValuePair<string, string>> TagFilters =>
        new[] { new KeyValuePair<string, string>("*", L.T("All tags")) }
            .Concat(Rows.SelectMany(r => r.Tags).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(t => t)
                .Select(t => new KeyValuePair<string, string>(t, "#" + t))).ToList();

    public IReadOnlyList<string> MoveTargets => new[] { L.T("(no group)") }.Concat(_groups()).ToList();
    public string MoveTarget { get => _moveTarget; set => Set(ref _moveTarget, value ?? string.Empty); }

    public IEnumerable<CustomEntryRowViewModel> Selected => Rows.Where(r => r.IsSelected);
    public int SelectedCount => Rows.Count(r => r.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public string SelectionText => SelectedCount == 1 ? L.T("1 entry selected") : L.F("{0} entries selected", SelectedCount);
    public string SummaryText
    {
        get
        {
            var visible = VisibleRows.Count;
            var problems = Rows.Count(r => r.HealthOk == false);
            return (visible == Rows.Count ? L.F("{0} entries", Rows.Count) : L.F("{0} of {1} entries", visible, Rows.Count)) +
                   (problems > 0 ? L.F(" · {0} with problems", problems) : string.Empty);
        }
    }
    public string CheckText => _checking ? L.T("Checking …") : L.T("Check reachability");

    public ICommand NewCommand { get; }
    public ICommand CatalogCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand MoveSelectedCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand CheckCommand { get; }
    public ICommand SetKindFilterCommand { get; }

    private bool Matches(CustomEntryRowViewModel r)
    {
        bool kindOk = _kindFilter switch
        {
            "all" => true,
            "problems" => r.HealthOk == false,
            "remote" => r.Entry.Kind is CustomEntryKind.Rdp or CustomEntryKind.Ssh,
            _ => r.Entry.Kind.ToString().Equals(_kindFilter, StringComparison.OrdinalIgnoreCase)
        };
        if (!kindOk) return false;
        if (_groupFilter != "*" && !(_groupFilter.Length == 0 ? r.Group.Length == 0 : r.Group.Length > 0 && GroupManager.IsSameOrBelow(r.Group, _groupFilter))) return false;
        if (_tagFilter != "*" && !r.Tags.Contains(_tagFilter, StringComparer.CurrentCultureIgnoreCase)) return false;
        if (_query.Trim().Length == 0) return true;
        var text = $"{r.Name} {r.Detail} {r.Entry.Code} {r.Entry.Description} {r.Group} {r.TagsText}";
        return _query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => text.Contains(w.TrimStart('#'), StringComparison.CurrentCultureIgnoreCase));
    }

    private void Refilter()
    {
        _rowsView.Refresh();
        OnPropertyChanged(nameof(NoMatches));
        OnPropertyChanged(nameof(SummaryText));
    }

    internal void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
        CommandRequery.Invalidate();
    }

    /// <summary>Schlagworte wurden in der Verwaltung geändert: Zeilen aus der Arbeitskopie neu lesen.</summary>
    public void ReloadTags()
    {
        foreach (var r in Rows) r.Update(r.Entry, r.Group, Core.Config.Tags.Of(_s, r.Entry.Key).ToList());
        CountsChanged();
    }

    private void CountsChanged()
    {
        foreach (var p in new[] { nameof(Count), nameof(IsEmpty), nameof(NoMatches), nameof(SummaryText), nameof(TagFilters), nameof(GroupFilters), nameof(MoveTargets) })
            OnPropertyChanged(p);
        OnSelectionChanged();
    }

    private object? Owner() => _app.SettingsDialogOwner;

    private IEnumerable<CustomEntryRowViewModel> SelectedOrVisible() =>
        Selected.Any() ? Selected : VisibleRows.ToList();

    private void New(string? kindKey) => AddWithEditor(CustomEntry.FromKindKey(kindKey), _groupFilter is "*" ? string.Empty : _groupFilter);

    private void AddWithEditor(CustomEntry? template, string group)
    {
        ApplyTo(_s); // Liste → Arbeitskopie, damit der Dialog den aktuellen Stand sieht
        var result = _app.EditEntryModal(_s, template, isNew: true, group, Owner());
        if (result.Alternative == "catalog") { AddFromCatalog(); return; }
        if (result.Alternative == "files") { AddFiles(group); return; }
        if (result.SmartGroupPath is { } folder) { _app.CreateSmartGroupFor(folder, group.Length > 0 ? group : null); return; }
        var key = result.SavedKey;
        if (key == null) return;
        var e = _s.CustomEntries.First(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        Rows.Add(new CustomEntryRowViewModel(this, e.Clone(), GroupManager.GroupOf(_s, key), Core.Config.Tags.Of(_s, key).ToList()));
        CountsChanged();
    }

    /// <summary>Dateien auswählen und als eigene Einträge (Art „Datei“) übernehmen.</summary>
    private void AddFiles(string group)
    {
        var files = _app.Dialogs.OpenFiles(
            L.T("Scripts and programs (*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc)|*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc|All files (*.*)|*.*"), owner: Owner());
        if (files.Count == 0) return;
        foreach (var f in files)
        {
            if (Rows.Any(r => r.Entry.Key.Equals(f, StringComparison.OrdinalIgnoreCase))) continue;
            Rows.Add(new CustomEntryRowViewModel(this, new CustomEntry { Kind = CustomEntryKind.File, Target = f }, group, Array.Empty<string>()));
        }
        CountsChanged();
    }

    /// <summary>⚙: derselbe Eintragsdialog wie im Panel (Eintrag, Gruppe, Schlagworte, Notiz, Startprofile).</summary>
    internal void Edit(CustomEntryRowViewModel row)
    {
        ApplyTo(_s);
        var oldKey = row.Entry.Key;
        var key = _app.EditEntryModal(_s, row.Entry, isNew: false, group: null, Owner()).SavedKey;
        if (key == null) return;
        if (!key.Equals(oldKey, StringComparison.OrdinalIgnoreCase)) _removedKeys.Add(oldKey);
        var e = _s.CustomEntries.First(c => c.Id == row.Entry.Id);
        row.Update(e.Clone(), GroupManager.GroupOf(_s, key), Core.Config.Tags.Of(_s, key).ToList());
        row.SetHealth(null);
        Refilter();
        CountsChanged();
    }

    internal void Remove(IReadOnlyList<CustomEntryRowViewModel> rows)
    {
        if (rows.Count == 0) return;
        var text = rows.Count == 1 ? L.F("Remove “{0}”?", rows[0].Name) : L.F("Remove {0} entries?", rows.Count);
        if (!_app.Dialogs.Confirm(text + "\n\nDateien und Ordner selbst bleiben unverändert.", L.T("Remove"), owner: Owner(), defaultNo: true)) return;
        foreach (var r in rows)
        {
            _removedKeys.Add(r.Entry.Key);
            Rows.Remove(r);
        }
        CountsChanged();
    }

    private void MoveSelected()
    {
        var target = _moveTarget == L.T("(no group)") ? string.Empty : GroupManager.Normalize(_moveTarget);
        foreach (var r in Selected.ToList()) r.Update(r.Entry, target, r.Tags);
        Refilter();
        CountsChanged();
    }

    private void AddFromCatalog()
    {
        var chosen = _app.Dialogs.PickCatalogTemplates();
        if (chosen == null) return;
        var group = _groupFilter is "*" ? string.Empty : _groupFilter;
        foreach (var t in chosen)
        {
            var e = ToolCatalog.Create(t);
            if (Rows.Any(r => r.Entry.Kind == e.Kind && r.Entry.Target == e.Target && r.Entry.Arguments == e.Arguments && r.Entry.Code == e.Code)) continue;
            Rows.Add(new CustomEntryRowViewModel(this, e, group, Array.Empty<string>()));
        }
        CountsChanged();
    }

    private void Import()
    {
        var path = _app.Dialogs.OpenFile(EntryShare.FileFilter + L.T("|All files (*.*)|*.*"), owner: Owner());
        if (path == null) return;
        try
        {
            var file = EntryShare.Parse(File.ReadAllText(path));
            var preview = BuildPreview();
            var (added, updated) = EntryShare.Import(preview, file);
            foreach (var e in preview.CustomEntries)
            {
                var existing = Rows.FirstOrDefault(r => r.Entry.Id == e.Id);
                var group = GroupManager.GroupOf(preview, e.Key);
                var tags = Core.Config.Tags.Of(preview, e.Key).ToList();
                if (existing != null) existing.Update(e, group, tags);
                else Rows.Add(new CustomEntryRowViewModel(this, e, group, tags));
            }
            // Neue Gruppen (und Symbole) in die Arbeitskopie übernehmen
            _s.CategoryOrder = GroupManager.All(preview).ToList();
            foreach (var kv in preview.GroupIcons) _s.GroupIcons[kv.Key] = kv.Value;
            foreach (var cfg in preview.Entries.Where(c => c.Profiles.Count > 0))
                if (_s.FindEntry(cfg.Path) is { } target) target.Profiles = cfg.Profiles.Select(p => p.Clone()).ToList();
                else _s.Entries.Add(cfg.Clone());
            CountsChanged();
            _app.Dialogs.Inform(L.F("{0} new and {1} updated entries imported.", added, updated), L.T("Import"), Owner());
        }
        catch (Exception ex)
        {
            _app.Dialogs.Warn(L.T("The file couldn't be read:\n") + ex.Message, L.T("Import"), Owner());
        }
    }

    private async Task CheckAsync()
    {
        _checking = true;
        OnPropertyChanged(nameof(CheckText));
        CommandRequery.Invalidate();
        try
        {
            var rows = SelectedOrVisible().ToList();
            await _app.Health.CheckAsync(rows.Select(r => r.Entry), force: true);
            foreach (var r in rows) r.SetHealth(_app.Health.Get(r.Entry.Key));
        }
        finally
        {
            _checking = false;
            OnPropertyChanged(nameof(CheckText));
            Refilter();
            CommandRequery.Invalidate();
        }
    }

    /// <summary>Einstellungen mit dem aktuellen Stand der Liste (für Export und Speichern).</summary>
    private AppSettings BuildPreview()
    {
        var copy = SettingsStore.Clone(_s);
        ApplyTo(copy);
        return copy;
    }

    /// <summary>Liste in die Einstellungen schreiben (beim Speichern).</summary>
    /// <summary>
    /// Stand aus den Einstellungen übernehmen, nachdem sie außerhalb der Liste geändert wurden (z. B. Marktplatz):
    /// neue Einträge mit Gruppe und Schlagworten aufnehmen, entfernte streichen, Gruppen nachziehen.
    /// </summary>
    public void SyncFrom(AppSettings s)
    {
        var byKey = s.CustomEntries.ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var gone in Rows.Where(r => !byKey.ContainsKey(r.Entry.Key)).ToList()) Rows.Remove(gone);
        foreach (var e in s.CustomEntries)
        {
            var row = Rows.FirstOrDefault(r => r.Entry.Key.Equals(e.Key, StringComparison.OrdinalIgnoreCase));
            if (row == null)
            {
                row = new CustomEntryRowViewModel(this, e.Clone(), GroupManager.GroupOf(s, e.Key), Core.Config.Tags.Of(s, e.Key).ToList());
                row.SetHealth(_app.Health?.Get(row.Entry.Key));
                Rows.Add(row);
            }
            else if (!ReferenceEquals(row.Entry, e) && Marketplace.IsMarketplaceEntry(s, e.Id))
                row.Update(e.Clone(), GroupManager.GroupOf(s, e.Key), Core.Config.Tags.Of(s, e.Key).ToList());
        }
        _rowsView.Refresh();
        foreach (var p in new[] { nameof(Count), nameof(IsEmpty), nameof(NoMatches) }) OnPropertyChanged(p);
    }

    public void ApplyTo(AppSettings target)
    {
        target.CustomEntries = Rows.Select(r => r.Entry.Clone()).ToList();
        var keys = new HashSet<string>(target.CustomEntries.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var removed in _removedKeys.Where(k => !keys.Contains(k)))
        {
            target.Entries.RemoveAll(e => e.Path.Equals(removed, StringComparison.OrdinalIgnoreCase) && CustomEntry.IsCustomKey(removed));
            target.Favorites.RemoveAll(p => p.Equals(removed, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var r in Rows)
        {
            GroupManager.Assign(target, r.Entry.Key, r.Group);
            Core.Config.Tags.Set(target, r.Entry.Key, r.Tags);
        }
    }
}
