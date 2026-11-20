using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Localization;
using PowerExec.Core.Profiles;
using PowerExec.UI.Infrastructure;

namespace PowerExec.UI.ViewModels;

/// <summary>Ein Abschnitt in der Navigation des Eintragsfensters (Allgemein, Parameter, Start, Presets …).</summary>
public sealed class EntrySectionViewModel : ObservableObject
{
    private bool _isSelected;
    private string _badge;

    internal EntrySectionViewModel(string key, string label, string glyph, string badge, Action<string> select)
    {
        Key = key;
        Label = label;
        Glyph = glyph;
        _badge = badge;
        SelectCommand = new RelayCommand(() => select(Key));
    }

    public string Key { get; }
    public string Label { get; }
    /// <summary>Symbol (Segoe Fluent Icons).</summary>
    public string Glyph { get; }
    /// <summary>Anzahl rechts (z. B. Parameter, Presets); leer = keine.</summary>
    public string Badge { get => _badge; internal set { if (Set(ref _badge, value)) OnPropertyChanged(nameof(HasBadge)); } }
    public bool HasBadge => _badge.Length > 0;
    public bool IsSelected { get => _isSelected; internal set => Set(ref _isSelected, value); }
    public ICommand SelectCommand { get; }
}

/// <summary>
/// Eine Abweichung eines Presets vom ★ Standard: Bezeichnung, der Wert im Standard („statt …“) und der Editor für
/// den eigenen Wert. Art bestimmt den Editor: target, runas, window, args, param, var.
/// </summary>
public sealed class PresetValueRow : ObservableObject
{
    internal PresetValueRow(ProfileEditViewModel profile, string key, string kind, string label, ProfileParamViewModel? param, ProfileVariableRowViewModel? variable)
    {
        Profile = profile;
        Key = key;
        Kind = kind;
        Label = label;
        Param = param;
        Variable = variable;
        RemoveCommand = new RelayCommand(() => profile.SetOwn(key, false));
    }

    public ProfileEditViewModel Profile { get; }
    public string Key { get; }
    public string Kind { get; }
    public string Label { get; }
    public ProfileParamViewModel? Param { get; }
    public ProfileVariableRowViewModel? Variable { get; }

    /// <summary>„statt Dieser PC“ – der Wert, den der Standard hat.</summary>
    public string BaseText => Profile.BaseValueText(Key) is { Length: > 0 } b ? L.F("instead of {0}", b) : string.Empty;

    public ICommand RemoveCommand { get; }

    internal void Refresh() => OnPropertyChanged(nameof(BaseText));
}

/// <summary>Eintrag im Menü „+ Wert ändern“ eines Presets.</summary>
public sealed record PresetKeyChoice(string Label, ICommand Command);

public sealed partial class ProfileEditViewModel
{
    /// <summary>Abweichungen dieses Presets vom Standard (leer beim Standard selbst).</summary>
    public ObservableCollection<PresetValueRow> OwnRows { get; } = new();
    public bool HasOwnRows => OwnRows.Count > 0;

    /// <summary>Werte, die das Preset noch vom Standard übernimmt und selbst festlegen könnte.</summary>
    public IReadOnlyList<PresetKeyChoice> AddableKeys =>
        _isDefault ? Array.Empty<PresetKeyChoice>()
            : _owner.PresetKeys(this).Where(k => !_own.Contains(k))
                .Select(k => new PresetKeyChoice(KeyLabel(k), new RelayCommand(() => SetOwn(k, true)))).ToList();
    public bool CanAddKeys => AddableKeys.Count > 0;

    /// <summary>Kurzfassung der Abweichungen für die Preset-Liste: „Wo = SRV-T01 · Mode = B“.</summary>
    public string PresetSummary
    {
        get
        {
            if (_isDefault) return L.T("Basis for all presets");
            var parts = OwnRows.Select(r => $"{r.Label} = {ValueText(r.Key)}").ToList();
            if (parts.Count == 0) return L.T("takes everything from ★ Standard");
            return parts.Count <= 3 ? string.Join(" · ", parts) : string.Join(" · ", parts.Take(3)) + " · " + L.F("+{0} more", parts.Count - 3);
        }
    }

