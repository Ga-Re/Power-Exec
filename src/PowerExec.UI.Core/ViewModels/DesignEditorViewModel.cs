using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Localization;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;

/// <summary>
/// Design-Editor: Farben (gesetzt oder aus Fläche und Akzent abgeleitet) für die helle und die dunkle Variante, Schrift
/// und die Farbreihe für Gruppen – mit Vorschau und Kontrastprüfung. Hintergrund, Deckkraft und Ecken übernimmt das
/// Design vom Ausgangsstand; sie stellt man unter „Darstellung“ ein.
/// </summary>
public sealed class DesignEditorViewModel : ObservableObject
{
    private readonly IDialogs _dialogs;
    private readonly DesignPreset _start;
    private readonly Func<string, string?> _validateName;
    private readonly Dictionary<bool, Draft> _drafts = new();
    private string _name;
    private bool _primaryDark;
    private bool _editDark;
    private bool _hasPair;
    private bool _useWindowsAccent;
    private string _fontFamily;
    private string _monoFontFamily;
    private bool _groupColorsForTags;
    private string? _error;

    /// <summary>Entwurf einer Variante; leere Werte = abgeleitet.</summary>
    private sealed class Draft
    {
        public string Surface = string.Empty, Accent = string.Empty, Card = string.Empty, Input = string.Empty, Line = string.Empty,
            Text = string.Empty, TextSecondary = string.Empty, Selected = string.Empty;

        public static Draft From(string panel, string accent, DesignPalette? p) => new()
        {
            Surface = panel,
            Accent = accent,
            Card = p?.Card ?? string.Empty,
            Input = p?.Input ?? string.Empty,
            Line = p?.Line ?? string.Empty,
            Text = p?.Text ?? string.Empty,
            TextSecondary = p?.TextSecondary ?? string.Empty,
            Selected = p?.Selected ?? string.Empty
        };

        public DesignPalette? Palette => new DesignPalette(Card, Input, Line, Text, TextSecondary, Selected) is { IsEmpty: false } p ? p : null;
    }

    /// <param name="dialogs">Farbwahl und „Speichern unter“.</param>
    /// <param name="start">Ausgangsdesign (aktuelles Aussehen oder ein eigenes Design).</param>
    /// <param name="validateName">Prüft den Namen (Fehlermeldung oder null).</param>
    public DesignEditorViewModel(IDialogs dialogs, DesignPreset start, Func<string, string?> validateName)
    {
        _dialogs = dialogs;
        _start = start;
        _validateName = validateName;
        _name = start.Name;
        _primaryDark = start.PrimaryIsDark;
        _editDark = _primaryDark;
        _hasPair = start.Pair != null;
        _useWindowsAccent = start.WindowsAccent;
        _fontFamily = start.FontFamily ?? string.Empty;
        _monoFontFamily = start.MonoFontFamily ?? string.Empty;
        _groupColorsForTags = start.GroupColorsForTags;
        _drafts[_primaryDark] = Draft.From(start.PanelColor, start.AccentColor, start.Palette);
        _drafts[!_primaryDark] = start.Pair is { } pair ? Draft.From(pair.PanelColor, pair.AccentColor, pair.Palette) : new Draft { Accent = start.AccentColor };

        Slots = new[]
        {
            Slot("Surface", L.T("Surface"), d => d.Surface, (d, v) => d.Surface = v),
            Slot("Card", L.T("Card"), d => d.Card, (d, v) => d.Card = v),
            Slot("Input", L.T("Input"), d => d.Input, (d, v) => d.Input = v),
            Slot("Line", L.T("Line"), d => d.Line, (d, v) => d.Line = v),
            Slot("Text", L.T("Text"), d => d.Text, (d, v) => d.Text = v),
            Slot("TextSecondary", L.T("Secondary text"), d => d.TextSecondary, (d, v) => d.TextSecondary = v),
            Slot("Accent", L.T("Accent"), d => d.Accent, (d, v) => { d.Accent = v; if (v.Length > 0) UseWindowsAccent = false; }),
            Slot("Selected", L.T("Selection"), d => d.Selected, (d, v) => d.Selected = v)
        };
        foreach (var hex in Appearance.SplitColors(start.GroupColors)) GroupColors.Add(NewGroupColor(hex));
        DeriveAllCommand = new RelayCommand(() =>
        {
            var d = Current;
            d.Card = d.Input = d.Line = d.Text = d.TextSecondary = d.Selected = string.Empty;
            Refresh();
        });
        AddGroupColorCommand = new RelayCommand(() =>
        {
            var suggestion = PanelColors.AccentPresets[GroupColors.Count % PanelColors.AccentPresets.Count].Hex;
            if (_dialogs.PickColor(L.T("Group color"), suggestion, Owner) is { Length: > 0 } hex && PanelColors.TryParse(hex, out var c)) GroupColors.Add(NewGroupColor(c.Hex));
            Refresh();
        }, () => GroupColors.Count < Appearance.MaxGroupColors);
        SuggestGroupColorsCommand = new RelayCommand(() =>
        {
            GroupColors.Clear();
            foreach (var p in PanelColors.AccentPresets.Take(8)) GroupColors.Add(NewGroupColor(p.Hex));
            Refresh();
        });
        ExportCommand = new RelayCommand(Export);
        SaveCommand = new RelayCommand(Save);
        Refresh();
    }

