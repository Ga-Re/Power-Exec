using PowerExec.Core.Localization;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Infrastructure;

namespace PowerExec.ViewModels;

/// <summary>Ein Feld im Parameter-Formular direkt im Panel.</summary>
public sealed class InlineParameterViewModel : ObservableObject
{
    private string _text;
    private bool _isChecked;
    private bool _hasError;

    public InlineParameterViewModel(ScriptParameter p, string? value)
    {
        Parameter = p;
        _text = value ?? p.DefaultValueLiteral ?? string.Empty;
        _isChecked = value is "true" or "$true" || (value == null && p.DefaultValueLiteral is "$true" or "true");
        Choices = p.ValidValues;
    }

    public ScriptParameter Parameter { get; }
    public string Name => Parameter.Name;
    public string Label => Parameter.Name + (Parameter.IsMandatory ? " *" : string.Empty);
    public string Help => Parameter.HelpText;
    public bool HasHelp => Parameter.HelpText.Length > 0;
    public IReadOnlyList<string> Choices { get; }
    public bool IsSwitch => Parameter.Kind is ParameterKind.Switch or ParameterKind.Boolean;
    public bool IsChoice => Parameter.Kind == ParameterKind.Choice;
    public bool IsSecure => Parameter.Kind == ParameterKind.Secure;
    public bool IsText => !IsSwitch && !IsChoice && !IsSecure;
    public string Placeholder => Parameter.Kind == ParameterKind.List ? L.T("Separate values with commas") : Parameter.TypeName;

    public string Text { get => _text; set { if (Set(ref _text, value ?? string.Empty)) HasError = false; } }
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
    public bool HasError { get => _hasError; set => Set(ref _hasError, value); }

    public string? RawValue => IsSecure ? null : IsSwitch ? (IsChecked ? "true" : "false") : Text;
}

/// <summary>
/// Parameter direkt im Panel (B2): Formular im aufgeklappten Eintrag statt eigenem Fenster,
/// dazu Parameter-Sets („Speichern als …“) als Startprofile mit festen Werten.
/// </summary>
public sealed class InlineParametersViewModel : ObservableObject
{
    private readonly IPanelHost _app;
    private readonly ItemViewModel _item;
    private string? _error;

    public InlineParametersViewModel(IPanelHost app, ItemViewModel item)
    {
        _app = app;
        _item = item;
        var initial = ProfileResolver.InitialValues(item.DefaultProfile, item.Item);
        Fields = item.Item.Parameters.Select(p => new InlineParameterViewModel(p, initial.GetValueOrDefault(p.Name))).ToList();
        RunCommand = new RelayCommand(Run);
        SaveSetCommand = new RelayCommand(SaveSet);
    }

    public IReadOnlyList<InlineParameterViewModel> Fields { get; }
    public bool HasSecure => Fields.Any(f => f.IsSecure);
    public string? Error { get => _error; private set => Set(ref _error, value); }
    public ICommand RunCommand { get; }
    public ICommand SaveSetCommand { get; }

    public Dictionary<string, string?> Values() => Fields.ToDictionary(f => f.Name, f => f.RawValue, StringComparer.OrdinalIgnoreCase);

    /// <summary>Pflichtfelder prüfen; true = alles ausgefüllt.</summary>
    public bool Validate()
    {
        var missing = Fields.Where(f => f.Parameter.IsMandatory && f.IsText && string.IsNullOrWhiteSpace(f.Text)).ToList();
        foreach (var f in missing) f.HasError = true;
        Error = missing.Count == 0 ? null : L.F("Please fill in: {0}", string.Join(", ", missing.Select(f => f.Name)));
        return missing.Count == 0;
    }

    private void Run()
    {
        if (!Validate()) return;
        _app.RunProfile(_item.Item, ParameterSets.WithValues(_item.DefaultProfile, _item.Item, Values()));
    }

    private void SaveSet()
    {
        if (!Validate()) return;
        var values = Values();
        string? error = null;
        var name = _app.Dialogs.Ask(L.T("Save as parameter set"),
            L.T("Appears as a launch profile on the Run button (arrow on the right) and in the profile dialog. Passwords are not saved."),
            string.Empty, L.T("Save"), n =>
            {
                var probe = Core.Config.SettingsStore.Clone(_app.Settings);
                return ParameterSets.Save(probe, _item.Item, n, values, _item.DefaultProfile);
            });
        if (name == null) return;
        error = ParameterSets.Save(_app.Settings, _item.Item, name, values, _item.DefaultProfile);
        if (error != null) { Error = error; return; }
        _app.OnProfilesSaved();
    }
}
