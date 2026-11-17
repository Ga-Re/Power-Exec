using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Localization;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;

/// <summary>
/// Designer: alles, was ein Design festlegt – Name, Kategorie und Übersetzungen, Farben der hellen und dunklen
/// Variante (gesetzt oder aus Fläche und Akzent abgeleitet), Hintergrund und Form, Schrift und die Farbreihen der
/// Gruppen – mit Vorschau und Kontrastprüfung. Mitgelieferte und heruntergeladene Designs sind schreibgeschützt; mit
/// „Als eigenes Design bearbeiten“ entsteht eine eigene Kopie, das Original bleibt (und bekommt weiter Updates).
/// </summary>
public sealed class DesignEditorViewModel : ObservableObject
{
    private readonly IDialogs _dialogs;
    private readonly DesignPreset _start;
    private readonly Func<string, string?> _validateName;
    private readonly Dictionary<bool, Draft> _drafts = new();
    private string _name;
    private string _category;
    private string _description;
    private bool _readOnly;
    private string? _basedOn;
    private bool _primaryDark;
    private bool _editDark;
    private bool _hasPair;
    private bool _useWindowsAccent;
    private string _fontFamily;
    private string _monoFontFamily;
    private bool _groupColorsForTags;
    private PanelBackdrop _backdrop;
    private int _opacity, _frost, _glassBlur, _glassSaturation;
    private CornerStyle _corners;
    private GroupCardStyle _groupStyle;
    private string _section = "colors";
    private string? _error;
    /// <summary>Farbreihe der Hauptvariante bzw. der zweiten Variante.</summary>
    private readonly ObservableCollection<DesignGroupColor> _groupPrimary = new(), _groupOther = new();

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
    /// <param name="start">Ausgangsdesign: ein eigenes (bearbeiten), ein mitgeliefertes oder heruntergeladenes (ansehen, als Kopie bearbeiten) oder ein neues (leere Kennung).</param>
    /// <param name="validateName">Prüft den Namen (Fehlermeldung oder null).</param>
    public DesignEditorViewModel(IDialogs dialogs, DesignPreset start, Func<string, string?> validateName)
    {
        _dialogs = dialogs;
        _start = start;
        _validateName = validateName;
        _readOnly = !(start.IsOwn || start.Key.Length == 0);
        _basedOn = start.BasedOn;
        _name = _readOnly ? start.DisplayName : start.Name;
        _category = start.Category ?? string.Empty;
        _description = start.Description;
        _primaryDark = start.PrimaryIsDark;
        _editDark = _primaryDark;
        _hasPair = start.Pair != null;
        _useWindowsAccent = start.WindowsAccent;
        _fontFamily = start.FontFamily ?? string.Empty;
        _monoFontFamily = start.MonoFontFamily ?? string.Empty;
        _groupColorsForTags = start.GroupColorsForTags;
        _backdrop = start.Backdrop is PanelBackdrop.Glass or PanelBackdrop.Solid ? start.Backdrop : PanelBackdrop.Clear;
        _opacity = Math.Clamp(start.Opacity, 20, 100);
        _frost = Math.Clamp(start.Frost, 0, 60);
        _glassBlur = Math.Clamp(start.GlassBlur, 0, 100);
        _glassSaturation = Math.Clamp(start.GlassSaturation, 0, 200);
        _corners = start.Corners ?? CornerStyle.Standard;
        _groupStyle = start.GroupStyle ?? GroupCardStyle.Accent;
        _drafts[_primaryDark] = Draft.From(start.PanelColor, start.AccentColor, start.Palette);
        _drafts[!_primaryDark] = start.Pair is { } pair ? Draft.From(pair.PanelColor, pair.AccentColor, pair.Palette) : CounterpartOf(_primaryDark);
        foreach (var t in start.Translations ?? new Dictionary<string, DesignText>())
            Translations.Add(NewTranslation(t.Key, t.Value));

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
        foreach (var hex in Appearance.SplitColors(start.GroupColors)) _groupPrimary.Add(NewGroupColor(hex, _groupPrimary));
        foreach (var hex in Appearance.SplitColors(start.Pair?.GroupColors)) _groupOther.Add(NewGroupColor(hex, _groupOther));
        DeriveAllCommand = new RelayCommand(() =>
        {
            var d = Current;
            d.Card = d.Input = d.Line = d.Text = d.TextSecondary = d.Selected = string.Empty;
            Refresh();
        }, () => IsEditable);
        AddGroupColorCommand = new RelayCommand(() =>
        {
            var list = GroupColors;
            var suggestion = ReadableOnSurface(PanelColors.AccentPresets[list.Count % PanelColors.AccentPresets.Count].Hex);
            if (_dialogs.PickColor(L.T("Group color"), suggestion, Owner) is { Length: > 0 } hex && PanelColors.TryParse(hex, out var c)) list.Add(NewGroupColor(c.Hex, list));
            Refresh();
        }, () => IsEditable && GroupColors.Count < Appearance.MaxGroupColors);
        SuggestGroupColorsCommand = new RelayCommand(() =>
        {
            var list = GroupColors;
            list.Clear();
            foreach (var p in PanelColors.AccentPresets.Take(8)) list.Add(NewGroupColor(ReadableOnSurface(p.Hex), list));
            Refresh();
        }, () => IsEditable);
        AddTranslationCommand = new RelayCommand(() =>
        {
            var language = L.Available.FirstOrDefault(l => l != "en" && Translations.All(t => t.Language != l)) ?? "de";
            Translations.Add(NewTranslation(language, new DesignText()));
        }, () => IsEditable);
        DuplicateCommand = new RelayCommand(Duplicate, () => _readOnly);
        ExportCommand = new RelayCommand(Export);
        SaveCommand = new RelayCommand(Save);
        Refresh();
    }

