using PowerExec.Core.Localization;
using System.Windows.Input;
using PowerExec.Core.Execution;
using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;

public sealed class ItemViewModel : ObservableObject
{
    private readonly IPanelHost _app;
    private readonly PanelViewModel _panel;
    private bool _isSelected;
    private IconImage? _icon;
    private readonly EntryConfig? _entry;
    private readonly string? _subtitle;
    /// <summary>„Zuletzt ausgeführt“: mit genau diesem Profil starten (nicht nachfragen).</summary>
    private readonly LaunchProfile? _fixedProfile;

    public ItemViewModel(IPanelHost app, PanelViewModel panel, LaunchItem item, string groupKey,
        LaunchProfile? fixedProfile = null, string? subtitle = null, string? recentWhen = null)
    {
        _subtitle = subtitle;
        RecentWhen = recentWhen;
        _app = app;
        _panel = panel;
        Item = item;
        Key = groupKey + "|" + item.FullPath;
        _entry = app.Settings.FindEntry(item.FullPath);
        Profiles = ProfileResolver.GetProfiles(app.Settings, item);
        _fixedProfile = fixedProfile;
        DefaultProfile = fixedProfile ?? Profiles.FirstOrDefault(p => p.IsDefault) ?? Profiles[0];

        CustomIcon = item.Source?.Icon ?? string.Empty;
        if (app.Settings.ShowProgramIcons && CustomIcon.Length == 0) _icon = LoadIcon(app, item);
        _health = item.Source != null ? app.Health?.Get(item.Source.Key) : null;
        // Commands und Profilmenü entstehen erst beim ersten Zugriff (Kontextmenü, ausgewählter Eintrag):
        // eine Zeile im Panel braucht davon nur SelectCommand.
    }

    // ---- Eigene Einträge: Symbol, Ein-Klick, Snippets, Prüfung -------------------

    /// <summary>Selbst gewähltes Symbol (IconRef-Text), leer = automatisch.</summary>
    public string CustomIcon { get; }
    public bool HasCustomIcon => CustomIcon.Length > 0;
    public bool ShowBadge => !HasCustomIcon && !HasIcon;
    /// <summary>Ein Klick startet sofort (je Eintrag einstellbar, für Scripte, Programme und eigene Einträge).</summary>
    public bool IsSingleClick => _entry?.SingleClick == true;
    public bool IsSnippet => Item.CustomKind == CustomEntryKind.Snippet;
    public bool CanDuplicate => Item.IsCustom && Item.CustomKind != CustomEntryKind.File;
    public bool CanToggleSingleClick => true;
    public string SingleClickHeader => IsSingleClick ? L.T("Start with one click ✓") : L.T("Start with one click");
    public string RunHeader => Item.Source?.CopiesToClipboard == true ? L.T("Copy to clipboard") : L.T("Run");

    private readonly Services.HealthResult? _health;
    public bool HasHealthProblem => _health is { Ok: false };
    public string? HealthText => _health is { Ok: false } h ? h.Message : null;

    public IReadOnlyList<string> Tags => _entry?.Tags ?? (IReadOnlyList<string>)Array.Empty<string>();
    public bool HasTags => Tags.Count > 0;
    public string TagsText => "#" + string.Join("  #", Tags);

    private string? _folderInfo;
    /// <summary>Bei Ordnern: Dateien, Unterordner, Größe (wird beim Aufklappen ermittelt).</summary>
    public string? FolderInfo { get => _folderInfo; private set { if (Set(ref _folderInfo, value)) { OnPropertyChanged(nameof(Meta)); OnPropertyChanged(nameof(HasMeta)); } } }

    private async void LoadFolderInfo()
    {
        if (Item.CustomKind != CustomEntryKind.Folder || _folderInfo != null || Item.Source == null) return;
        var path = Item.Source.Target.Trim();
        if (Item.Source.Placeholders.Count > 0) return;
        FolderInfo = Services.FolderStats.Cached(path) ?? L.T("Evaluating folder …");
        FolderInfo = await Services.FolderStats.GetAsync(path);
    }