    public ICommand MakeStandardCommand => _makeStandard ??= new RelayCommand(() => IsDefault = true, () => !_isDefault);
    private ICommand? _makeStandard;
    public ICommand RemovePresetCommand => _removePreset ??= new RelayCommand(() => _owner.RemovePreset(this), () => !_isDefault);
    private ICommand? _removePreset;
    public ICommand DuplicatePresetCommand => _duplicatePreset ??= new RelayCommand(() => _owner.DuplicatePreset(this));
    private ICommand? _duplicatePreset;

    /// <summary>Zeilen der Abweichungen neu aufbauen (nach ✎/×, Parameter- oder Variablenänderung).</summary>
    internal void RebuildOwnRows()
    {
        OwnRows.Clear();
        if (!_isDefault)
        {
            foreach (var key in _owner.PresetKeys(this).Where(_own.Contains).Concat(_own.Where(k => !_owner.PresetKeys(this).Contains(k, StringComparer.OrdinalIgnoreCase))))
            {
                if (OwnRows.Any(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) continue;
                if (key.StartsWith(ProfileInheritance.ParamPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var name = key[ProfileInheritance.ParamPrefix.Length..];
                    if (Parameters.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } row)
                        OwnRows.Add(new PresetValueRow(this, key, "param", KeyLabel(key), row, null));
                }
                else if (key.StartsWith(ProfileInheritance.VarPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var name = key[ProfileInheritance.VarPrefix.Length..];
                    if (Variables.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } row)
                        OwnRows.Add(new PresetValueRow(this, key, "var", KeyLabel(key), null, row));
                }
                else
                {
                    var kind = key switch
                    {
                        ProfileInheritance.Target => "target",
                        ProfileInheritance.RunAs => "runas",
                        ProfileInheritance.Arguments => "args",
                        _ => "window"
                    };
                    OwnRows.Add(new PresetValueRow(this, key, kind, KeyLabel(key), null, null));
                }
            }
        }
        OnPropertyChanged(nameof(HasOwnRows));
        OnPropertyChanged(nameof(AddableKeys));
        OnPropertyChanged(nameof(CanAddKeys));
        OnPropertyChanged(nameof(PresetSummary));
    }

    /// <summary>Wert des Standards für einen Schlüssel (für „statt …“).</summary>
    internal string BaseValueText(string key) => _owner.DefaultProfile is { } std && !ReferenceEquals(std, this) ? std.ValueText(key) : string.Empty;

    /// <summary>Lesbarer Wert eines Schlüssels in diesem Profil.</summary>
    internal string ValueText(string key)
    {
        if (key.StartsWith(ProfileInheritance.ParamPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = key[ProfileInheritance.ParamPrefix.Length..];
            if (Parameters.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is not { } p) return "–";
            if (p.Mode == ParameterMode.Ask) return L.T("ask");
            var v = p.IsCheck ? (p.BoolValue ? L.T("on") : L.T("off")) : p.Value.Length == 0 ? "–" : p.Value;
            return p.Mode == ParameterMode.Preset ? L.F("{0} (prefilled)", v) : v;
        }
        if (key.StartsWith(ProfileInheritance.VarPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = key[ProfileInheritance.VarPrefix.Length..];
            if (Variables.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is not { } row) return "–";
            if (row.IsQuery && row.KindKey == "ask") return row.Value.Length == 0 ? L.T("ask") : L.F("ask, suggestion {0}", row.Value);
            return row.Value.Length == 0 ? "–" : row.Value;
        }
        switch (key)
        {
            case ProfileInheritance.Target:
                return UsesCode ? L.T("own code") : Shorten(TargetText);
            case ProfileInheritance.Arguments:
                return _arguments.Trim().Length == 0 ? "–" : Shorten(_arguments.Trim());
            case ProfileInheritance.RunAs:
                {
                    var parts = new List<string>();
                    if (_owner.ShowRunAs) parts.Add(_runAsAdmin ? L.T("Administrator") : _runAsOther ? L.T("Different user") : L.T("Current account"));
                    if (_owner.ShowPsOptions)
                    {
                        parts.Add(_target switch
                        {
                            ProfileTarget.Fixed => MachineList.ParseNames(_machines).ToList() is { Count: > 0 } m ? string.Join(", ", m.Take(3)) + (m.Count > 3 ? " …" : string.Empty) : L.T("Fixed computers"),
                            ProfileTarget.Ask => L.T("Choose at launch"),
                            _ => L.T("This PC")
                        });
                        parts.Add(_engine == PsEngine.WindowsPowerShell ? "PS 5.1" : "PS 7");
                    }
                    return parts.Count == 0 ? "–" : string.Join(" · ", parts);
                }
            default:
                {
                    var parts = new List<string>();
                    if (Options is { HasOptions: true } o && o.OptionsSummary.Length > 0) parts.Add(o.OptionsSummary);
                    if (_owner.ShowPsOptions) parts.Add(_dialog switch { DialogMode.Always => L.T("dialog always"), DialogMode.Never => L.T("dialog never"), _ => L.T("dialog when needed") });
                    if (_confirm) parts.Add(L.T("confirm"));
                    return parts.Count == 0 ? "–" : string.Join(" · ", parts);
                }
        }
    }

    private static string Shorten(string s) => s.Length > 48 ? s[..47] + "…" : s;
}

public sealed partial class ProfilesViewModel
{
    private string _section = "general";
    private bool _presetsRevealed;

    /// <summary>Abschnitte der Navigation – je nach Art des Eintrags.</summary>
    public ObservableCollection<EntrySectionViewModel> Sections { get; } = new();

    /// <summary>Gewählter Abschnitt: general, parameters, variables, start, presets, behavior.</summary>
    public string Section
    {
        get => _section;
        set
        {
            if (Sections.Count > 0 && Sections.All(s => s.Key != value)) value = "general";
            if (!Set(ref _section, value)) return;
            foreach (var s in Sections) s.IsSelected = s.Key == value;
            // Im Abschnitt Presets ist immer ein Preset aufgeklappt, sofern es eines gibt
            if (value == "presets" && (Selected == null || Selected.IsDefault) && Presets.FirstOrDefault() is { } first) Selected = first;
        }
    }

    /// <summary>Presets außer dem Standard.</summary>
    public ObservableCollection<ProfileEditViewModel> Presets { get; } = new();
    public bool HasPresets => Presets.Count > 0;

    /// <summary>Bei einfachen Arten (Webseite, Ordner, Datei, Text) erscheint der Abschnitt erst nach „+ Preset“.</summary>
    public bool ShowPresetsSection => Profiles.Count > 1 || _presetsRevealed
        || Entry == null || Entry.Kind is not (CustomEntryKind.Url or CustomEntryKind.Folder or CustomEntryKind.File or CustomEntryKind.Snippet);
    public bool CanRevealPresets => !ShowPresetsSection;

    public ICommand RevealPresetsCommand => _reveal ??= new RelayCommand(() =>
    {
        _presetsRevealed = true;
        AddCommand.Execute(null);
        Section = "presets";
    });
    private ICommand? _reveal;

    /// <summary>Überschrift des Startabschnitts je Art.</summary>
    public string StartSectionLabel => Entry?.Kind switch
    {
        CustomEntryKind.Url or CustomEntryKind.Folder or CustomEntryKind.File => L.T("Open with"),
        CustomEntryKind.Snippet when Entry.CopyInsteadOfRun => L.T("Insert"),
        _ => L.T("Start")
    };

    public bool ShowStartPage => IsScannedFile || ShowRunAsSection || ShowProgramArgs || DefaultProfile?.HasOptions == true;
    public bool ShowVariablesPage => Entry != null || DefaultProfile?.HasVariables == true;

    /// <summary>Navigation neu aufbauen (Art, Parameter, Variablen oder Presets haben sich geändert).</summary>
    internal void RebuildSections()
    {
        var wanted = new List<(string Key, string Label, string Glyph, string Badge)> { ("general", L.T("General"), "", string.Empty) };
        if (ShowParametersSection) wanted.Add(("parameters", L.T("Parameters"), "", Item.Parameters.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)));
        var variableCount = DefaultProfile?.Variables.Count ?? 0;
        if (ShowVariablesPage) wanted.Add(("variables", L.T("Variables"), "", variableCount > 0 ? variableCount.ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty));
        if (ShowStartPage) wanted.Add(("start", StartSectionLabel, "", string.Empty));
        if (ShowPresetsSection) wanted.Add(("presets", L.T("Presets"), "", Presets.Count > 0 ? Presets.Count.ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty));
        wanted.Add(("behavior", L.T("Behavior"), "", string.Empty));

        if (Sections.Select(s => s.Key + "|" + s.Label).SequenceEqual(wanted.Select(w => w.Key + "|" + w.Label)))
        {
            for (var i = 0; i < wanted.Count; i++) Sections[i].Badge = wanted[i].Badge;
        }
        else
        {
            Sections.Clear();
            foreach (var w in wanted) Sections.Add(new EntrySectionViewModel(w.Key, w.Label, w.Glyph, w.Badge, k => Section = k));
        }
        if (Sections.All(s => s.Key != _section)) { _section = "general"; OnPropertyChanged(nameof(Section)); }
        foreach (var s in Sections) s.IsSelected = s.Key == _section;
        OnPropertyChanged(nameof(ShowPresetsSection));
        OnPropertyChanged(nameof(CanRevealPresets));
        OnPropertyChanged(nameof(StartSectionLabel));
        OnPropertyChanged(nameof(ShowStartPage));
        OnPropertyChanged(nameof(ShowVariablesPage));
    }

    /// <summary>Presets-Liste aus den Profilen (ohne Standard) neu bilden.</summary>
    internal void RebuildPresets()
    {
        Presets.Clear();
        foreach (var p in Profiles.Where(p => !p.IsDefault)) Presets.Add(p);
        foreach (var p in Profiles) p.RebuildOwnRows();
        OnPropertyChanged(nameof(HasPresets));
        OnPropertyChanged(nameof(DefaultProfile));
        RebuildSections();
    }

    /// <summary>Schlüssel, die ein Preset bei dieser Art selbst festlegen kann – in der Reihenfolge des Fensters.</summary>
    internal IReadOnlyList<string> PresetKeys(ProfileEditViewModel p)
    {
        var keys = new List<string>();
        if (Entry != null) keys.Add(ProfileInheritance.Target);
        if (ShowParametersSection) keys.AddRange(p.Parameters.Where(x => !x.IsSecure).Select(x => ProfileInheritance.ParamPrefix + x.Name));
        keys.AddRange(p.Variables.Select(v => ProfileInheritance.VarPrefix + v.Name));
        if (ShowRunAsSection) keys.Add(ProfileInheritance.RunAs);
        if (ShowProgramArgs) keys.Add(ProfileInheritance.Arguments);
        keys.Add(ProfileInheritance.Window);
        return keys;
    }

    internal void RemovePreset(ProfileEditViewModel p)
    {
        if (p.IsDefault || !Profiles.Contains(p)) return;
        Selected = p;
        DeleteCommand.Execute(null);
    }

    internal void DuplicatePreset(ProfileEditViewModel p)
    {
        Selected = p;
        DuplicateCommand.Execute(null);
    }
}