    /// <summary>Fenster des Editors (Besitzer der Dialoge); setzt die Oberfläche.</summary>
    public object? Owner { get; set; }

    /// <summary>Schließt das Fenster nach „Speichern“ bzw. „Anwenden“ (setzt die Oberfläche).</summary>
    public Action? CloseRequested { get; set; }

    /// <summary>Das gespeicherte (bzw. bei schreibgeschützten Designs: das unveränderte) Design; null = abgebrochen.</summary>
    public DesignPreset? Result { get; private set; }

    // ---- Schreibschutz ----------------------------------------------------------------

    /// <summary>Mitgeliefert oder heruntergeladen: ansehen und anwenden, Änderungen nur in einer eigenen Kopie.</summary>
    public bool IsReadOnly => _readOnly;
    public bool IsEditable => !_readOnly;

    /// <summary>Hinweis über dem Formular.</summary>
    public string ReadOnlyText => _start.IsDownloaded
        ? L.T("Downloaded design – read-only. It keeps getting updates from the marketplace. To customize it, create your own copy.")
        : L.T("Built-in design – read-only. To customize it, create your own copy.");

    /// <summary>Herkunft unter dem Titel.</summary>
    public string StartText => _readOnly
        ? (_start.IsDownloaded ? L.T("Downloaded from the marketplace") : L.T("Built into Power-Exec"))
        : _basedOn != null ? L.F("Custom design · based on “{0}”", _basedOnName ?? _basedOn) : _start.Key.Length == 0 ? L.T("New design") : L.T("Custom design");
    private string? _basedOnName;

    /// <summary>Eigene Kopie anlegen: alles bleibt, wie es ist, wird aber bearbeitbar; das Original bleibt unverändert.</summary>
    public ICommand DuplicateCommand { get; }

    private void Duplicate()
    {
        _readOnly = false;
        _basedOn = _start.Key;
        _basedOnName = _start.DisplayName;
        // Der Name der Kopie ist ein neuer Grundwert – übersetzte Namen des Originals passen nicht mehr
        Name = L.F("{0} (own)", _start.DisplayName);
        foreach (var t in Translations) t.Name = string.Empty;
        foreach (var p in new[] { nameof(IsReadOnly), nameof(IsEditable), nameof(StartText), nameof(SaveText) }) OnPropertyChanged(p);
        CommandRequery.Invalidate();
    }

