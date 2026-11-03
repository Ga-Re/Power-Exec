using System.Windows;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

/// <summary>Bearbeiten eines eigenen Eintrags (alle Arten) – im Editor-Dialog.</summary>
public sealed class CustomEntryViewModel : ObservableObject
{
    private readonly CustomEntry _e;
    private string _group;
    private string _tags;

    public CustomEntryViewModel(CustomEntry entry, string? group = null, IEnumerable<string>? tags = null, IReadOnlyList<string>? groups = null)
    {
        _e = entry.Clone();
        _group = group ?? string.Empty;
        _tags = Core.Config.Tags.Format(tags ?? Array.Empty<string>());
        Groups = groups ?? Array.Empty<string>();
        BrowseCommand = new RelayCommand(Browse, () => _e.Kind is CustomEntryKind.Folder or CustomEntryKind.Command or CustomEntryKind.File or CustomEntryKind.Tool);
        BrowseKeyCommand = new RelayCommand(BrowseKey);
        TestCommand = new RelayCommand(Test, () => (_e.Target.Trim().Length > 0 || _e.Code.Trim().Length > 0) && _e.Placeholders.Count == 0);
        ChooseIconCommand = new RelayCommand(ChooseIcon);
        ClearIconCommand = new RelayCommand(() => Icon = string.Empty, () => _e.Icon.Length > 0);
    }

