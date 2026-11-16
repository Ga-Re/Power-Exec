using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Localization;
using PowerExec.UI.Infrastructure;

namespace PowerExec.UI.ViewModels;

/// <summary>
/// „Geerbt oder eigen?“ für einen Wert bzw. einen Abschnitt eines Profils. Im Standardprofil ist alles eigen (es ist die
/// Grundlage); in anderen Profilen zeigt ein geerbter Wert den des Standards und lässt sich mit ✎ anpassen bzw. mit ↺
/// wieder auf den Standard zurücksetzen.
/// </summary>
public sealed class InheritToggle : ObservableObject
{
    private readonly ProfileEditViewModel _profile;

    internal InheritToggle(ProfileEditViewModel profile, string key)
    {
        _profile = profile;
        Key = key;
        MakeOwnCommand = new RelayCommand(() => _profile.SetOwn(Key, true), () => ShowControls && !IsOwn);
        ResetCommand = new RelayCommand(() => _profile.SetOwn(Key, false), () => ShowControls && IsOwn);
    }

    public string Key { get; }

    /// <summary>Legt dieses Profil den Wert selbst fest?</summary>
    public bool IsOwn => _profile.Owns(Key);

    /// <summary>Profil ist nicht der Standard – nur dann gibt es ✎ und ↺.</summary>
    public bool ShowControls => !_profile.IsDefault;

    /// <summary>Wert kommt vom Standard (grau, nicht bearbeitbar).</summary>
    public bool IsInherited => ShowControls && !IsOwn;

    /// <summary>Bearbeitbar: im Standard immer, sonst wenn eigen.</summary>
    public bool IsEditable => !IsInherited;

    public string StateText => !ShowControls ? string.Empty : IsOwn ? L.T("✎ own") : L.T("↳ like Standard");

    public ICommand MakeOwnCommand { get; }
    public ICommand ResetCommand { get; }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsOwn));
        OnPropertyChanged(nameof(ShowControls));
        OnPropertyChanged(nameof(IsInherited));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(StateText));
        CommandRequery.Invalidate();
    }
}

/// <summary>
/// Eine Zeile der Variablen-Tabelle eines Profils: <c>{{$Name}}</c> (Wert vor dem globalen bzw. eigene Profil-Variable)
/// oder eine Abfrage <c>{{Name}}</c> (fragen, Auswahl oder fester Wert).
/// </summary>
public sealed class ProfileVariableRowViewModel : ObservableObject
{
    private readonly ProfileEditViewModel _profile;
    private ProfileVariableKind _kind;
    private string _value;
    private string _choices;
    private string _description;

    internal ProfileVariableRowViewModel(ProfileEditViewModel profile, string name, ProfileVariable? definition, bool fromText, bool isProfileOnly)
    {
        _profile = profile;
        Name = name;
        FromText = fromText;
        IsProfileOnly = isProfileOnly;
        _kind = definition?.Kind ?? (IsVariable ? ProfileVariableKind.Fixed : ProfileVariableKind.Ask);
        _value = definition?.Value ?? string.Empty;
        _choices = string.Join("; ", definition?.Choices ?? new List<string>());
        _description = definition?.Description ?? string.Empty;
        Inherit = new InheritToggle(profile, Core.Profiles.ProfileInheritance.VarPrefix + name);
        RemoveCommand = new RelayCommand(() => _profile.RemoveVariable(this), () => IsProfileOnly && Inherit.IsEditable);
    }

    /// <summary>Name wie im Text: „$Kunde“ bzw. „Ticket“.</summary>
    public string Name { get; }
    public string Token => "{{" + Name + "}}";
    /// <summary>{{$Name}}: Variable (fester Wert); sonst Abfrage.</summary>
    public bool IsVariable => Name.StartsWith('$');
    public bool IsQuery => !IsVariable;
    /// <summary>Kommt im Ziel bzw. Code vor.</summary>
    public bool FromText { get; }
    /// <summary>Nur in diesem Eintrag angelegt (nicht global) – lässt sich entfernen.</summary>
    public bool IsProfileOnly { get; }
    public InheritToggle Inherit { get; }
    public ICommand RemoveCommand { get; }