    private ICommand? _copySnippetCommand;
    public ICommand CopySnippetCommand => _copySnippetCommand ??= new RelayCommand(() => _app.CopySnippet(Item), () => IsSnippet);
    private ICommand? _duplicateCommand;
    public ICommand DuplicateCommand => _duplicateCommand ??= new RelayCommand(() => _app.DuplicateCustomEntry(Item), () => CanDuplicate);
    private ICommand? _toggleSingleClickCommand;
    public ICommand ToggleSingleClickCommand => _toggleSingleClickCommand ??= new RelayCommand(() => _app.ToggleSingleClick(Item));
    private ICommand? _openDetailsCommand;
    public ICommand OpenDetailsCommand => _openDetailsCommand ??= new RelayCommand(() => _panel.Select(this));

    /// <summary>
    /// Symbol je Art: Webseite = Favicon, Ordner = Ordnersymbol, RDP = Remotedesktop, Befehl = Programmsymbol,
    /// EXE/Verknüpfung/Snap-in und sonstige Dateien = Shell-Symbol. Scripte behalten ihr Kürzel (PS1, CMD …).
    /// </summary>
    private IconImage? LoadIcon(IPanelHost app, LaunchItem item) => LoadIcon(app.IconProvider, item, img => Icon = img);

    /// <summary>Symbol laden (auch für die Liste „Eigene Einträge“); <paramref name="Done"/> liefert nachgeladene Symbole.</summary>
    internal static IconImage? LoadIcon(IIconProvider icons, LaunchItem item, Action<IconImage> Done)
    {
        switch (item.CustomKind)
        {
            case CustomEntryKind.Url: return icons.GetFavicon(item.Source?.Target.Trim() ?? item.LaunchTarget ?? string.Empty, Done);
            case CustomEntryKind.Folder: return icons.GetFolder(Done);
            case CustomEntryKind.Rdp: return icons.GetProgram("mstsc.exe", Done);
            case CustomEntryKind.Command:
            case CustomEntryKind.Ssh:
                return icons.GetProgram(item.LaunchTarget ?? string.Empty, Done);
            case CustomEntryKind.Tool:
                var t = item.Source?.Target.Trim() ?? string.Empty;
                return t.Contains(':') && !Path.IsPathRooted(t) ? null : icons.GetProgram(t, Done);
            case CustomEntryKind.Snippet:
                return null;
        }
        if (item.Type is ItemType.PowerShell or ItemType.Batch) return null;
        return icons.Get(item.FullPath, item.LastWriteTimeUtc, Done);
    }

