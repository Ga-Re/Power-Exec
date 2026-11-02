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
    private readonly string? _subtitle;

    public ItemViewModel(AppController app, PanelViewModel panel, LaunchItem item, string groupKey,
        LaunchProfile? fixedProfile = null, string? subtitle = null)
    {
        _subtitle = subtitle;
        _app = app;
        _panel = panel;
        Item = item;
        Key = groupKey + "|" + item.FullPath;
        _entry = app.Settings.FindEntry(item.FullPath);
        Profiles = ProfileResolver.GetProfiles(app.Settings, item);
        DefaultProfile = fixedProfile ?? Profiles.FirstOrDefault(p => p.IsDefault) ?? Profiles[0];
        ProfileMenu = Profiles.Select(p => new ProfileMenuItem(
            p.Name, ProfileResolver.Summary(p, item), p.IsDefault, p.RunAsAdmin,
            new RelayCommand(() => _app.RunProfile(Item, p)))).ToList();

        if (app.Settings.ShowProgramIcons) _icon = LoadIcon(app, item);

        SelectCommand = new RelayCommand(() => _panel.Select(this));
        RunCommand = new RelayCommand(() => _app.RunProfile(Item, DefaultProfile));
        RunAdminCommand = new RelayCommand(() => _app.RunProfile(Item, DefaultProfile, ExecutionMode.Administrator), () => CanElevate);
        RunAsUserCommand = new RelayCommand(() => _app.RunProfile(Item, DefaultProfile, ExecutionMode.OtherUser), () => CanElevate);
        RunRemoteCommand = new RelayCommand(() => _app.OpenRunDialog(Item, DefaultProfile.RunAsAdmin ? ExecutionMode.Administrator : ExecutionMode.CurrentUser,
            remote: true, DefaultProfile), () => Item.SupportsRemote);
        ManageProfilesCommand = new RelayCommand(() => _app.ShowProfiles(Item));
        EditCommand = new RelayCommand(() => _app.Launcher.OpenInEditor(Item), () => CanEdit);
        OpenLocationCommand = new RelayCommand(OpenLocation, () => Item.CustomKind is null or CustomEntryKind.Folder or CustomEntryKind.File);
        CopyPathCommand = new RelayCommand(() => { try { Clipboard.SetText(Item.LaunchTarget ?? Item.FullPath); } catch { } });
        ToggleFavoriteCommand = new RelayCommand(() => _app.ToggleFavorite(Item));
        HideCommand = new RelayCommand(() => _app.HideItem(Item), () => !Item.IsCustom);
        EditCustomCommand = new RelayCommand(() => _app.EditCustomEntry(Item), () => Item.IsCustom);
        RemoveCustomCommand = new RelayCommand(() => _app.RemoveCustomEntry(Item), () => Item.IsCustom);
        RemoveFromGroupCommand = new RelayCommand(() => _app.AssignGroup(Item.FullPath, null), () => IsInGroup);
        NewGroupWithItemCommand = new RelayCommand(() =>
        {
            var name = _app.CreateGroup();
            if (name != null) _app.AssignGroup(Item.FullPath, name);
        });
        UnblockCommand = new RelayCommand(() => _app.UnblockItem(Item), () => Item.IsBlocked);
    }

    /// <summary>
    /// Symbol je Art: Webseite = Favicon, Ordner = Ordnersymbol, RDP = Remotedesktop, Befehl = Programmsymbol,
    /// EXE/Verknüpfung/Snap-in und sonstige Dateien = Shell-Symbol. Scripte behalten ihr Kürzel (PS1, CMD …).
    /// </summary>
    private ImageSource? LoadIcon(AppController app, LaunchItem item)
    {
        void Done(ImageSource img) => Icon = img;
        switch (item.CustomKind)
        {
            case CustomEntryKind.Url: return app.Icons.GetFavicon(item.LaunchTarget ?? string.Empty, Done);
            case CustomEntryKind.Folder: return app.Icons.GetFolder(Done);
            case CustomEntryKind.Rdp: return app.Icons.GetProgram("mstsc.exe", Done);
            case CustomEntryKind.Command: return app.Icons.GetProgram(item.LaunchTarget ?? string.Empty, Done);
        }
        if (item.Type is ItemType.PowerShell or ItemType.Batch) return null;
        return app.Icons.Get(item.FullPath, item.LastWriteTimeUtc, Done);
    }

    public LaunchItem Item { get; }
    public string Key { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(_entry?.DisplayName) ? Item.DisplayName : _entry!.DisplayName;

    public IReadOnlyList<LaunchProfile> Profiles { get; }
    public LaunchProfile DefaultProfile { get; }
    public IReadOnlyList<ProfileMenuItem> ProfileMenu { get; }
    public bool HasProfileMenu => Profiles.Count > 1;
    /// <summary>Info-Zeile; ohne Synopsis der Pfad relativ zum Ordner (unterscheidet gleichnamige Dateien).</summary>
    public string Synopsis => _subtitle != null ? _subtitle
        : !string.IsNullOrWhiteSpace(_entry?.Description) ? _entry!.Description
        : string.IsNullOrWhiteSpace(Item.Synopsis) ? RelativePath : Item.Synopsis;

    public string RelativePath
    {
        get
        {
            if (Item.CustomKind == CustomEntryKind.File) return Item.FullPath;
            if (Item.IsCustom) return Item.LaunchTarget ?? string.Empty;
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

    public string BadgeText => Item.CustomKind is { } ck and not CustomEntryKind.File ? CustomEntryViewModel.PanelBadge(ck) : Item.Type switch
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
            if (Item.IsCustom && Item.CustomKind != CustomEntryKind.File) return Item.Description;
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

    public string ToolTipText => (string.IsNullOrWhiteSpace(Item.Description) ? Synopsis : Item.Description) +
                                 (HasNotes ? "\n\nNotiz: " + Notes : string.Empty);

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
        set
        {
            if (Set(ref _isSelected, value) && value) { LoadSignature(); LoadRequirements(); }
        }
    }

    public void RefreshFavorite() => OnPropertyChanged(nameof(IsFavorite));

    /// <summary>Admin / anderer Benutzer ergibt nur bei Programmen und Scripten Sinn (nicht bei Webseiten und Ordnern).</summary>
    public bool CanElevate => Item.CustomKind is null or CustomEntryKind.Command or CustomEntryKind.Rdp;
    public bool CanHide => !Item.IsCustom;
    public bool IsCustom => Item.IsCustom;

    // ---- Eigene Gruppen ------------------------------------------------------

    public string GroupName => GroupManager.GroupOf(_app.Settings, Item.FullPath);
    public bool IsInGroup => GroupName.Length > 0;

    /// <summary>Menü „In Gruppe verschieben“: alle eigenen Gruppen außer der aktuellen.</summary>
    public IReadOnlyList<GroupChoice> GroupChoices => GroupManager.All(_app.Settings)
        .Where(g => !g.Equals(GroupName, StringComparison.CurrentCultureIgnoreCase))
        .Select(g => new GroupChoice(g, new RelayCommand(() => _app.AssignGroup(Item.FullPath, g))))
        .ToList();
    public bool HasGroupChoices => GroupChoices.Count > 0;

    public ICommand EditCustomCommand { get; }
    public ICommand RemoveCustomCommand { get; }
    public ICommand RemoveFromGroupCommand { get; }
    public ICommand NewGroupWithItemCommand { get; }

    private void OpenLocation()
    {
        if (Item.CustomKind == CustomEntryKind.Folder) Services.Launcher.OpenLocation(Item.LaunchTarget ?? string.Empty, isFolder: true);
        else Services.Launcher.OpenLocation(Item.FullPath);
    }

    // ---- Notizen ------------------------------------------------------------

    public string Notes => _entry?.Notes ?? string.Empty;
    public bool HasNotes => !string.IsNullOrWhiteSpace(_entry?.Notes);

    // ---- #Requires ---------------------------------------------------------

    private string? _requirementsText;
    private bool _requirementsWarn;

    /// <summary>Hinweise aus #Requires (Admin, PowerShell-Version, fehlende Module); wird beim Aufklappen ermittelt.</summary>
    public string? RequirementsText { get => _requirementsText; private set => Set(ref _requirementsText, value); }
    public bool RequirementsWarn { get => _requirementsWarn; private set => Set(ref _requirementsWarn, value); }
    public bool RequiresAdmin => Item.RequiresAdmin;

    private async void LoadRequirements()
    {
        if (_requirementsText != null || !Item.IsPowerShell) return;
        var parts = new List<string>();
        bool warn = false;
        if (Item.RequiresAdmin) parts.Add("#Requires -RunAsAdministrator: startet lokal automatisch als Administrator.");
        if (Item.RequiredPsVersion.Length > 0)
        {
            if (Requirements.NeedsPowerShell7(Item))
            {
                if (Services.Launcher.IsPwshInstalled) parts.Add($"Benötigt PowerShell {Item.RequiredPsVersion}: wird mit pwsh.exe gestartet.");
                else { parts.Add($"Benötigt PowerShell {Item.RequiredPsVersion}, aber PowerShell 7 ist nicht installiert."); warn = true; }
            }
            else parts.Add($"Benötigt PowerShell {Item.RequiredPsVersion}.");
        }
        if (Item.RequiredModules.Count > 0)
        {
            var missing = await Task.Run(() => Core.Metadata.ModuleLocator.FindMissing(Item.RequiredModules,
                Core.Metadata.ModuleLocator.DefaultSearchPaths(), Item.Directory));
            if (missing.Count > 0) { parts.Add("Lokal nicht gefunden: " + string.Join(", ", missing) + " (remote wird vor dem Start geprüft)."); warn = true; }
            else parts.Add("Module vorhanden: " + string.Join(", ", Item.RequiredModules) + ".");
        }
        RequirementsWarn = warn;
        RequirementsText = parts.Count > 0 ? string.Join("\n", parts) : string.Empty;
    }

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
    public ICommand UnblockCommand { get; }

    // ---- Sicherheit: Zonen-Kennung und Signatur ---------------------------

    public bool IsBlocked => Item.IsBlocked;
    public string BlockedText => $"Blockiert: {Core.Metadata.ZoneIdentifier.Describe(Item.ZoneId)}. Je nach Execution Policy startet das Script nicht.";

    private string? _signatureText;
    private bool _signatureValid;

    /// <summary>Signaturstatus; wird beim Aufklappen im Hintergrund ermittelt.</summary>
    public string? SignatureText { get => _signatureText; private set => Set(ref _signatureText, value); }
    public bool SignatureValid { get => _signatureValid; private set => Set(ref _signatureValid, value); }

    private async void LoadSignature()
    {
        if (_signatureText != null || (Item.IsCustom && Item.CustomKind != CustomEntryKind.File) || Item.Type is not (ItemType.PowerShell or ItemType.Executable)) return;
        SignatureText = "Signatur wird geprüft …";
        var (text, valid) = await Task.Run(() => Services.SignatureService.Check(Item.FullPath));
        SignatureValid = valid;
        SignatureText = text;
    }
}

/// <summary>Ziel im Menü „In Gruppe verschieben“.</summary>
public sealed record GroupChoice(string Name, ICommand Command);

/// <summary>Eintrag im Profil-Menü am Ausführen-Knopf.</summary>
public sealed record ProfileMenuItem(string Name, string Summary, bool IsDefault, bool IsAdmin, ICommand Command);
