using PowerExec.Core.Localization;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Profiles;
using PowerExec.UI.Infrastructure;

namespace PowerExec.UI.ViewModels;

/// <summary>Ein Schritt im Ablauf-Editor.</summary>
public sealed class WorkflowStepViewModel : ObservableObject
{
    private string _profileName;

    public WorkflowStepViewModel(IWorkflowHost app, WorkflowStep step)
    {
        Step = step;
        var item = app.FindItem(step.Path);
        Exists = item != null;
        Name = step.Name.Length > 0 ? step.Name : Path.GetFileName(step.Path);
        Profiles = item == null ? new[] { "Standard" } : ProfileResolver.GetProfiles(app.Settings, item).Select(p => p.Name).Distinct().ToList();
        _profileName = step.ProfileName.Length > 0 && Profiles.Contains(step.ProfileName) ? step.ProfileName : Profiles[0];
    }

    public WorkflowStep Step { get; }
    public string Name { get; }
    public bool Exists { get; }
    public string Status => Exists ? string.Empty : L.T("not found – will be skipped or abort");
    public IReadOnlyList<string> Profiles { get; }
    public bool HasProfiles => Profiles.Count > 1;
    public string ProfileName { get => _profileName; set => Set(ref _profileName, value ?? Profiles[0]); }
    private int _number;
    public int Number { get => _number; set => Set(ref _number, value); }

    public WorkflowStep ToStep() => new() { Path = Step.Path, Name = Step.Name, ProfileName = _profileName == "Standard" && Profiles.Count == 1 ? string.Empty : _profileName };
}

/// <summary>Ablauf bearbeiten (C3): Name, Reihenfolge der Schritte, Startprofil je Schritt, „bei Fehler abbrechen“.</summary>
public sealed class WorkflowEditorViewModel : ObservableObject
{
    private readonly IWorkflowHost _app;
    private readonly CustomEntry _original;
    private string _name;
    private bool _stopOnError;
    private WorkflowStepViewModel? _selected;

    public WorkflowEditorViewModel(IWorkflowHost app, CustomEntry workflow)
    {
        _app = app;
        _original = workflow;
        _name = workflow.Name;
        _stopOnError = workflow.StopOnError;
        foreach (var s in workflow.Steps) Steps.Add(new WorkflowStepViewModel(app, s));
        Renumber();
        MoveUpCommand = new RelayCommand(p => Move(p as WorkflowStepViewModel, -1));
        MoveDownCommand = new RelayCommand(p => Move(p as WorkflowStepViewModel, 1));
        RemoveCommand = new RelayCommand(p => { if (p is WorkflowStepViewModel s) { Steps.Remove(s); Renumber(); } });
        SaveCommand = new RelayCommand(Save, () => CanSave);
        RunCommand = new RelayCommand(() => { Save(); _ = _app.RunWorkflowAsync(_name.Trim(), Steps.Select(s => s.ToStep()).ToList(), _stopOnError); });
    }

    public ObservableCollection<WorkflowStepViewModel> Steps { get; } = new();
    public string Name { get => _name; set { if (Set(ref _name, value ?? string.Empty)) OnPropertyChanged(nameof(CanSave)); } }
    public bool StopOnError { get => _stopOnError; set => Set(ref _stopOnError, value); }
    public WorkflowStepViewModel? Selected { get => _selected; set => Set(ref _selected, value); }
    public bool CanSave => _name.Trim().Length > 0 && Steps.Count > 0;

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RunCommand { get; }

    public event Action? CloseRequested;

    private void Move(WorkflowStepViewModel? step, int delta)
    {
        if (step == null) return;
        var i = Steps.IndexOf(step);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= Steps.Count) return;
        Steps.Move(i, j);
        Renumber();
        Selected = step;
    }

    private void Renumber()
    {
        for (int i = 0; i < Steps.Count; i++) Steps[i].Number = i + 1;
        OnPropertyChanged(nameof(CanSave));
    }

    public void Save()
    {
        if (!CanSave) return;
        var edited = _original.Clone();
        edited.Name = _name.Trim();
        edited.StopOnError = _stopOnError;
        edited.Steps = Steps.Select(s => s.ToStep()).ToList();
        _app.OnWorkflowSaved(_original, edited);
        CloseRequested?.Invoke();
    }
}