    public LaunchItem Item { get; }
    public string Key { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(_entry?.DisplayName) ? Item.DisplayName : _entry!.DisplayName;

    // ---- Team-Katalog ----------------------------------------------------------

    /// <summary>Aus einem abonnierten Team-Katalog: schreibgeschützt (Duplizieren legt eine eigene Kopie an).</summary>
    public bool IsFromCatalog => Item.Source?.IsFromCatalog == true;
    public string CatalogToolTip => _app.CatalogOf(Item.Source) is { } c
        ? L.F("From the team catalog “{0}” – read-only. Duplicate creates your own editable copy.", c.Name)
        : string.Empty;

    // ---- Zeitplan --------------------------------------------------------------

    private ICommand? _scheduleCommand;
    public ICommand ScheduleCommand => _scheduleCommand ??= new RelayCommand(() => _app.ShowSchedule(Item));
    private IReadOnlyList<Schedule> SchedulesOf => Schedules.For(_app.Settings, Item.FullPath).Where(s => s.Enabled).ToList();
    public bool HasSchedule => SchedulesOf.Count > 0;
    /// <summary>Chip am Eintrag, z. B. „Mo–Fr 07:00“ (bei mehreren: „+1“).</summary>
    public string ScheduleText => SchedulesOf is { Count: > 0 } list ? Schedules.Describe(list[0]) + (list.Count > 1 ? $" +{list.Count - 1}" : string.Empty) : string.Empty;
    public string ScheduleToolTip => string.Join("\n", SchedulesOf.Select(s => L.T("Scheduled: ") + Schedules.Describe(s) + (s.NextRunLocal is { } n ? L.F(" · next run {0:ddd g}", n) : string.Empty)));

    private bool _isChecked;
    /// <summary>In der Mehrfachauswahl (Strg+Klick).</summary>
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    private string? _quickKeyText;
    /// <summary>Schnellstart-Taste in der Gruppe „Favoriten“ (z. B. „Alt+1“), sonst null.</summary>
    public string? QuickKeyText { get => _quickKeyText; set { if (Set(ref _quickKeyText, value)) OnPropertyChanged(nameof(HasQuickKey)); } }
    public bool HasQuickKey => _quickKeyText != null;

    private IReadOnlyList<TextPart>? _nameParts, _synopsisParts;
    /// <summary>Suchbegriffe, mit denen die Abschnitte berechnet wurden.</summary>
    private IReadOnlyList<string>? _partsTerms;
    private IReadOnlyList<string> CurrentTerms => HighlightTerms ?? _panel.HighlightTerms;
    /// <summary>Name in Abschnitten; Treffer der Suche werden hervorgehoben.</summary>
    public IReadOnlyList<TextPart> NameParts => _nameParts ??= TextPart.Split(DisplayName, _partsTerms ??= CurrentTerms);
    /// <summary>Info-Zeile in Abschnitten; Treffer der Suche werden hervorgehoben.</summary>
    public IReadOnlyList<TextPart> SynopsisParts => _synopsisParts ??= TextPart.Split(Synopsis, _partsTerms ??= CurrentTerms);
    /// <summary>Eigene Suchbegriffe für die Hervorhebung (Spotlight); null = die des Panels.</summary>
    public IReadOnlyList<string>? HighlightTerms { get; init; }

    /// <summary>
    /// Wiederverwendung beim nächsten Rebuild (Suche, Filter): Hervorhebung neu berechnen, falls sich die Suchbegriffe
    /// des Panels geändert haben. Alles andere (Eintrag, Profile, Symbol) bleibt – Einstellungen verwerfen den Cache.
    /// </summary>
    internal void RefreshHighlight()
    {
        if (HighlightTerms != null || (_nameParts == null && _synopsisParts == null)) return;
        // Gleiche Begriffe (z. B. nur der Filter hat gewechselt): nichts neu zeichnen
        if (_partsTerms != null && _partsTerms.SequenceEqual(_panel.HighlightTerms, StringComparer.CurrentCultureIgnoreCase)) return;
        _nameParts = null;
        _synopsisParts = null;
        _partsTerms = null;
        OnPropertyChanged(nameof(NameParts));
        OnPropertyChanged(nameof(SynopsisParts));
    }

    /// <summary>Stellt dieses ViewModel denselben Eintrag dar? (Eigene Einträge werden bei jedem Rebuild neu beschrieben.)</summary>
    internal bool Represents(LaunchItem item) =>
        ReferenceEquals(Item, item) || (item.Source != null && ReferenceEquals(Item.Source, item.Source) && Item.FullPath == item.FullPath);

    public IReadOnlyList<LaunchProfile> Profiles { get; }
    public LaunchProfile DefaultProfile { get; }
    private IReadOnlyList<ProfileMenuItem>? _profileMenu;
    /// <summary>Profile am Ausführen-Knopf – erst beim ersten Zugriff aufgebaut (Zusammenfassung je Profil kostet).</summary>
    public IReadOnlyList<ProfileMenuItem> ProfileMenu => _profileMenu ??= Profiles.Select(p => new ProfileMenuItem(
                p.Name, ProfileResolver.Summary(p, Item), p.IsDefault, p.RunAsAdmin,
                new RelayCommand(() => _app.RunProfile(Item, p)))).ToList();
    public bool HasProfileMenu => Profiles.Count > 1;

    /// <summary>„Zuletzt ausgeführt“: wann (z. B. „vor 5 Min.“) – rechts in der Zeile, auch in der kompakten Liste.</summary>
    public string? RecentWhen { get; }
    public bool HasRecentWhen => RecentWhen != null;

    private bool _isRunning;
    /// <summary>Dieser Eintrag läuft gerade (Aktivitäten-Leiste).</summary>
    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }
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

