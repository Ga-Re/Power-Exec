using System.Windows;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;


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

    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public ICommand ToggleCommand => new RelayCommand(() => IsExpanded = !IsExpanded);

    /// <summary>Welche Gruppen in diesem Arbeitsbereich im Panel erscheinen.</summary>
    public System.Collections.ObjectModel.ObservableCollection<WorkspaceGroupChoice> Groups { get; } = new();
}