    /// <summary>Text der Hauptschaltfläche.</summary>
    public string SaveText => _readOnly ? L.T("Apply") : L.T("Save and apply");

    // ---- Abschnitte -------------------------------------------------------------------

    /// <summary>Gewählter Abschnitt: general, colors, background, fonts, groups.</summary>
    public string Section { get => _section; set => Set(ref _section, value ?? "colors"); }

    // ---- Allgemein --------------------------------------------------------------------

    public string Name { get => _name; set { if (Set(ref _name, value ?? string.Empty)) Error = null; } }
    public string Category { get => _category; set => Set(ref _category, value ?? string.Empty); }
    public string Description { get => _description; set => Set(ref _description, value ?? string.Empty); }
    public string? Error { get => _error; private set => Set(ref _error, value); }

    /// <summary>Übersetzungen von Name, Kategorie und Beschreibung (leer = englischer Grundwert).</summary>
    public ObservableCollection<DesignTranslationRow> Translations { get; } = new();
    public ICommand AddTranslationCommand { get; }

    /// <summary>Sprachen der Oberfläche für die Übersetzungen.</summary>
    public static IReadOnlyList<string> Languages => L.Available.Where(l => l != "en").ToList();

    private DesignTranslationRow NewTranslation(string language, DesignText text)
    {
        DesignTranslationRow? row = null;
        row = new DesignTranslationRow(language, text, new RelayCommand(() => Translations.Remove(row!), () => IsEditable));
        return row;
    }

    private Draft Current => _drafts[_editDark];