    /// <summary>Fenster des Editors (Besitzer der Dialoge); setzt die Oberfläche.</summary>
    public object? Owner { get; set; }

    /// <summary>Schließt das Fenster nach „Speichern“ (setzt die Oberfläche).</summary>
    public Action? CloseRequested { get; set; }

    /// <summary>Das gespeicherte Design; null, solange nicht gespeichert wurde.</summary>
    public DesignPreset? Result { get; private set; }

    public string Name { get => _name; set { if (Set(ref _name, value ?? string.Empty)) Error = null; } }
    public string StartText => _start.Name.Length > 0 ? L.F("based on “{0}”", _start.Name) : L.T("based on the current look");
    public string? Error { get => _error; private set => Set(ref _error, value); }

    private Draft Current => _drafts[_editDark];

    /// <summary>Welche Variante bearbeitet wird. Ohne Paar legt das zugleich fest, ob das Design hell oder dunkel ist.</summary>
    public bool EditDark
    {
        get => _editDark;
        set
        {
            if (_editDark == value) return;
            if (!_hasPair)
            {
                // Ein einzelnes Design wechselt die Seite: seine Farben ziehen mit
                _drafts[value] = _drafts[_editDark];
                _drafts[_editDark] = new Draft { Accent = _drafts[value].Accent };
                _primaryDark = value;
            }
            _editDark = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditLight));
            Refresh();
        }
    }
    public bool EditLight { get => !EditDark; set => EditDark = !value; }

    /// <summary>Das Design hat eine helle und eine dunkle Variante und wechselt mit Windows mit.</summary>
    public bool HasPair
    {
        get => _hasPair;
        set { if (Set(ref _hasPair, value)) Refresh(); }
    }

    public bool UseWindowsAccent
    {
        get => _useWindowsAccent;
        set { if (Set(ref _useWindowsAccent, value)) Refresh(); }
    }

    /// <summary>Schrift für Text; leer = Schrift der App.</summary>
    public string FontFamily { get => _fontFamily; set { if (Set(ref _fontFamily, (value ?? string.Empty).Trim())) Refresh(); } }
    /// <summary>Schrift für Pfade und Code; leer = Schrift der App.</summary>
    public string MonoFontFamily { get => _monoFontFamily; set { if (Set(ref _monoFontFamily, (value ?? string.Empty).Trim())) Refresh(); } }

    public IReadOnlyList<DesignColorSlot> Slots { get; }
    public ObservableCollection<DesignGroupColor> GroupColors { get; } = new();
    public bool GroupColorsForTags { get => _groupColorsForTags; set => Set(ref _groupColorsForTags, value); }
    public bool HasGroupColors => GroupColors.Count > 0;

    public ICommand DeriveAllCommand { get; }
    public ICommand AddGroupColorCommand { get; }
    public ICommand SuggestGroupColorsCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand SaveCommand { get; }

    /// <summary>Kopfzeile der Farben: welche Variante gerade bearbeitet wird.</summary>
    public string ColorsTitle => _editDark ? L.T("Colors · dark variant") : L.T("Colors · light variant");

    /// <summary>Was vom Ausgangsstand übernommen wird (Hintergrund, Deckkraft, Ecken).</summary>
    public string CarriedText =>
        PanelBackdrops.Label(_start.Backdrop).Split(' ')[0] + (_start.Backdrop == PanelBackdrop.Solid ? string.Empty : $" · {_start.Opacity} %") +
        (_start.Backdrop == PanelBackdrop.Glass ? " · " + L.F("blur {0}", _start.GlassBlur) : string.Empty);

    // ---- Vorschau: ausgerechnete Farben der bearbeiteten und der anderen Variante ----

    public ResolvedPalette Colors { get; private set; } = null!;
    public ResolvedPalette OtherColors { get; private set; } = null!;
    public string SurfaceHex => Colors.Surface.Hex;
    public string CardHex => Colors.Card.Hex;
    public string InputHex => Colors.Input.Hex;
    public string LineHex => Colors.Line.Hex;
    public string TextHex => Colors.Text.Hex;
    public string TextSecondaryHex => Colors.TextSecondary.Hex;
    public string AccentHex => Colors.Accent.Hex;
    public string OnAccentHex => PanelColors.IsLight(Colors.Accent) ? "#000000" : "#FFFFFF";
    public string SelectedHex => Colors.Selected.Hex;
    public string OtherSurfaceHex => OtherColors.Surface.Hex;
    public string OtherCardHex => OtherColors.Card.Hex;
    public string OtherTextHex => OtherColors.Text.Hex;
    public string OtherAccentHex => OtherColors.Accent.Hex;
    public string OtherLabel => _editDark ? L.T("Light variant") : L.T("Dark variant");
    /// <summary>Farben für die ersten drei Gruppen der Vorschau (ohne Reihe: Akzent).</summary>
    public string Group1Hex => GroupHex(0);
    public string Group2Hex => GroupHex(1);
    public string Group3Hex => GroupHex(2);
    private string GroupHex(int i) => GroupColors.Count > 0 ? GroupColors[i % GroupColors.Count].Hex : AccentHex;

    public IReadOnlyList<DesignContrastCheck> Checks { get; private set; } = Array.Empty<DesignContrastCheck>();

    private DesignColors ColorsOf(bool dark) => new(_drafts[dark].Surface, _useWindowsAccent ? string.Empty : _drafts[dark].Accent, _drafts[dark].Palette);

    private void Refresh()
    {
        Colors = Appearance.Resolve(ColorsOf(_editDark), _editDark);
        OtherColors = Appearance.Resolve(ColorsOf(!_editDark), !_editDark);
        foreach (var s in Slots) s.Refresh();
        static string Ratio(double r) => r.ToString("0.0", CultureInfo.CurrentCulture) + ":1";
        DesignContrastCheck Check(string label, Rgb a, Rgb b, double min)
        {
            var r = PanelColors.Contrast(a, b);
            return new DesignContrastCheck(r >= min ? $"{label} {Ratio(r)}" : L.F("{0} {1} – too weak (at least {2})", label, Ratio(r), Ratio(min)), r >= min);
        }
        Checks = new[]
        {
            Check(L.T("Text on surface"), Colors.Text, Colors.Surface, 4.5),
            Check(L.T("Text on card"), Colors.Text, Colors.Card, 4.5),
            Check(L.T("Secondary text"), Colors.TextSecondary, Colors.Surface, 4.5),
            Check(L.T("Accent on surface"), Colors.Accent, Colors.Surface, 3.0)
        };
        foreach (var p in new[] { nameof(Colors), nameof(OtherColors), nameof(SurfaceHex), nameof(CardHex), nameof(InputHex), nameof(LineHex), nameof(TextHex),
                     nameof(TextSecondaryHex), nameof(AccentHex), nameof(OnAccentHex), nameof(SelectedHex), nameof(OtherSurfaceHex), nameof(OtherCardHex),
                     nameof(OtherTextHex), nameof(OtherAccentHex), nameof(OtherLabel), nameof(Group1Hex), nameof(Group2Hex), nameof(Group3Hex), nameof(Checks),
                     nameof(ColorsTitle), nameof(HasGroupColors), nameof(Preview) })
            OnPropertyChanged(p);
        CommandRequery.Invalidate();
    }

    private DesignColorSlot Slot(string key, string label, Func<Draft, string> get, Action<Draft, string> set) =>
        new(key, label, () => get(Current), () => ResolvedHex(key), hex => { set(Current, hex); Refresh(); },
            initial => _dialogs.PickColor(label, initial, Owner));

    private string ResolvedHex(string key) => key switch
    {
        "Surface" => Colors.Surface.Hex,
        "Card" => Colors.Card.Hex,
        "Input" => Colors.Input.Hex,
        "Line" => Colors.Line.Hex,
        "Text" => Colors.Text.Hex,
        "TextSecondary" => Colors.TextSecondary.Hex,
        "Accent" => Colors.Accent.Hex,
        _ => Colors.Selected.Hex
    };

    private DesignGroupColor NewGroupColor(string hex)
    {
        DesignGroupColor? item = null;
        item = new DesignGroupColor(hex,
            new RelayCommand(() =>
            {
                if (_dialogs.PickColor(L.T("Group color"), item!.Hex, Owner) is { Length: > 0 } picked && PanelColors.TryParse(picked, out var c)) item.Hex = c.Hex;
                Refresh();
            }),
            new RelayCommand(() => { GroupColors.Remove(item!); Refresh(); }));
        return item;
    }

    /// <summary>Das Design, wie es gerade eingestellt ist (Vorschau, Export, Speichern).</summary>
    public DesignPreset Preview => Build(_start.Key, _name.Trim());

    private DesignPreset Build(string key, string name)
    {
        var primary = _drafts[_primaryDark];
        var other = _drafts[!_primaryDark];
        // Ohne eigene Fläche und ohne Paar bleibt ein „wie Windows“-Design eines, das Windows folgt
        var theme = !_hasPair && _start.Theme == ThemeMode.System && primary.Surface.Length == 0 ? ThemeMode.System : _primaryDark ? ThemeMode.Dark : ThemeMode.Light;
        return _start with
        {
            Key = key,
            Name = name,
            IsCustom = true,
            Theme = theme,
            WindowsAccent = _useWindowsAccent,
            PanelColor = primary.Surface,
            AccentColor = _useWindowsAccent ? string.Empty : primary.Accent,
            Palette = primary.Palette,
            Pair = _hasPair ? new DesignVariant(other.Surface, _useWindowsAccent ? string.Empty : other.Accent, other.Palette) : null,
            FontFamily = _fontFamily.Length > 0 ? _fontFamily : null,
            MonoFontFamily = _monoFontFamily.Length > 0 ? _monoFontFamily : null,
            GroupColors = Appearance.JoinColors(GroupColors.Select(g => g.Hex)),
            GroupColorsForTags = _groupColorsForTags && GroupColors.Count > 0,
            Description = Describe()
        };
    }

    private string Describe() => string.Join(" · ", new[]
    {
        _hasPair ? L.T("Light and dark") : _primaryDark ? L.T("Dark") : L.T("Light"),
        _fontFamily, GroupColors.Count > 0 ? L.F("{0} group colors", GroupColors.Count) : string.Empty
    }.Where(t => t.Length > 0));

    private void Save()
    {
        var name = _name.Trim();
        Error = name.Length == 0 ? L.T("Please enter a name.") : _validateName(name);
        if (Error != null) return;
        // Ein eigenes Design wird ersetzt, alles andere (mitgeliefert, Marktplatz, aktueller Stand) wird ein neues
        var key = _start.IsCustom && _start.Key.StartsWith("custom:", StringComparison.Ordinal) ? _start.Key : "custom:" + Guid.NewGuid().ToString("N")[..8];
        Result = Build(key, name);
        CloseRequested?.Invoke();
    }

    private void Export()
    {
        var name = _name.Trim().Length > 0 ? _name.Trim() : L.T("My design");
        var file = _dialogs.SaveFile(L.T("Design files") + "|*.design.json", name.Replace(' ', '-') + ".design.json", L.T("Export design"), Owner);
        if (file == null) return;
        try { File.WriteAllText(file, Appearance.ToFile(Build(string.Empty, name))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = L.F("The file couldn't be written: {0}", ex.Message); }
    }
}