    public string BadgeText => Item.Source is { Kind: CustomEntryKind.Snippet, Language: SnippetLanguage.Text } ? "TXT"
        : Item.CustomKind is { } ck and not CustomEntryKind.File ? EntryBadge.For(ck, Item.Source?.Target ?? string.Empty) : Item.Type switch
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
            if (Item.Source is { } src && src.Kind != CustomEntryKind.File) return CustomMeta(src);
            if (!Item.IsPowerShell) return Item.FullPath;
            var parts = new List<string> { Item.FullPath };
            parts.Add(Item.HasParameters ? L.F("{0} parameters", Item.Parameters.Count) : L.T("no parameters"));
            if (Item.RequiredModules.Count > 0) parts.Add(L.T("requires ") + string.Join(", ", Item.RequiredModules));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Zusatzinfo für eigene Einträge ohne Wiederholung der Info-Zeile.</summary>
    private string CustomMeta(CustomEntry src)
    {
        var parts = new List<string>();
        switch (src.Kind)
        {
            case CustomEntryKind.Url when src.Browser != BrowserChoice.Default:
                parts.Add(src.Browser switch { BrowserChoice.Chrome => "Chrome", BrowserChoice.Firefox => "Firefox", _ => "Edge" } +
                          (src.Private ? L.T(" · private") : string.Empty) + (src.BrowserProfile.Length > 0 ? L.T(" · profile ") + src.BrowserProfile : string.Empty));
                break;
            case CustomEntryKind.Folder:
                if (_folderInfo != null) parts.Add(_folderInfo);
                break;
            case CustomEntryKind.Snippet:
                var lines = src.Code.Replace("\r", "").Split('\n');
                parts.Add(string.Join("\n", lines.Take(8)) + (lines.Length > 8 ? "\n…" : string.Empty));
                break;
            case CustomEntryKind.Ssh:
            case CustomEntryKind.Rdp:
            case CustomEntryKind.Command:
            case CustomEntryKind.Tool:
                var cmd = ((Item.LaunchTarget ?? string.Empty) + " " + Item.LaunchArguments).Trim();
                if (!cmd.Equals(Synopsis, StringComparison.OrdinalIgnoreCase)) parts.Add(cmd);
                break;
        }
        if (src.Placeholders.Count > 0) parts.Add(L.T("Asks at launch: ") + string.Join(", ", src.Placeholders.Select(p => p.Name)));
        return string.Join("\n", parts);
    }

    public bool HasMeta => Meta.Length > 0;

    /// <summary>Text für den Ausführen-Knopf: Scripte mit Parametern öffnen den Dialog.</summary>
    public string RunLabel => Item.Source?.CopiesToClipboard == true ? L.T("Copy") : L.T("Run");

    public string RunToolTip
    {
        get
        {
            var dialog = Item.IsPowerShell && ProfileResolver.NeedsDialog(DefaultProfile, Item);
            var what = dialog ? L.T("opens the Run dialog") : L.T("starts immediately");
            return DefaultProfile.IsImplicit
                ? L.F("Run – {0} (also by double-click)", what)
                : L.F("Default profile “{0}” – {1} (also by double-click)", DefaultProfile.Name, ProfileResolver.Summary(DefaultProfile, Item));
        }
    }

    public string ToolTipText => (string.IsNullOrWhiteSpace(Item.Description) ? Synopsis : Item.Description) +
                                 (HasNotes ? "\n\nNotiz: " + Notes : string.Empty);

    /// <summary>Programmsymbol (EXE, Verknüpfung, Snap-in); null = Typ-Kürzel anzeigen.</summary>
    public IconImage? Icon
    {
        get => _icon;
        private set
        {
            if (Set(ref _icon, value)) { OnPropertyChanged(nameof(HasIcon)); OnPropertyChanged(nameof(ShowBadge)); }
        }
    }

