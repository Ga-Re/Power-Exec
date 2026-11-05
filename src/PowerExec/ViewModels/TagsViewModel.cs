using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PowerExec.Core.Config;
using PowerExec.Infrastructure;

namespace PowerExec.ViewModels;

/// <summary>Ein Schlagwort in der Verwaltung (Anzahl, Einträge, Farbe, Umbenennen, Löschen).</summary>
public sealed class TagRowViewModel : ObservableObject
{
    private readonly TagsViewModel _owner;
    private bool _isExpanded;

    public TagRowViewModel(TagsViewModel owner, string tag, int count, string color, IReadOnlyList<(string Key, string Name)> entries)
    {
        _owner = owner;
        Tag = tag;
        Count = count;
        _color = color;
        Entries = entries.Select(e => new TagEntryViewModel(e.Name, new RelayCommand(() => _owner.RemoveFromEntry(this, e.Key)))).ToList();
        RenameCommand = new RelayCommand(() => _owner.Rename(this));
        DeleteCommand = new RelayCommand(() => _owner.Delete(this));
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        CustomColorCommand = new RelayCommand(() =>
        {
            var hex = Views.ColorPickerWindow.Pick($"Farbe für #{Tag}", _color, Application.Current.Windows.OfType<Views.SettingsWindow>().FirstOrDefault());
            if (hex != null) Color = hex;
        });
    }

    public ICommand CustomColorCommand { get; }

    public string Tag { get; }
    public int Count { get; }
    public string CountText => Count == 1 ? "1 Eintrag" : $"{Count} Einträge";
    public IReadOnlyList<TagEntryViewModel> Entries { get; }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    private string _color;
    public string Color
    {
        get => _color;
        set
        {
            if (!Set(ref _color, value ?? string.Empty)) return;
            _owner.SetColor(this, _color);
            OnPropertyChanged(nameof(Colors));
            OnPropertyChanged(nameof(ChipBrush));
        }
    }
    /// <summary>Palette plus die eigene Farbe, falls gesetzt (sonst bliebe die Auswahl leer).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Colors =>
        _color.Length > 0 && !CategoryItemViewModel.ColorList.Any(c => c.Key.Equals(_color, StringComparison.OrdinalIgnoreCase))
            ? CategoryItemViewModel.ColorList.Append(new KeyValuePair<string, string>(_color, "Eigene " + _color)).ToList()
            : CategoryItemViewModel.ColorList;

    /// <summary>Hintergrund des Chips: getönt in der Farbe, sonst Standard.</summary>
    public Brush ChipBrush => PanelColors.TryParse(_color, out var c)
        ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, c.R, c.G, c.B))
        : (Brush)Application.Current.Resources["Bg.Chip"];

    public ICommand RenameCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ToggleCommand { get; }
}

/// <summary>Einstellungen › Schlagworte: arbeitet auf der Arbeitskopie der Einstellungen.</summary>
public sealed class TagsViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly AppSettings _s;
    private readonly Action _beforeChange;
    private readonly Action _afterChange;
    private string _query = string.Empty;

    /// <param name="beforeChange">Offene Änderungen anderer Listen in die Arbeitskopie übernehmen.</param>
    /// <param name="afterChange">Andere Listen (Eigene Einträge) neu laden.</param>
    public TagsViewModel(AppController app, AppSettings s, Action beforeChange, Action afterChange)
    {
        _app = app;
        _s = s;
        _beforeChange = beforeChange;
        _afterChange = afterChange;
        Load();
    }

    public ObservableCollection<TagRowViewModel> Rows { get; } = new();
    public bool IsEmpty => Rows.Count == 0;

    public string Query { get => _query; set { if (Set(ref _query, value ?? string.Empty)) Load(); } }

    public void Load()
    {
        var expanded = Rows.Where(r => r.IsExpanded).Select(r => r.Tag).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        Rows.Clear();
        foreach (var (tag, count) in Tags.Counts(_s))
        {
            if (_query.Trim().Length > 0 && !tag.Contains(_query.Trim().TrimStart('#'), StringComparison.CurrentCultureIgnoreCase)) continue;
            var entries = Tags.EntriesWith(_s, tag).Select(k => (k, NameOf(k))).OrderBy(e => e.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
            Rows.Add(new TagRowViewModel(this, tag, count, Tags.ColorOf(_s, tag), entries) { IsExpanded = expanded.Contains(tag) });
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    private string NameOf(string key)
    {
        if (_s.CustomEntries.FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } c)
            return string.IsNullOrWhiteSpace(c.Name) ? c.ToLaunchItem().DisplayName : c.Name;
        if (_s.FindEntry(key)?.DisplayName is { Length: > 0 } dn) return dn;
        return _app.Repository.Items.FirstOrDefault(i => i.FullPath.Equals(key, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? System.IO.Path.GetFileName(key);
    }

    private static Window? Owner() => Application.Current.Windows.OfType<Views.SettingsWindow>().FirstOrDefault();

    internal void Rename(TagRowViewModel row)
    {
        var name = Views.InputWindow.Ask("Schlagwort umbenennen",
            "Gibt es den neuen Namen schon, werden beide Schlagworte zusammengeführt.", row.Tag, "Umbenennen",
            t => t.Trim().TrimStart('#').Length == 0 ? "Bitte einen Namen eingeben." : t.Contains(',') || t.Contains(';') ? "Ohne Komma oder Semikolon." : null);
        if (name == null || name.Trim().TrimStart('#') == row.Tag) return;
        var target = name.Trim().TrimStart('#');
        var existing = Tags.All(_s).FirstOrDefault(t => t.Equals(target, StringComparison.CurrentCultureIgnoreCase) && !t.Equals(row.Tag, StringComparison.CurrentCultureIgnoreCase));
        if (existing != null && MessageBox.Show(Owner()!, $"„#{existing}“ gibt es schon. „#{row.Tag}“ damit zusammenführen?", "Schlagworte zusammenführen",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _beforeChange();
        Tags.Rename(_s, row.Tag, existing ?? target);
        _afterChange();
        Load();
    }

    internal void Delete(TagRowViewModel row)
    {
        if (MessageBox.Show(Owner()!, $"„#{row.Tag}“ von {row.CountText} entfernen?\n\nDie Einträge selbst bleiben erhalten.", "Schlagwort löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _beforeChange();
        Tags.Delete(_s, row.Tag);
        _afterChange();
        Load();
    }

    internal void SetColor(TagRowViewModel row, string color) => Tags.SetColor(_s, row.Tag, color);

    /// <summary>Schlagwort nur von diesem einen Eintrag entfernen.</summary>
    internal void RemoveFromEntry(TagRowViewModel row, string key)
    {
        _beforeChange();
        Tags.Set(_s, key, Tags.Of(_s, key).Where(t => !t.Equals(row.Tag, StringComparison.CurrentCultureIgnoreCase)).ToList());
        _afterChange();
        Load();
    }
}

/// <summary>Ein Eintrag unter einem Schlagwort; ✕ nimmt ihm das Schlagwort.</summary>
public sealed record TagEntryViewModel(string Name, ICommand RemoveCommand);