/// <summary>Ein Farbfeld des Design-Editors: gesetzt oder abgeleitet.</summary>
public sealed class DesignColorSlot : ObservableObject
{
    private readonly Func<string> _own;
    private readonly Func<string> _resolved;

    public DesignColorSlot(string key, string label, Func<string> own, Func<string> resolved, Action<string> set, Func<string, string?> pick)
    {
        Key = key;
        Label = label;
        _own = own;
        _resolved = resolved;
        PickCommand = new RelayCommand(() =>
        {
            if (pick(Hex) is { Length: > 0 } picked && PanelColors.TryParse(picked, out var c)) set(c.Hex);
        });
        ClearCommand = new RelayCommand(() => set(string.Empty), () => !IsDerived);
    }

    public string Key { get; }
    public string Label { get; }
    /// <summary>Wirksame Farbe (gesetzt oder abgeleitet).</summary>
    public string Hex => _resolved();
    public bool IsDerived => _own().Length == 0;
    /// <summary>Unter dem Feld: der Farbwert oder „abgeleitet“.</summary>
    public string Note => IsDerived ? L.T("derived") : Hex;
    public ICommand PickCommand { get; }
    public ICommand ClearCommand { get; }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(Hex));
        OnPropertyChanged(nameof(IsDerived));
        OnPropertyChanged(nameof(Note));
    }
}

/// <summary>Eine Farbe der Reihe für Gruppen.</summary>
public sealed class DesignGroupColor : ObservableObject
{
    private string _hex;

    public DesignGroupColor(string hex, ICommand pickCommand, ICommand removeCommand)
    {
        _hex = hex;
        PickCommand = pickCommand;
        RemoveCommand = removeCommand;
    }

    public string Hex { get => _hex; set => Set(ref _hex, value); }
    public ICommand PickCommand { get; }
    public ICommand RemoveCommand { get; }
}

/// <summary>Ergebnis einer Kontrastprüfung im Design-Editor.</summary>
public sealed record DesignContrastCheck(string Text, bool Ok);