    public bool HasIcon => _icon != null;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value) && value) { LoadSignature(); LoadRequirements(); LoadFolderInfo(); }
        }
    }

    private InlineParametersViewModel? _inline;
    /// <summary>Parameter-Formular direkt im Panel (nur PowerShell-Scripte mit Parametern).</summary>
    public InlineParametersViewModel? Inline => HasInlineForm ? _inline ??= new InlineParametersViewModel(_app, this) : null;
    public bool HasInlineForm => Item.IsPowerShell && Item.HasParameters && Item.ParseError == null && !Item.IsCustom && _app.Settings.InlineParameters;

    /// <summary>Signatur, Voraussetzungen und Ordnerinfo im Hintergrund laden (Aufklappen, Vorschau).</summary>
    public void LoadDetails() { LoadSignature(); LoadRequirements(); LoadFolderInfo(); }

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
        .Where(g => !g.Equals(GroupName, StringComparison.OrdinalIgnoreCase))
        .Select(g => new GroupChoice(GroupManager.Display(g), new RelayCommand(() => _app.AssignGroup(Item.FullPath, g))))
        .ToList();
    public bool HasGroupChoices => GroupChoices.Count > 0;

    private ICommand? _editCustomCommand;
    public ICommand EditCustomCommand => _editCustomCommand ??= new RelayCommand(() => _app.EditCustomEntry(Item), () => Item.IsCustom && !IsFromCatalog);
    private ICommand? _removeCustomCommand;
    public ICommand RemoveCustomCommand => _removeCustomCommand ??= new RelayCommand(() => _app.RemoveCustomEntry(Item), () => Item.IsCustom && !IsFromCatalog);
    private ICommand? _removeFromGroupCommand;
    public ICommand RemoveFromGroupCommand => _removeFromGroupCommand ??= new RelayCommand(() => _app.AssignGroup(Item.FullPath, null), () => IsInGroup);
    private ICommand? _newGroupWithItemCommand;
    public ICommand NewGroupWithItemCommand => _newGroupWithItemCommand ??= new RelayCommand(() =>
            {
                var name = _app.CreateGroup();
                if (name != null) _app.AssignGroup(Item.FullPath, name);
            });

    private void OpenLocation()
    {
        if (Item.CustomKind == CustomEntryKind.Folder) _app.Shell.OpenLocation(Item.Source?.Target.Trim() ?? Item.LaunchTarget ?? string.Empty, isFolder: true);
        else _app.Shell.OpenLocation(Item.FullPath);
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
        if (Item.RequiresAdmin) parts.Add(L.T("#Requires -RunAsAdministrator: runs locally as administrator automatically."));
        if (Item.RequiredPsVersion.Length > 0)
        {
            if (Requirements.NeedsPowerShell7(Item))
            {
                if (_app.Shell.IsPwshInstalled) parts.Add(L.F("Requires PowerShell {0}: runs with pwsh.exe.", Item.RequiredPsVersion));
                else { parts.Add(L.F("Requires PowerShell {0}, but PowerShell 7 isn't installed.", Item.RequiredPsVersion)); warn = true; }
            }
            else parts.Add(L.F("Requires PowerShell {0}.", Item.RequiredPsVersion));
        }
        if (Item.RequiredModules.Count > 0)
        {
            var missing = await Task.Run(() => Core.Metadata.ModuleLocator.FindMissing(Item.RequiredModules,
                Core.Metadata.ModuleLocator.DefaultSearchPaths(), Item.Directory));
            if (missing.Count > 0) { parts.Add(L.T("Not found locally: ") + string.Join(", ", missing) + L.T(" (checked before launch for remote).")); warn = true; }
            else parts.Add(L.T("Modules available: ") + string.Join(", ", Item.RequiredModules) + ".");
        }
        RequirementsWarn = warn;
        RequirementsText = parts.Count > 0 ? string.Join("\n", parts) : string.Empty;
    }

    // Eigene Einträge mit „Ein Klick startet“: sofort ausführen statt aufklappen
    private ICommand? _selectCommand;
    public ICommand SelectCommand => _selectCommand ??= new RelayCommand(() => { if (IsSingleClick) RunCommand?.Execute(null); else _panel.Select(this); });
    private ICommand? _runCommand;
    public ICommand RunCommand => _runCommand ??= new RelayCommand(() => _app.RunProfile(Item, _fixedProfile));
    private ICommand? _runAdminCommand;
    public ICommand RunAdminCommand => _runAdminCommand ??= new RelayCommand(() => _app.RunProfile(Item, _fixedProfile, ExecutionMode.Administrator), () => CanElevate);
    private ICommand? _runAsUserCommand;
    public ICommand RunAsUserCommand => _runAsUserCommand ??= new RelayCommand(() => _app.RunProfile(Item, _fixedProfile, ExecutionMode.OtherUser), () => CanElevate);
    private ICommand? _runRemoteCommand;
    public ICommand RunRemoteCommand => _runRemoteCommand ??= new RelayCommand(() => _app.OpenRunDialog(Item, DefaultProfile.RunAsAdmin ? ExecutionMode.Administrator : ExecutionMode.CurrentUser,
                remote: true, DefaultProfile), () => Item.SupportsRemote);
    private ICommand? _editCommand;
    public ICommand EditCommand => _editCommand ??= new RelayCommand(() => _app.OpenInEditor(Item), () => CanEdit);
    private ICommand? _openLocationCommand;
    public ICommand OpenLocationCommand => _openLocationCommand ??= new RelayCommand(OpenLocation, () => Item.CustomKind is null or CustomEntryKind.Folder or CustomEntryKind.File);
    private ICommand? _copyPathCommand;
    public ICommand CopyPathCommand => _copyPathCommand ??= new RelayCommand(() => _app.Shell.SetClipboardText(Item.LaunchTarget ?? Item.FullPath));
    private ICommand? _toggleFavoriteCommand;
    public ICommand ToggleFavoriteCommand => _toggleFavoriteCommand ??= new RelayCommand(() => _app.ToggleFavorite(Item));
    private ICommand? _hideCommand;
    public ICommand HideCommand => _hideCommand ??= new RelayCommand(() => _app.HideItem(Item), () => !Item.IsCustom);
    private ICommand? _manageProfilesCommand;
    public ICommand ManageProfilesCommand => _manageProfilesCommand ??= new RelayCommand(() => _app.ShowProfiles(Item));
    private ICommand? _unblockCommand;
    public ICommand UnblockCommand => _unblockCommand ??= new RelayCommand(() => _app.UnblockItem(Item), () => Item.IsBlocked);

    // ---- Sicherheit: Zonen-Kennung und Signatur ---------------------------

    public bool IsBlocked => Item.IsBlocked;
    public string BlockedText => L.F("Blocked: {0}. Depending on the execution policy, the script won't run.", Core.Metadata.ZoneIdentifier.Describe(Item.ZoneId));

    private string? _signatureText;
    private bool _signatureValid;

    /// <summary>Signaturstatus; wird beim Aufklappen im Hintergrund ermittelt.</summary>
    public string? SignatureText { get => _signatureText; private set => Set(ref _signatureText, value); }
    public bool SignatureValid { get => _signatureValid; private set => Set(ref _signatureValid, value); }

    private async void LoadSignature()
    {
        if (_signatureText != null || (Item.IsCustom && Item.CustomKind != CustomEntryKind.File) || Item.Type is not (ItemType.PowerShell or ItemType.Executable)) return;
        SignatureText = L.T("Checking signature …");
        var (text, valid) = await Task.Run(() => Services.SignatureService.Check(Item.FullPath));
        SignatureValid = valid;
        SignatureText = text;
    }
}

/// <summary>Ziel im Menü „In Gruppe verschieben“.</summary>
public sealed record GroupChoice(string Name, ICommand Command);

/// <summary>Eintrag im Profil-Menü am Ausführen-Knopf.</summary>
public sealed record ProfileMenuItem(string Name, string Summary, bool IsDefault, bool IsAdmin, ICommand Command);
