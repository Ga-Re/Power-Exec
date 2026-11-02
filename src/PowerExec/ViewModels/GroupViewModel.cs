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
    }

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
        "category" => "\uE8EC",
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
            if (Set(ref _isExpanded, value)) _panel.OnGroupExpandedChanged(this);
        }
    }

    internal void SetExpandedSilently(bool value)
    {
        _isExpanded = value;
        OnPropertyChanged(nameof(IsExpanded));
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
