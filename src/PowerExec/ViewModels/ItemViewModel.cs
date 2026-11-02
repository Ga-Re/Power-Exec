using System.Windows;
using System.Windows.Input;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Infrastructure;

namespace PowerExec.ViewModels;

public sealed class ItemViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly PanelViewModel _panel;
    private bool _isSelected;

    public ItemViewModel(AppController app, PanelViewModel panel, LaunchItem item, string groupKey)
    {
        _app = app;
        _panel = panel;
        Item = item;
        Key = groupKey + "|" + item.FullPath;

        SelectCommand = new RelayCommand(() => _panel.Select(this));
        RunCommand = new RelayCommand(() => _app.Run(Item, ExecutionMode.CurrentUser));
        RunAdminCommand = new RelayCommand(() => _app.Run(Item, ExecutionMode.Administrator));
        RunAsUserCommand = new RelayCommand(() => _app.Run(Item, ExecutionMode.OtherUser));
        RunRemoteCommand = new RelayCommand(() => _app.OpenRunDialog(Item, ExecutionMode.CurrentUser, remote: true), () => Item.SupportsRemote);
        EditCommand = new RelayCommand(() => _app.Launcher.OpenInEditor(Item), () => CanEdit);
        OpenLocationCommand = new RelayCommand(() => Services.Launcher.OpenLocation(Item.FullPath));
        CopyPathCommand = new RelayCommand(() => { try { Clipboard.SetText(Item.FullPath); } catch { } });
        ToggleFavoriteCommand = new RelayCommand(() => _app.ToggleFavorite(Item));
        HideCommand = new RelayCommand(() => _app.HideItem(Item));
    }

    public LaunchItem Item { get; }
    public string Key { get; }

    public string DisplayName => Item.DisplayName;
    /// <summary>Info-Zeile; ohne Synopsis der Pfad relativ zum Ordner (unterscheidet gleichnamige Dateien).</summary>
    public string Synopsis => string.IsNullOrWhiteSpace(Item.Synopsis) ? RelativePath : Item.Synopsis;

    public string RelativePath
    {
        get
        {
            try { return Path.GetRelativePath(Item.RootFolder, Item.FullPath); }
            catch { return Item.FileName; }
        }
    }
    public string Description => Item.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Item.Description);
    public ItemType Type => Item.Type;
    public bool IsPowerShell => Item.IsPowerShell;
    public bool CanEdit => Item.Type is ItemType.PowerShell or ItemType.Batch;
    public bool IsFavorite => _app.IsFavorite(Item.FullPath);
    public bool HasParseError => Item.ParseError != null;
    public string? ParseError => Item.ParseError;

    public string BadgeText => Item.Type switch
    {
        ItemType.PowerShell => "PS1",
        ItemType.Batch => Path.GetExtension(Item.FullPath).TrimStart('.').ToUpperInvariant(),
        ItemType.Executable => "EXE",
        ItemType.Shortcut => "LNK",
        ItemType.ConsoleSnapIn => "MSC",
        _ => Path.GetExtension(Item.FullPath).TrimStart('.').ToUpperInvariant()
    };

    public string Meta
    {
        get
        {
            if (!Item.IsPowerShell) return Item.FullPath;
            var parts = new List<string> { Item.FullPath };
            parts.Add(Item.HasParameters ? $"{Item.Parameters.Count} Parameter" : "keine Parameter");
            if (Item.RequiredModules.Count > 0) parts.Add("benötigt " + string.Join(", ", Item.RequiredModules));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Text für den Ausführen-Knopf: Scripte mit Parametern öffnen den Dialog.</summary>
    public string RunLabel => Item.IsPowerShell && Item.HasParameters ? "Ausführen …" : "Ausführen";

    public string ToolTipText => string.IsNullOrWhiteSpace(Item.Description) ? Synopsis : Item.Description;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public void RefreshFavorite() => OnPropertyChanged(nameof(IsFavorite));

    public ICommand SelectCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand RunAdminCommand { get; }
    public ICommand RunAsUserCommand { get; }
    public ICommand RunRemoteCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand OpenLocationCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand HideCommand { get; }
}