    /// <summary>Art bei Abfragen: „ask“, „choice“ oder „fixed“.</summary>
    public string KindKey
    {
        get => _kind switch { ProfileVariableKind.Choice => "choice", ProfileVariableKind.Fixed => "fixed", _ => "ask" };
        set
        {
            var k = value switch { "choice" => ProfileVariableKind.Choice, "fixed" => ProfileVariableKind.Fixed, _ => ProfileVariableKind.Ask };
            if (!Set(ref _kind, k, nameof(KindKey))) return;
            OnPropertyChanged(nameof(IsChoice));
            OnPropertyChanged(nameof(ValueHint));
            _profile.OnChanged();
        }
    }

    public static IReadOnlyList<KeyValuePair<string, string>> Kinds { get; } = new[]
    {
        new KeyValuePair<string, string>("ask", L.T("Ask")),
        new KeyValuePair<string, string>("choice", L.T("Choice")),
        new KeyValuePair<string, string>("fixed", L.T("Fixed"))
    };

    public bool IsChoice => IsQuery && _kind == ProfileVariableKind.Choice;

    public string Value { get => _value; set { if (Set(ref _value, value ?? string.Empty)) _profile.OnChanged(); } }

    /// <summary>Auswahlwerte, mit Strichpunkt getrennt.</summary>
    public string ChoicesText { get => _choices; set { if (Set(ref _choices, value ?? string.Empty)) _profile.OnChanged(); } }

    public string Description { get => _description; set { if (Set(ref _description, value ?? string.Empty)) _profile.OnChanged(); } }

    /// <summary>Platzhalter im Wertfeld: was ohne eigenen Wert gilt.</summary>
    public string ValueHint => IsVariable
        ? (_profile.GlobalValue(Name) is { Length: > 0 } g ? L.F("global: {0}", g) : L.T("value for this entry"))
        : _kind switch
        {
            ProfileVariableKind.Fixed => L.T("value – not asked"),
            ProfileVariableKind.Choice => L.T("preselected value"),
            _ => L.T("suggestion (optional)")
        };

    /// <summary>Herkunft: aus dem Text, Standard, dieses Profil, global.</summary>
    public string Origin => Inherit.IsOwn && Inherit.ShowControls ? L.T("this profile")
        : Inherit.IsInherited ? L.T("↳ like Standard")
        : IsProfileOnly ? L.T("this entry")
        : IsVariable ? L.T("global, overridable")
        : L.T("from the text");

    /// <summary>Ist etwas eingestellt (sonst muss nichts gespeichert werden)?</summary>
    public bool IsDefined => IsVariable
        ? _value.Length > 0 || IsProfileOnly
        : _kind == ProfileVariableKind.Fixed || _value.Length > 0 || Choices.Count > 0;

    private List<string> Choices => _choices.Split(';').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();

    public ProfileVariable ToVariable() => new()
    {
        Kind = IsVariable ? ProfileVariableKind.Fixed : _kind,
        Value = _value.Trim(),
        Choices = IsChoice ? Choices : new List<string>(),
        Description = _description.Trim()
    };

    /// <summary>Wert vom Standard übernehmen (geerbt).</summary>
    internal void CopyFrom(ProfileVariable? v)
    {
        _kind = v?.Kind ?? (IsVariable ? ProfileVariableKind.Fixed : ProfileVariableKind.Ask);
        _value = v?.Value ?? string.Empty;
        _choices = string.Join("; ", v?.Choices ?? new List<string>());
        _description = v?.Description ?? string.Empty;
        foreach (var p in new[] { nameof(KindKey), nameof(IsChoice), nameof(Value), nameof(ChoicesText), nameof(Description), nameof(ValueHint) }) OnPropertyChanged(p);
    }

    internal void Refresh()
    {
        Inherit.Refresh();
        OnPropertyChanged(nameof(Origin));
        OnPropertyChanged(nameof(ValueHint));
    }
}