    /// <summary>Welche Variante bearbeitet wird. Ohne Paar legt das zugleich fest, ob das Design hell oder dunkel ist.</summary>
    public bool EditDark
    {
        get => _editDark;
        set
        {
            if (_editDark == value) return;
            // Ohne Paar wird das Design zur anderen Seite: dort steht das passende Gegenstück (zurück bleibt das Original)
            if (!_hasPair && IsEditable) _primaryDark = value;
            _editDark = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditLight));
            OnPropertyChanged(nameof(GroupColors));
            Refresh();
        }
    }
    public bool EditLight { get => !EditDark; set => EditDark = !value; }

    /// <summary>Das Design hat eine helle und eine dunkle Variante (welche gilt, wählt man unter Darstellung).</summary>
    public bool HasPair
    {
        get => _hasPair;
        set
        {
            if (!Set(ref _hasPair, value)) return;
            // Die andere Variante beginnt als Gegenstück der bearbeiteten, nicht als weiße Fläche
            if (value && _drafts[!_editDark].Surface.Length == 0 && _drafts[_editDark].Surface.Length > 0) _drafts[!_editDark] = CounterpartOf(_editDark);
            if (!value) _primaryDark = _editDark;
            OnPropertyChanged(nameof(GroupColors));
            Refresh();
        }
    }

    /// <summary>Die andere Variante neu aus der bearbeiteten ableiten (Farbton und Akzent bleiben, Helligkeit umgekehrt).</summary>
    public ICommand RegenerateOtherCommand => _regenerateOther ??= new RelayCommand(() =>
    {
        _drafts[!_editDark] = CounterpartOf(_editDark);
        Refresh();
    }, () => IsEditable);
    private ICommand? _regenerateOther;

    private Draft CounterpartOf(bool dark)
    {
        var d = _drafts[dark];
        var v = Appearance.Counterpart(new DesignColors(d.Surface, d.Accent, d.Palette), dark);
        return Draft.From(v.PanelColor, v.AccentColor, v.Palette);
    }

    public bool UseWindowsAccent
    {
        get => _useWindowsAccent;
        set { if (Set(ref _useWindowsAccent, value)) Refresh(); }
    }

    // ---- Hintergrund und Form ---------------------------------------------------------

    public PanelBackdrop Backdrop
    {
        get => _backdrop;
        set { if (Set(ref _backdrop, value)) { RaiseBackground(); Refresh(); } }
    }
    public static IReadOnlyList<KeyValuePair<PanelBackdrop, string>> Backdrops => PanelSettingsViewModel.BackdropChoices;
    public bool ShowOpacity => _backdrop != PanelBackdrop.Solid;
    public bool ShowGlass => _backdrop == PanelBackdrop.Glass;
    public bool ShowFrost => _backdrop != PanelBackdrop.Solid;
    public int Opacity { get => _opacity; set { if (Set(ref _opacity, Math.Clamp(value, 20, 100))) Refresh(); } }
    public int Frost { get => _frost; set => Set(ref _frost, Math.Clamp(value, 0, 60)); }
    public int GlassBlur { get => _glassBlur; set => Set(ref _glassBlur, Math.Clamp(value, 0, 100)); }
    public int GlassSaturation { get => _glassSaturation; set => Set(ref _glassSaturation, Math.Clamp(value, 0, 200)); }
    public string BackdropDescription => _backdrop switch
    {
        PanelBackdrop.Glass => L.T("The desktop behind the panel is blurred (Windows 11; elsewhere “Transparent” is used)."),
        PanelBackdrop.Solid => L.T("Opaque panel color without transparency."),
        _ => L.T("The panel color lets the desktop shine through; frosted glass adds a light or dark layer.")
    };

    private void RaiseBackground()
    {
        foreach (var p in new[] { nameof(ShowOpacity), nameof(ShowGlass), nameof(ShowFrost), nameof(BackdropDescription) }) OnPropertyChanged(p);
    }

    public CornerStyle Corners { get => _corners; set { if (Set(ref _corners, value)) Refresh(); } }
    public static IReadOnlyList<KeyValuePair<CornerStyle, string>> CornerStyles { get; } = new[]
    {
        new KeyValuePair<CornerStyle, string>(CornerStyle.Square, L.T("Square")),
        new KeyValuePair<CornerStyle, string>(CornerStyle.Standard, L.T("Standard (like Windows 11)")),
        new KeyValuePair<CornerStyle, string>(CornerStyle.Rounded, L.T("Rounded"))
    };

    public GroupCardStyle GroupStyle { get => _groupStyle; set { if (Set(ref _groupStyle, value)) Refresh(); } }
    public static IReadOnlyList<KeyValuePair<GroupCardStyle, string>> GroupStyles { get; } = new[]
    {
        new KeyValuePair<GroupCardStyle, string>(GroupCardStyle.Accent, L.T("Accent stripe")),
        new KeyValuePair<GroupCardStyle, string>(GroupCardStyle.Tinted, L.T("Colored cards"))
    };

    // ---- Schrift ----------------------------------------------------------------------

    /// <summary>Schrift für Text; leer = Schrift der App.</summary>
    public string FontFamily { get => _fontFamily; set { if (Set(ref _fontFamily, (value ?? string.Empty).Trim())) Refresh(); } }
    /// <summary>Schrift für Pfade und Code; leer = Schrift der App.</summary>
    public string MonoFontFamily { get => _monoFontFamily; set { if (Set(ref _monoFontFamily, (value ?? string.Empty).Trim())) Refresh(); } }

    // ---- Farben und Gruppenpalette ----------------------------------------------------

    public IReadOnlyList<DesignColorSlot> Slots { get; }

    /// <summary>Farbreihe der Gruppen in der bearbeiteten Variante.</summary>
    public ObservableCollection<DesignGroupColor> GroupColors => !_hasPair || _editDark == _primaryDark ? _groupPrimary : _groupOther;
    public bool GroupColorsForTags { get => _groupColorsForTags; set => Set(ref _groupColorsForTags, value); }
    public bool HasGroupColors => _groupPrimary.Count > 0 || _groupOther.Count > 0;

    /// <summary>Kopfzeile der Gruppenpalette: welche Variante.</summary>
    public string GroupColorsTitle => _editDark ? L.T("Group palette · dark variant") : L.T("Group palette · light variant");

    public ICommand DeriveAllCommand { get; }
    public ICommand AddGroupColorCommand { get; }
    public ICommand SuggestGroupColorsCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand SaveCommand { get; }

    /// <summary>Kopfzeile der Farben: welche Variante gerade bearbeitet wird.</summary>
    public string ColorsTitle => _editDark ? L.T("Colors · dark variant") : L.T("Colors · light variant");

    // ---- Vorschau: ausgerechnete Farben der bearbeiteten und der anderen Variante ----

    public ResolvedPalette Colors { get; private set; } = null!;
    public ResolvedPalette OtherColors { get; private set; } = null!;
    public string SurfaceHex => Colors.Surface.Hex;
    /// <summary>Fläche mit der Deckkraft des Designs (Vorschau auf dem Hintergrund).</summary>
    public ArgbColor SurfacePreview => ArgbColor.From(Colors.Surface, _backdrop == PanelBackdrop.Solid ? (byte)255 : (byte)Math.Round(255 * _opacity / 100.0));
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
    /// <summary>Karten der Gruppen 2 und 3: bei „farbigen Karten“ in der Gruppenfarbe getönt.</summary>
    public ArgbColor Group2Card => GroupCard(1);
    public ArgbColor Group3Card => GroupCard(2);
    private ArgbColor GroupCard(int i) => _groupStyle == GroupCardStyle.Tinted && PanelColors.TryParse(GroupHex(i), out var g)
        ? ArgbColor.From(PanelColors.Blend(Colors.Surface, g, 0.18)) : ArgbColor.From(Colors.Card);
    /// <summary>Eckenradius des Panels und der Karten in der Vorschau.</summary>
    public double PreviewPanelRadius => CornerRadii.Of(_corners).Tile;
    public double PreviewCardRadius => CornerRadii.Of(_corners).Card;
    public double PreviewControlRadius => CornerRadii.Of(_corners).Control;

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
        foreach (var p in new[] { nameof(Colors), nameof(OtherColors), nameof(SurfaceHex), nameof(SurfacePreview), nameof(CardHex), nameof(InputHex), nameof(LineHex), nameof(TextHex),
                     nameof(TextSecondaryHex), nameof(AccentHex), nameof(OnAccentHex), nameof(SelectedHex), nameof(OtherSurfaceHex), nameof(OtherCardHex),
                     nameof(OtherTextHex), nameof(OtherAccentHex), nameof(OtherLabel), nameof(Group1Hex), nameof(Group2Hex), nameof(Group3Hex), nameof(Group2Card),
                     nameof(Group3Card), nameof(PreviewPanelRadius), nameof(PreviewCardRadius), nameof(PreviewControlRadius), nameof(Checks),
                     nameof(ColorsTitle), nameof(GroupColorsTitle), nameof(HasGroupColors), nameof(Preview) })
            OnPropertyChanged(p);
        CommandRequery.Invalidate();
    }

    /// <summary>Farbe so weit aufhellen bzw. abdunkeln, dass sie auf der Fläche der bearbeiteten Variante lesbar ist (4,5:1).</summary>
    private string ReadableOnSurface(string hex)
    {
        if (!PanelColors.TryParse(hex, out var c)) return hex;
        var toward = PanelColors.IsLight(Colors.Surface) ? new Rgb(0, 0, 0) : new Rgb(255, 255, 255);
        for (int i = 0; i < 30 && PanelColors.Contrast(c, Colors.Surface) < 4.5; i++) c = PanelColors.Blend(c, toward, 0.08);
        return c.Hex;
    }

    private DesignColorSlot Slot(string key, string label, Func<Draft, string> get, Action<Draft, string> set) =>
        new(key, label, () => get(Current), () => ResolvedHex(key), hex => { if (IsEditable) { set(Current, hex); Refresh(); } },
            initial => IsEditable ? _dialogs.PickColor(label, initial, Owner) : null);

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

    private DesignGroupColor NewGroupColor(string hex, ObservableCollection<DesignGroupColor> list)
    {
        DesignGroupColor? item = null;
        item = new DesignGroupColor(hex,
            new RelayCommand(() =>
            {
                if (_dialogs.PickColor(L.T("Group color"), item!.Hex, Owner) is { Length: > 0 } picked && PanelColors.TryParse(picked, out var c)) item.Hex = c.Hex;
                Refresh();
            }, () => IsEditable),
            new RelayCommand(() => { list.Remove(item!); Refresh(); }, () => IsEditable));
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
        var translations = Translations.Where(t => t.Language.Length > 0 && !t.IsEmpty)
            .GroupBy(t => t.Language).ToDictionary(g => g.Key, g => g.First().ToText());
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
            Pair = _hasPair ? new DesignVariant(other.Surface, _useWindowsAccent ? string.Empty : other.Accent, other.Palette, Appearance.JoinColors(_groupOther.Select(g => g.Hex))) : null,
            FontFamily = _fontFamily.Length > 0 ? _fontFamily : null,
            MonoFontFamily = _monoFontFamily.Length > 0 ? _monoFontFamily : null,
            GroupColors = Appearance.JoinColors(_groupPrimary.Select(g => g.Hex)),
            GroupColorsForTags = _groupColorsForTags && HasGroupColors,
            Description = _description.Trim().Length > 0 ? _description.Trim() : Describe(),
            Category = _category.Trim().Length > 0 ? _category.Trim() : null,
            Translations = translations.Count > 0 ? translations : null,
            Backdrop = _backdrop,
            Opacity = _opacity,
            Frost = _frost,
            GlassBlur = _glassBlur,
            GlassSaturation = _glassSaturation,
            Corners = _corners,
            GroupStyle = _groupStyle,
            BasedOn = _basedOn
        };
    }

    private string Describe() => string.Join(" · ", new[]
    {
        _hasPair ? L.T("Light and dark") : _primaryDark ? L.T("Dark") : L.T("Light"),
        _fontFamily, HasGroupColors ? L.F("{0} group colors", _groupPrimary.Count) : string.Empty
    }.Where(t => t.Length > 0));

    private void Save()
    {
        if (_readOnly)
        {
            // Schreibgeschützt: nur anwenden, nichts ändern
            Result = _start;
            CloseRequested?.Invoke();
            return;
        }
        var name = _name.Trim();
        Error = name.Length == 0 ? L.T("Please enter a name.") : _validateName(name);
        if (Error != null)
        {
            Section = "general";
            return;
        }
        // Ein eigenes Design wird ersetzt, alles andere (Kopie, neues Design) bekommt eine neue Kennung
        var key = _start.IsOwn && _basedOn == _start.BasedOn ? _start.Key : "custom:" + Guid.NewGuid().ToString("N")[..8];
        Result = Build(key, name);
        CloseRequested?.Invoke();
    }

    private void Export()
    {
        var name = _name.Trim().Length > 0 ? _name.Trim() : L.T("My design");
        var file = _dialogs.SaveFile(L.T("Design files") + "|*.design.json", name.Replace(' ', '-') + ".design.json", L.T("Export design"), Owner);
        if (file == null) return;
        try { File.WriteAllText(file, Appearance.ToFile(Build(string.Empty, name) with { BasedOn = null })); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = L.F("The file couldn't be written: {0}", ex.Message); }
    }
}

/// <summary>Übersetzung eines Designs in eine Sprache (leere Felder zeigen den englischen Grundwert).</summary>
public sealed class DesignTranslationRow : ObservableObject
{
    private string _language;
    private string _name;
    private string _category;
    private string _description;

    public DesignTranslationRow(string language, DesignText text, ICommand removeCommand)
    {
        _language = language;
        _name = text.Name;
        _category = text.Category;
        _description = text.Description;
        RemoveCommand = removeCommand;
    }

    public string Language { get => _language; set => Set(ref _language, (value ?? string.Empty).Trim().ToLowerInvariant()); }
    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }
    public string Category { get => _category; set => Set(ref _category, value ?? string.Empty); }
    public string Description { get => _description; set => Set(ref _description, value ?? string.Empty); }
    public ICommand RemoveCommand { get; }
    public bool IsEmpty => _name.Trim().Length + _category.Trim().Length + _description.Trim().Length == 0;
    public DesignText ToText() => new(_name.Trim(), _description.Trim(), _category.Trim());
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
