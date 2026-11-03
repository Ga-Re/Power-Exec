using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

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
    public bool SingleClick => Entry.SingleClick;
    public bool HasPlaceholders => Entry.Placeholders.Count > 0;

    private HealthResult? _health;
    public string HealthText => _health == null ? (HealthService.CanCheck(Entry) ? "nicht geprüft" : "–") : _health.Message;
    public bool? HealthOk => _health?.Ok;

    public bool IsSelected { get => _isSelected; set { if (Set(ref _isSelected, value)) _owner.OnSelectionChanged(); } }

    public ICommand EditCommand { get; }
    public ICommand RemoveCommand { get; }

    internal void Update(CustomEntry entry, string group, IReadOnlyList<string> tags)
    {
        Entry = entry;
        Group = group;
        Tags = tags;
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
        foreach (var p in new[] { nameof(Name), nameof(Detail), nameof(KindText), nameof(GroupText), nameof(TagsText), nameof(Badge), nameof(Icon),
                     nameof(HasIcon), nameof(SingleClick), nameof(HasPlaceholders), nameof(HealthText), nameof(HealthOk) })
            OnPropertyChanged(p);
    }
}

/// <summary>
/// Kompakte, durchsuch- und filterbare Liste der eigenen Einträge (Arbeitskopie der Einstellungen):
/// ⚙ bearbeitet im Dialog, Mehrfachauswahl für Löschen, Gruppe, Export; Erreichbarkeit prüfen; Import.
/// </summary>
public sealed class CustomEntryListViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly AppSettings _s;
    private readonly Func<IReadOnlyList<string>> _groups;
    private string _query = string.Empty;
    private string _kindFilter = "all";
    private string _groupFilter = "*";
    private string _tagFilter = "*";
    private string _moveTarget = string.Empty;
    private bool _checking;
    private readonly HashSet<string> _removedKeys = new(StringComparer.OrdinalIgnoreCase);

    public CustomEntryListViewModel(AppController app, AppSettings s, Func<IReadOnlyList<string>> groups)
    {
        _app = app;
        _s = s;
        _groups = groups;
        foreach (var e in s.CustomEntries)
            Rows.Add(new CustomEntryRowViewModel(this, e.Clone(), GroupManager.GroupOf(s, e.Key), Core.Config.Tags.Of(s, e.Key).ToList()));
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => o is CustomEntryRowViewModel r && Matches(r);
        RowsView.SortDescriptions.Add(new SortDescription(nameof(CustomEntryRowViewModel.Name), ListSortDirection.Ascending));
        foreach (var r in Rows) r.SetHealth(app.Health?.Get(r.Entry.Key));

        NewCommand = new RelayCommand(p => New(Enum.TryParse<CustomEntryKind>(p?.ToString(), out var k) ? k : CustomEntryKind.Url));
        CatalogCommand = new RelayCommand(AddFromCatalog);
        ImportCommand = new RelayCommand(Import);
        ExportCommand = new RelayCommand(() => _app.ExportEntries(SelectedOrVisible().Select(r => r.Entry).ToList(), null, Owner(), BuildPreview()));
        RemoveSelectedCommand = new RelayCommand(() => Remove(Selected.ToList()), () => Selected.Any());
        MoveSelectedCommand = new RelayCommand(MoveSelected, () => Selected.Any());
        ClearSelectionCommand = new RelayCommand(() => { foreach (var r in Selected.ToList()) r.IsSelected = false; });
        SelectAllCommand = new RelayCommand(() => { foreach (var r in RowsView.Cast<CustomEntryRowViewModel>()) r.IsSelected = true; });
        CheckCommand = new RelayCommand(() => _ = CheckAsync(), () => !_checking);
        SetKindFilterCommand = new RelayCommand(p => KindFilter = p?.ToString() ?? "all");
    }

    public ObservableCollection<CustomEntryRowViewModel> Rows { get; } = new();
    public ICollectionView RowsView { get; }
    public int Count => Rows.Count;
    public bool IsEmpty => Rows.Count == 0;
    public bool NoMatches => Rows.Count > 0 && !RowsView.Cast<object>().Any();

    public string Query { get => _query; set { if (Set(ref _query, value ?? string.Empty)) Refilter(); } }
    public string KindFilter { get => _kindFilter; set { if (Set(ref _kindFilter, value)) Refilter(); } }
    public string GroupFilter { get => _groupFilter; set { if (Set(ref _groupFilter, value ?? "*")) Refilter(); } }
    public string TagFilter { get => _tagFilter; set { if (Set(ref _tagFilter, value ?? "*")) Refilter(); } }

    public IReadOnlyList<KeyValuePair<string, string>> GroupFilters =>
        new[] { new KeyValuePair<string, string>("*", "Alle Gruppen"), new KeyValuePair<string, string>("", "Ohne Gruppe") }
            .Concat(_groups().Select(g => new KeyValuePair<string, string>(g, GroupManager.Display(g)))).ToList();

    public IReadOnlyList<KeyValuePair<string, string>> TagFilters =>
        new[] { new KeyValuePair<string, string>("*", "Alle Schlagworte") }
            .Concat(Rows.SelectMany(r => r.Tags).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(t => t)
                .Select(t => new KeyValuePair<string, string>(t, "#" + t))).ToList();

    public IReadOnlyList<string> MoveTargets => new[] { "(ohne Gruppe)" }.Concat(_groups()).ToList();
    public string MoveTarget { get => _moveTarget; set => Set(ref _moveTarget, value ?? string.Empty); }

    public IEnumerable<CustomEntryRowViewModel> Selected => Rows.Where(r => r.IsSelected);
    public int SelectedCount => Rows.Count(r => r.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public string SelectionText => SelectedCount == 1 ? "1 Eintrag ausgewählt" : $"{SelectedCount} Einträge ausgewählt";
    public string SummaryText
    {
        get
        {
            var visible = RowsView.Cast<object>().Count();
            var problems = Rows.Count(r => r.HealthOk == false);
            return (visible == Rows.Count ? $"{Rows.Count} Einträge" : $"{visible} von {Rows.Count} Einträgen") +
                   (problems > 0 ? $" · {problems} mit Problemen" : string.Empty);
        }
    }
    public string CheckText => _checking ? "Prüfe …" : "Erreichbarkeit prüfen";

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
        RowsView.Refresh();
        OnPropertyChanged(nameof(NoMatches));
        OnPropertyChanged(nameof(SummaryText));
    }

    internal void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void CountsChanged()
    {
        foreach (var p in new[] { nameof(Count), nameof(IsEmpty), nameof(NoMatches), nameof(SummaryText), nameof(TagFilters), nameof(GroupFilters), nameof(MoveTargets) })
            OnPropertyChanged(p);
        OnSelectionChanged();
    }

    private static Window? Owner() => Application.Current.Windows.OfType<Views.SettingsWindow>().FirstOrDefault();

    private IEnumerable<CustomEntryRowViewModel> SelectedOrVisible() =>
        Selected.Any() ? Selected : RowsView.Cast<CustomEntryRowViewModel>();

    private void New(CustomEntryKind kind) => AddWithEditor(new CustomEntry { Kind = kind }, _groupFilter is "*" ? string.Empty : _groupFilter);

    private void AddWithEditor(CustomEntry template, string group)
    {
        var r = Views.CustomEntryWindow.Edit(_groups(), template, isNew: true, group, Array.Empty<string>(), Owner());
        if (r == null) return;
        var row = new CustomEntryRowViewModel(this, r.Entry, r.Group, r.Tags);
        Rows.Add(row);
        CountsChanged();
    }

    internal void Edit(CustomEntryRowViewModel row)
    {
        var r = Views.CustomEntryWindow.Edit(_groups(), row.Entry, isNew: false, row.Group, row.Tags, Owner());
        if (r == null) return;
        if (!r.Entry.Key.Equals(row.Entry.Key, StringComparison.OrdinalIgnoreCase)) _removedKeys.Add(row.Entry.Key);
        row.Update(r.Entry, r.Group, r.Tags);
        row.SetHealth(null);
        Refilter();
        CountsChanged();
    }

    internal void Remove(IReadOnlyList<CustomEntryRowViewModel> rows)
    {
        if (rows.Count == 0) return;
        var text = rows.Count == 1 ? $"„{rows[0].Name}“ entfernen?" : $"{rows.Count} Einträge entfernen?";
        if (MessageBox.Show(Owner()!, text + "\n\nDateien und Ordner selbst bleiben unverändert.", "Entfernen",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        foreach (var r in rows)
        {
            _removedKeys.Add(r.Entry.Key);
            Rows.Remove(r);
        }
        CountsChanged();
    }

    private void MoveSelected()
    {
        var target = _moveTarget == "(ohne Gruppe)" ? string.Empty : GroupManager.Normalize(_moveTarget);
        foreach (var r in Selected.ToList()) r.Update(r.Entry, target, r.Tags);
        Refilter();
        CountsChanged();
    }

    private void AddFromCatalog()
    {
        var chosen = Views.CatalogWindow.Pick();
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
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = EntryShare.FileFilter + "|Alle Dateien (*.*)|*.*" };
        if (dlg.ShowDialog(Owner()) != true) return;
        try
        {
            var file = EntryShare.Parse(File.ReadAllText(dlg.FileName));
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
            MessageBox.Show(Owner()!, $"{added} neue und {updated} aktualisierte Einträge übernommen.", "Importieren",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Owner()!, "Die Datei konnte nicht gelesen werden:\n" + ex.Message, "Importieren", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task CheckAsync()
    {
        _checking = true;
        OnPropertyChanged(nameof(CheckText));
        CommandManager.InvalidateRequerySuggested();
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
            CommandManager.InvalidateRequerySuggested();
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
