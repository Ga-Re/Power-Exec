using System.Windows.Input;
using PowerExec.Infrastructure;

namespace PowerExec.ViewModels;

public sealed class GroupViewModel : ObservableObject
{
    private readonly PanelViewModel _panel;
    private bool _isExpanded;

    public GroupViewModel(PanelViewModel panel, AppController app, string key, string title, bool isFavorites, string? folderPath,
        IReadOnlyList<ItemViewModel> items, bool expanded, string? error, string kind = "folder", bool allowReorder = true)
    {
        Kind = kind;
        AllowReorder = allowReorder;
        _panel = panel;
        Key = key;
        Title = title;
        IsFavorites = isFavorites;
        FolderPath = folderPath;
        Items = items;
        _isExpanded = expanded;
        Error = error;

        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        OpenInExplorerCommand = new RelayCommand(() => { if (FolderPath != null) Services.Launcher.OpenLocation(FolderPath, isFolder: true); }, () => FolderPath != null);
        RescanCommand = new RelayCommand(() => _ = app.Repository.ScanAsync());
        CollapseOthersCommand = new RelayCommand(() => _panel.CollapseAllExcept(this));
        CollapseAllCommand = new RelayCommand(() => _panel.SetAllExpanded(false));
        ExpandAllCommand = new RelayCommand(() => _panel.SetAllExpanded(true));
        FolderSettingsCommand = new RelayCommand(() => app.ShowSettings(FolderPath));
        ResetOrderCommand = new RelayCommand(() => _panel.ResetOrder(this), () => AllowReorder && app.Settings.CustomOrder.ContainsKey(Key));

        // Eigene Gruppen
        RenameCommand = new RelayCommand(() => app.RenameGroup(Title), () => IsUserGroup);
        DeleteCommand = new RelayCommand(() => app.DeleteGroup(Title, Items.Count), () => IsUserGroup);
        MoveUpCommand = new RelayCommand(() => app.MoveGroup(Title, -1), () => IsUserGroup);
        MoveDownCommand = new RelayCommand(() => app.MoveGroup(Title, 1), () => IsUserGroup);
        NewGroupCommand = new RelayCommand(() => app.CreateGroup());
        NewEntryCommand = new RelayCommand(p =>
        {
            var kind = Enum.TryParse<Core.Config.CustomEntryKind>(p?.ToString(), out var k) ? k : Core.Config.CustomEntryKind.Url;
            app.NewCustomEntry(kind, IsUserGroup ? Title : null);
        });
        AddFilesCommand = new RelayCommand(() =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = IsUserGroup ? $"Dateien zur Gruppe „{Title}“ hinzufügen" : "Dateien hinzufügen",
                Multiselect = true,
                Filter = "Scripte und Programme (*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc)|*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc|Alle Dateien (*.*)|*.*"
            };
            if (app.WithDialog(() => dlg.ShowDialog()) != true) return;
            app.AddDropped(dlg.FileNames.Select(f => new Core.Config.DroppedTarget(Core.Config.CustomEntryKind.File, f, Path.GetFileName(f))).ToList(),
                IsUserGroup ? Title : null);
        });
    }

    /// <summary>Vom Benutzer angelegte Gruppe (umbenennen, löschen, Einträge hineinziehen).</summary>
    public bool IsUserGroup => Kind == "group";

    /// <summary>Gruppen, in die man Einträge ziehen kann (eigene Gruppen, Ordner, „Eigene Einträge“, „Ohne Gruppe“, Favoriten).</summary>
    public bool AcceptsItems => Kind is "group" or "folder" or "custom" or "ungrouped" or "fav";

    /// <summary>Gruppenname für Zuordnungen: eigene Gruppe = ihr Name, sonst leer (= aus der Gruppe nehmen).</summary>
    public string? GroupName => IsUserGroup ? Title : null;

    public bool IsEmptyUserGroup => IsUserGroup && Items.Count == 0;
    public bool ShowDropHint => IsEmptyUserGroup && IsExpanded;
    public bool IsUserGroupOrCustom => IsUserGroup || Kind == "custom";

    public ICommand RenameCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand NewGroupCommand { get; }
    public ICommand NewEntryCommand { get; }
    public ICommand AddFilesCommand { get; }

    public string Key { get; }
    public string Title { get; }
    public bool IsFavorites { get; }
    public bool IsFolder => FolderPath != null;
    public string Kind { get; }
    public bool AllowReorder { get; }

    /// <summary>Symbol im Gruppenkopf (Segoe Fluent Icons).</summary>
    public string IconGlyph => Kind switch
    {
        "fav" => "\uE735",
        "recent" => "\uE81C",
        "category" or "group" => "\uE8EC",
        "ungrouped" => "\uE8FD",
        "custom" => "\uE71B",
        _ => "\uE8B7"
    };
    public string? FolderPath { get; }
    public IReadOnlyList<ItemViewModel> Items { get; }
    public string CountText => Items.Count.ToString();
    public string? Error { get; }
    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value)) { _panel.OnGroupExpandedChanged(this); OnPropertyChanged(nameof(ShowDropHint)); }
        }
    }

    internal void SetExpandedSilently(bool value)
    {
        _isExpanded = value;
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(ShowDropHint));
    }

    public ICommand ToggleCommand { get; }
    public ICommand OpenInExplorerCommand { get; }
    public ICommand RescanCommand { get; }
    public ICommand CollapseOthersCommand { get; }
    public ICommand CollapseAllCommand { get; }
    public ICommand ExpandAllCommand { get; }
    public ICommand FolderSettingsCommand { get; }
    public ICommand ResetOrderCommand { get; }
}