    public string Name { get => _e.Name; set { _e.Name = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); } }
    public string Title => string.IsNullOrWhiteSpace(_e.Name) ? (string.IsNullOrWhiteSpace(_e.Target) ? "Neuer Eintrag" : _e.Target) : _e.Name;

    public CustomEntryKind Kind
    {
        get => _e.Kind;
        set
        {
            _e.Kind = value;
            foreach (var p in new[] { nameof(Kind), nameof(TargetHint), nameof(TargetLabel), nameof(HasArguments), nameof(Badge), nameof(HasTarget),
                         nameof(IsUrl), nameof(IsSnippet), nameof(IsSsh), nameof(IsRdp), nameof(HasPort), nameof(CanBrowse), nameof(KindHelp) })
                OnPropertyChanged(p);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public static IReadOnlyList<KeyValuePair<CustomEntryKind, string>> Kinds { get; } = new[]
    {
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.Url, "Webseite"),
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.Folder, "Ordner / Freigabe"),
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.Rdp, "Remotedesktop (RDP)"),
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.Ssh, "SSH-Verbindung"),
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.Snippet, "Snippet (PowerShell oder Text)"),
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.Tool, "Windows-Werkzeug"),
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.Command, "Befehl / Programm"),
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.File, "Datei (Script, Programm …)")
    };
    public IReadOnlyList<KeyValuePair<CustomEntryKind, string>> KindList => Kinds;

    public string Target { get => _e.Target; set { _e.Target = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Badge)); OnPropertyChanged(nameof(PlaceholderInfo)); } }
    public string Arguments { get => _e.Arguments; set { _e.Arguments = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(PlaceholderInfo)); } }
    public string Description { get => _e.Description; set { _e.Description = value ?? string.Empty; OnPropertyChanged(); } }

    // Snippet
    public string Code { get => _e.Code; set { _e.Code = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(PlaceholderInfo)); } }
    public SnippetLanguage Language
    {
        get => _e.Language;
        set { _e.Language = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPowerShellSnippet)); OnPropertyChanged(nameof(Badge)); }
    }
    public bool IsPowerShellSnippet => _e.Language == SnippetLanguage.PowerShell;
    public SnippetAction Action { get => _e.Action; set { _e.Action = value; OnPropertyChanged(); } }
    public static IReadOnlyList<KeyValuePair<SnippetLanguage, string>> Languages { get; } = new[]
    {
        new KeyValuePair<SnippetLanguage, string>(SnippetLanguage.PowerShell, "PowerShell"),
        new KeyValuePair<SnippetLanguage, string>(SnippetLanguage.Text, "Text (nur kopieren)")
    };
    public IReadOnlyList<KeyValuePair<SnippetLanguage, string>> LanguageList => Languages;
    public static IReadOnlyList<KeyValuePair<SnippetAction, string>> Actions { get; } = new[]
    {
        new KeyValuePair<SnippetAction, string>(SnippetAction.Run, "Ausführen (Admin, Als …, Remote möglich)"),
        new KeyValuePair<SnippetAction, string>(SnippetAction.Copy, "In die Zwischenablage kopieren")
    };
    public IReadOnlyList<KeyValuePair<SnippetAction, string>> ActionList => Actions;

    // SSH / RDP
    public string User { get => _e.User; set { _e.User = value ?? string.Empty; OnPropertyChanged(); } }
    public string PortText
    {
        get => _e.Port > 0 ? _e.Port.ToString() : string.Empty;
        set { _e.Port = int.TryParse(value, out var p) && p is > 0 and < 65536 ? p : 0; OnPropertyChanged(); }
    }
    public string KeyFile { get => _e.KeyFile; set { _e.KeyFile = value ?? string.Empty; OnPropertyChanged(); } }
    public SshClient Client { get => _e.Client; set { _e.Client = value; OnPropertyChanged(); } }
    public static IReadOnlyList<KeyValuePair<SshClient, string>> Clients { get; } = new[]
    {
        new KeyValuePair<SshClient, string>(SshClient.Terminal, "Windows Terminal"),
        new KeyValuePair<SshClient, string>(SshClient.Console, "Konsole (ssh.exe)"),
        new KeyValuePair<SshClient, string>(SshClient.Putty, "PuTTY")
    };
    public IReadOnlyList<KeyValuePair<SshClient, string>> ClientList => Clients;

    // Webseite
    public BrowserChoice Browser { get => _e.Browser; set { _e.Browser = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBrowserOptions)); } }
    public bool HasBrowserOptions => _e.Browser != BrowserChoice.Default;
    public bool Private { get => _e.Private; set { _e.Private = value; OnPropertyChanged(); } }
    public string BrowserProfile { get => _e.BrowserProfile; set { _e.BrowserProfile = value ?? string.Empty; OnPropertyChanged(); } }
    public static IReadOnlyList<KeyValuePair<BrowserChoice, string>> Browsers { get; } = new[]
    {
        new KeyValuePair<BrowserChoice, string>(BrowserChoice.Default, "Standardbrowser"),
        new KeyValuePair<BrowserChoice, string>(BrowserChoice.Edge, "Microsoft Edge"),
        new KeyValuePair<BrowserChoice, string>(BrowserChoice.Chrome, "Google Chrome"),
        new KeyValuePair<BrowserChoice, string>(BrowserChoice.Firefox, "Mozilla Firefox")
    };
    public IReadOnlyList<KeyValuePair<BrowserChoice, string>> BrowserList => Browsers;

    // Allgemein
    public string Icon { get => _e.Icon; set { _e.Icon = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(HasIcon)); CommandManager.InvalidateRequerySuggested(); } }
    public bool HasIcon => _e.Icon.Length > 0;
    public bool SingleClick { get => _e.SingleClick; set { _e.SingleClick = value; OnPropertyChanged(); } }
    public string Group { get => _group; set => Set(ref _group, value ?? string.Empty); }
    public IReadOnlyList<string> Groups { get; }
    public string TagsText { get => _tags; set => Set(ref _tags, value ?? string.Empty); }

    public bool IsUrl => _e.Kind == CustomEntryKind.Url;
    public bool IsSnippet => _e.Kind == CustomEntryKind.Snippet;
    public bool IsSsh => _e.Kind == CustomEntryKind.Ssh;
    public bool IsRdp => _e.Kind == CustomEntryKind.Rdp;
    public bool HasPort => _e.Kind is CustomEntryKind.Rdp or CustomEntryKind.Ssh;
    public bool HasTarget => _e.Kind != CustomEntryKind.Snippet;
    public bool HasArguments => _e.Kind is CustomEntryKind.Command or CustomEntryKind.Rdp or CustomEntryKind.Ssh or CustomEntryKind.Tool;
    public bool CanBrowse => _e.Kind is CustomEntryKind.Folder or CustomEntryKind.Command or CustomEntryKind.File or CustomEntryKind.Tool;
    public string Badge => PanelBadge(_e.Kind, _e.Target);

    public string PlaceholderInfo
    {
        get
        {
            var ph = _e.Placeholders;
            return ph.Count == 0
                ? "Tipp: {{Name}} oder {{Name:Vorgabe}} in Ziel, Argumenten oder Code wird beim Start abgefragt."
                : "Wird beim Start abgefragt: " + string.Join(", ", ph.Select(p => p.Default.Length > 0 ? $"{p.Name} (Vorgabe {p.Default})" : p.Name));
        }
    }

    public string KindHelp => _e.Kind switch
    {
        CustomEntryKind.Snippet => "PowerShell-Code läuft wie ein Script (auch mit param()-Block, als Admin, als anderer Benutzer oder remote). Text-Snippets werden in die Zwischenablage kopiert.",
        CustomEntryKind.Ssh => "Öffnet die Verbindung in Windows Terminal, in einer Konsole oder in PuTTY. Kennwörter werden nicht gespeichert.",
        CustomEntryKind.Tool => "Snap-in (z. B. dsa.msc), Einstellungsseite (ms-settings:display) oder Programm. Fertige Vorlagen gibt es unter „Aus Vorlagen“.",
        CustomEntryKind.Rdp => "Startet mstsc /v:Rechner. Benutzername und Kennwort fragt Windows ab.",
        _ => string.Empty
    };

    public static string PanelBadge(CustomEntryKind kind, string target = "") => kind switch
    {
        CustomEntryKind.Url => "URL",
        CustomEntryKind.Folder => "DIR",
        CustomEntryKind.Rdp => "RDP",
        CustomEntryKind.Ssh => "SSH",
        CustomEntryKind.Snippet => "PS",
        CustomEntryKind.Tool => "WIN",
        CustomEntryKind.File => Path.GetExtension(target).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext : "DAT",
        _ => "CMD"
    };

    public string TargetLabel => _e.Kind switch
    {
        CustomEntryKind.Url => "Adresse",
        CustomEntryKind.Folder => "Pfad",
        CustomEntryKind.Rdp or CustomEntryKind.Ssh => "Rechner",
        CustomEntryKind.File => "Datei",
        CustomEntryKind.Tool => "Werkzeug",
        _ => "Programm"
    };

    public string TargetHint => _e.Kind switch
    {
        CustomEntryKind.Url => "https://intranet.firma.local oder https://ticket/{{Nummer}}",
        CustomEntryKind.Folder => @"\\server\freigabe\Ordner",
        CustomEntryKind.Rdp => "SRV-01, SRV-01:3390 oder {{Rechner}}",
        CustomEntryKind.Ssh => "server.firma.local",
        CustomEntryKind.File => @"\\server\tools\Skript.ps1",
        CustomEntryKind.Tool => "dsa.msc, ms-settings:display, control.exe",
        _ => "z. B. mmc.exe, wt.exe oder C:\\Tools\\tool.exe"
    };

    public ICommand BrowseCommand { get; }
    public ICommand BrowseKeyCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand ChooseIconCommand { get; }
    public ICommand ClearIconCommand { get; }

    private void Browse()
    {
        if (_e.Kind == CustomEntryKind.Folder)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog();
            if (dlg.ShowDialog() == true) Target = dlg.FolderName;
        }
        else
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = _e.Kind == CustomEntryKind.File
                    ? "Scripte und Programme (*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc)|*.ps1;*.cmd;*.bat;*.exe;*.lnk;*.msc|Alle Dateien (*.*)|*.*"
                    : "Programme (*.exe;*.cmd;*.bat;*.msc)|*.exe;*.cmd;*.bat;*.msc|Alle Dateien (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true) Target = dlg.FileName;
        }
    }

    private void BrowseKey()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Schlüsseldateien (*.ppk;*.pem;id_*)|*.ppk;*.pem;id_*|Alle Dateien (*.*)|*.*" };
        if (dlg.ShowDialog() == true) KeyFile = dlg.FileName;
    }

    private void ChooseIcon()
    {
        var picked = Views.IconPickerWindow.Pick(IconRef.Parse(_e.Icon), "Symbol für den Eintrag");
        if (picked != null) Icon = picked.Value.ToString();
    }

    private void Test()
    {
        var entry = ToEntry();
        try
        {
            if (entry.CopiesToClipboard) { Clipboard.SetText(entry.Code); return; }
            var item = entry.ToLaunchItem();
            if (entry.Kind == CustomEntryKind.File) { Launcher.OpenLocation(item.FullPath); return; }
            if (entry.Kind == CustomEntryKind.Snippet)
            {
                Directory.CreateDirectory(CustomEntry.SnippetFolder);
                File.WriteAllText(entry.SnippetPath, entry.Code, new System.Text.UTF8Encoding(true));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    $"-NoProfile -NoExit -ExecutionPolicy Bypass -File \"{entry.SnippetPath}\"") { UseShellExecute = true })?.Dispose();
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.LaunchTarget!, item.LaunchArguments) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Eintrag testen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public CustomEntry ToEntry() => _e.Clone();
    public IReadOnlyList<string> TagList => Core.Config.Tags.Parse(_tags);

    /// <summary>Fehlermeldung oder null, wenn der Eintrag gespeichert werden kann.</summary>
    public string? Validate() => _e.Kind == CustomEntryKind.Snippet
        ? (_e.Code.Trim().Length == 0 ? "Der Code fehlt." : null)
        : (_e.Target.Trim().Length == 0 ? TargetLabel + " fehlt." : null);
}

