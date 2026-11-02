using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PowerExec.Core.Execution;
using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Infrastructure;

namespace PowerExec.ViewModels;

public sealed class ItemViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly PanelViewModel _panel;
    private bool _isSelected;
    private ImageSource? _icon;
    private readonly EntryConfig? _entry;

    public ItemViewModel(AppController app, PanelViewModel panel, LaunchItem item, string groupKey)
    {
        _app = app;
        _panel = panel;
        Item = item;
        Key = groupKey + "|" + item.FullPath;
        _entry = app.Settings.FindEntry(item.FullPath);
        Profiles = ProfileResolver.GetProfiles(app.Settings, item);
        DefaultProfile = Profiles.FirstOrDefault(p => p.IsDefault) ?? Profiles[0];
        ProfileMenu = Profiles.Select(p => new ProfileMenuItem(
            p.Name, ProfileResolver.Summary(p, item), p.IsDefault, p.RunAsAdmin,
            new RelayCommand(() => _app.RunProfile(Item, p)))).ToList();

        if (app.Settings.ShowProgramIcons && item.Type is ItemType.Executable or ItemType.Shortcut or ItemType.ConsoleSnapIn)
            _icon = app.Icons.Get(item.FullPath, item.LastWriteTimeUtc, img => Icon = img);

        SelectCommand = new RelayCommand(() => _panel.Select(this));
        RunCommand = new RelayCommand(() => _app.RunProfile(Item, DefaultProfile));
        RunAdminCommand = new RelayCommand(() => _app.RunProfile(Item, DefaultProfile, ExecutionMode.Administrator));
        RunAsUserCommand = new RelayCommand(() => _app.RunProfile(Item, DefaultProfile, ExecutionMode.OtherUser));
        RunRemoteCommand = new RelayCommand(() => _app.OpenRunDialog(Item, DefaultProfile.RunAsAdmin ? ExecutionMode.Administrator : ExecutionMode.CurrentUser,
            remote: true, DefaultProfile), () => Item.SupportsRemote);
        ManageProfilesCommand = new RelayCommand(() => _app.ShowProfiles(Item));
        EditCommand = new RelayCommand(() => _app.Launcher.OpenInEditor(Item), () => CanEdit);
        OpenLocationCommand = new RelayCommand(() => Services.Launcher.OpenLocation(Item.FullPath));
        CopyPathCommand = new RelayCommand(() => { try { Clipboard.SetText(Item.FullPath); } catch { } });
        ToggleFavoriteCommand = new RelayCommand(() => _app.ToggleFavorite(Item));
        HideCommand = new RelayCommand(() => _app.HideItem(Item));
    }

    public LaunchItem Item { get; }
    public string Key { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(_entry?.DisplayName) ? Item.DisplayName : _entry!.DisplayName;

    public IReadOnlyList<LaunchProfile> Profiles { get; }
    public LaunchProfile DefaultProfile { get; }
    public IReadOnlyList<ProfileMenuItem> ProfileMenu { get; }
    public bool HasProfileMenu => Profiles.Count > 1;
    /// <summary>Info-Zeile; ohne Synopsis der Pfad relativ zum Ordner (unterscheidet gleichnamige Dateien).</summary>
    public string Synopsis => !string.IsNullOrWhiteSpace(_entry?.Description) ? _entry!.Description
        : string.IsNullOrWhiteSpace(Item.Synopsis) ? RelativePath : Item.Synopsis;

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
    public string RunLabel => "Ausführen";

    public string RunToolTip
    {
        get
        {
            var dialog = Item.IsPowerShell && ProfileResolver.NeedsDialog(DefaultProfile, Item);
            var what = dialog ? "öffnet den Ausführen-Dialog" : "startet sofort";
            return DefaultProfile.IsImplicit
                ? $"Ausführen – {what} (auch per Doppelklick)"
                : $"Standardprofil „{DefaultProfile.Name}“ – {ProfileResolver.Summary(DefaultProfile, Item)} (auch per Doppelklick)";
        }
    }

    public string ToolTipText => string.IsNullOrWhiteSpace(Item.Description) ? Synopsis : Item.Description;

    /// <summary>Programmsymbol (EXE, Verknüpfung, Snap-in); null = Typ-Kürzel anzeigen.</summary>
    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            if (Set(ref _icon, value)) OnPropertyChanged(nameof(HasIcon));
        }
    }

    public bool HasIcon => _icon != null;

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
    public ICommand ManageProfilesCommand { get; }
}

/// <summary>Eintrag im Profil-Menü am Ausführen-Knopf.</summary>
public sealed record ProfileMenuItem(string Name, string Summary, bool IsDefault, bool IsAdmin, ICommand Command);
