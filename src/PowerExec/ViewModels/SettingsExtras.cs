using System.Windows;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

/// <summary>Ein eigener Eintrag (Webseite, Ordner, RDP, Befehl) im Einstellungsfenster.</summary>
public sealed class CustomEntryViewModel : ObservableObject
{
    private readonly CustomEntry _e;

    public CustomEntryViewModel(CustomEntry entry, Action<CustomEntryViewModel> remove)
    {
        _e = entry.Clone();
        RemoveCommand = new RelayCommand(() => remove(this));
        BrowseCommand = new RelayCommand(Browse, () => _e.Kind is CustomEntryKind.Folder or CustomEntryKind.Command or CustomEntryKind.File);
        TestCommand = new RelayCommand(Test, () => _e.Target.Trim().Length > 0);
    }

    public string Name { get => _e.Name; set { _e.Name = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); } }
    public string Title => string.IsNullOrWhiteSpace(_e.Name) ? (string.IsNullOrWhiteSpace(_e.Target) ? "Neuer Eintrag" : _e.Target) : _e.Name;

    public CustomEntryKind Kind
    {
        get => _e.Kind;
        set
        {
            _e.Kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TargetHint));
            OnPropertyChanged(nameof(TargetLabel));
            OnPropertyChanged(nameof(HasArguments));
            OnPropertyChanged(nameof(Badge));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public static IReadOnlyList<KeyValuePair<CustomEntryKind, string>> Kinds { get; } = new[]
    {
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.Url, "Webseite"),
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.Folder, "Ordner / Freigabe"),
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.Rdp, "Remotedesktop (RDP)"),
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.Command, "Befehl / Programm"),
        new KeyValuePair<CustomEntryKind, string>(CustomEntryKind.File, "Datei (Script, Programm …)")
    };
    public IReadOnlyList<KeyValuePair<CustomEntryKind, string>> KindList => Kinds;

    public string Target { get => _e.Target; set { _e.Target = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Badge)); } }
    public string Arguments { get => _e.Arguments; set { _e.Arguments = value ?? string.Empty; OnPropertyChanged(); } }
    public string Description { get => _e.Description; set { _e.Description = value ?? string.Empty; OnPropertyChanged(); } }

    public bool HasArguments => _e.Kind is CustomEntryKind.Command or CustomEntryKind.Rdp;
    public string Badge => PanelBadge(_e.Kind, _e.Target);

    public static string PanelBadge(CustomEntryKind kind, string target = "") => kind switch
    {
        CustomEntryKind.Url => "URL",
        CustomEntryKind.Folder => "DIR",
        CustomEntryKind.Rdp => "RDP",
        CustomEntryKind.File => Path.GetExtension(target).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext : "DAT",
        _ => "CMD"
    };

    public string TargetLabel => _e.Kind switch
    {
        CustomEntryKind.Url => "Adresse",
        CustomEntryKind.Folder => "Pfad",
        CustomEntryKind.Rdp => "Rechner",
        CustomEntryKind.File => "Datei",
        _ => "Programm"
    };

    public string TargetHint => _e.Kind switch
    {
        CustomEntryKind.Url => "https://intranet.firma.local",
        CustomEntryKind.Folder => @"\\server\freigabe\Ordner",
        CustomEntryKind.Rdp => "SRV-01 oder SRV-01:3390",
        CustomEntryKind.File => @"\\server\tools\Skript.ps1",
        _ => "z. B. mmc.exe, wt.exe oder C:\\Tools\\tool.exe"
    };

    public ICommand RemoveCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand TestCommand { get; }

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

    private void Test()
    {
        var item = ToEntry().ToLaunchItem();
        try
        {
            if (item.CustomKind == CustomEntryKind.File) { Launcher.OpenLocation(item.FullPath); return; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.LaunchTarget!, item.LaunchArguments) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Eintrag testen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public CustomEntry ToEntry() => _e.Clone();
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