/// <summary>Eine Rechnergruppe („@Name“) im Einstellungsfenster.</summary>
public sealed class MachineGroupViewModel : ObservableObject
{
    private string _name;
    private string _machines;
    private string _adQuery = string.Empty;
    private string _status = string.Empty;
    private bool _searching;

    public MachineGroupViewModel(MachineGroup group, Action<MachineGroupViewModel> remove)
    {
        _name = group.Name;
        _machines = string.Join(Environment.NewLine, group.Machines);
        RemoveCommand = new RelayCommand(() => remove(this));
        ImportCommand = new RelayCommand(Import);
        AdSearchCommand = new RelayCommand(() => _ = SearchAdAsync(), () => !_searching && _adQuery.Trim().Length > 0);
        SortCommand = new RelayCommand(() => Machines = string.Join(Environment.NewLine, Parse().OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));
    }

    public string Name { get => _name; set { if (Set(ref _name, (value ?? string.Empty).TrimStart('@'))) OnPropertyChanged(nameof(Reference)); } }
    public string Reference => "@" + _name.Trim();
    public string Machines { get => _machines; set { if (Set(ref _machines, value ?? string.Empty)) OnPropertyChanged(nameof(CountText)); } }
    public string CountText { get { var n = Parse().Count; return n == 1 ? "1 Rechner" : $"{n} Rechner"; } }
    public string AdQuery { get => _adQuery; set => Set(ref _adQuery, value ?? string.Empty); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsSearching { get => _searching; private set { if (Set(ref _searching, value)) CommandManager.InvalidateRequerySuggested(); } }

    public ICommand RemoveCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand AdSearchCommand { get; }
    public ICommand SortCommand { get; }

    public IReadOnlyList<string> Parse() => MachineList.ParseNames(_machines);

    private void AddNames(IEnumerable<string> names, string source)
    {
        var existing = Parse().ToList();
        var added = names.Where(n => !existing.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        Machines = string.Join(Environment.NewLine, existing.Concat(added));
        Status = added.Count == 0 ? $"{source}: keine neuen Rechner." : $"{source}: {added.Count} Rechner hinzugefügt.";
    }

    private void Import()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Rechnerlisten (*.csv;*.txt)|*.csv;*.txt|Alle Dateien (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            AddNames(MachineList.ParseImport(File.ReadAllText(dlg.FileName)), Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            Status = "Import fehlgeschlagen: " + ex.Message;
        }
    }

    private async Task SearchAdAsync()
    {
        IsSearching = true;
        Status = "Suche im Active Directory …";
        try
        {
            var names = await AdSearch.FindComputersAsync(_adQuery);
            AddNames(names, $"AD ({names.Count} gefunden)");
        }
        catch (Exception ex)
        {
            Status = "AD-Suche fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    public MachineGroup ToGroup() => new() { Name = _name.Trim(), Machines = Parse().ToList() };
}

/// <summary>Ein Arbeitsbereich in der Liste der Einstellungen.</summary>
public sealed class WorkspaceRowViewModel : ObservableObject
{
    private string _name;

    public WorkspaceRowViewModel(string name, bool isActive, string summary, Action<WorkspaceRowViewModel> activate, Action<WorkspaceRowViewModel> remove)
    {
        OriginalName = name;
        _name = name;
        IsActive = isActive;
        Summary = summary;
        ActivateCommand = new RelayCommand(() => activate(this), () => !IsActive);
        RemoveCommand = new RelayCommand(() => remove(this), () => !IsActive);
    }

    public string OriginalName { get; }
    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }
    public bool IsActive { get; }
    public string Summary { get; }
    public ICommand ActivateCommand { get; }
    public ICommand RemoveCommand { get; }
}
