using PowerExec.Core.Localization;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Search;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

/// <summary>Einstellungen eines Panels im Einstellungsfenster (Arbeitskopie von <see cref="PanelConfig"/>).</summary>
public sealed class PanelSettingsViewModel : ObservableObject
{
    private readonly PanelConfig _c;

    private readonly ISettingsHost _app;

    /// <param name="recentColors">Zuletzt verwendete Panelfarben (wird beim Wählen einer Farbe ergänzt).</param>
    public PanelSettingsViewModel(ISettingsHost app, PanelConfig config, IReadOnlyList<KeyValuePair<int, string>> monitors,
        List<string>? recentColors = null)
    {
        _app = app;
        _recentColors = recentColors ?? new List<string>();
        _c = config.Clone();
        _startSize = (config.PanelWidth, config.PanelHeightPercent);
        Monitors = monitors;
        ClearHotkeyCommand = new RelayCommand(() => Hotkey = string.Empty);
    }

    public string Id => _c.Id;

    /// <summary>Größe beim Öffnen des Formulars (um eine im Panel gezogene Größe nicht zu überschreiben).</summary>
    private readonly (int Width, int HeightPercent) _startSize;

    /// <summary>
    /// Größe, die übernommen wird: im Formular geändert → Formular; sonst die aktuelle Größe des Panels
    /// (am Rand gezogen, während die Einstellungen offen waren).
    /// </summary>
    public (int Width, int HeightPercent) EffectiveSize(PanelConfig live) =>
        (_c.PanelWidth != _startSize.Width ? _c.PanelWidth : live.PanelWidth,
         _c.PanelHeightPercent != _startSize.HeightPercent ? _c.PanelHeightPercent : live.PanelHeightPercent);

    public string Name
    {
        get => _c.Name;
        set { _c.Name = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public string Summary
    {
        get
        {
            var edge = Edges.First(e => e.Key == _c.Edge).Value;
            var mon = Monitors.FirstOrDefault(m => m.Key == MonitorChoice).Value ?? "Monitor";
            var hk = string.IsNullOrWhiteSpace(_c.Hotkey) ? L.T("no hotkey") : DisplayText.Hotkey(_c.Hotkey);
            return $"{edge} · {mon} · {hk}";
        }
    }

    // ---- Position ---------------------------------------------------------

    public ScreenEdge Edge { get => _c.Edge; set { _c.Edge = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); } }
    public static IReadOnlyList<KeyValuePair<ScreenEdge, string>> Edges { get; } = new[]
    {
        new KeyValuePair<ScreenEdge, string>(ScreenEdge.Right, L.T("Right")),
        new KeyValuePair<ScreenEdge, string>(ScreenEdge.Left, L.T("Left")),
        new KeyValuePair<ScreenEdge, string>(ScreenEdge.Top, L.T("Top")),
        new KeyValuePair<ScreenEdge, string>(ScreenEdge.Bottom, L.T("Bottom"))
    };
    public IReadOnlyList<KeyValuePair<ScreenEdge, string>> EdgeList => Edges;

    public IReadOnlyList<KeyValuePair<int, string>> Monitors { get; }
    public int MonitorChoice
    {
        get => _c.MonitorMode switch { MonitorMode.All => -2, MonitorMode.Specific => _c.MonitorIndex, _ => -1 };
        set
        {
            if (value == -2) _c.MonitorMode = MonitorMode.All;
            else if (value == -1) _c.MonitorMode = MonitorMode.Primary;
            else { _c.MonitorMode = MonitorMode.Specific; _c.MonitorIndex = value; }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public HotZoneMode HotZone { get => _c.HotZone; set { _c.HotZone = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsHotZoneMiddle)); } }
    public bool IsHotZoneMiddle => _c.HotZone == HotZoneMode.Middle;
    public int HotZoneThickness { get => _c.HotZoneThickness; set { _c.HotZoneThickness = value; OnPropertyChanged(); } }
    public int HotZoneLength { get => _c.HotZoneLength; set { _c.HotZoneLength = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<HotZoneMode, string>> HotZones { get; } = new[]
    {
        new KeyValuePair<HotZoneMode, string>(HotZoneMode.Middle, L.T("Middle · custom length")),
        new KeyValuePair<HotZoneMode, string>(HotZoneMode.FirstHalf, L.T("First half (top or left)")),
        new KeyValuePair<HotZoneMode, string>(HotZoneMode.Full, L.T("Whole edge"))
    };

    // ---- Größe ------------------------------------------------------------

    public int PanelWidth { get => _c.PanelWidth; set { _c.PanelWidth = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeSummary)); } }
    public int PanelHeightPercent
    {
        get => _c.PanelHeightPercent;
        set { _c.PanelHeightPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPartialHeight)); OnPropertyChanged(nameof(SizeSummary)); }
    }
    public bool IsPartialHeight => _c.PanelHeightPercent < 100;
    public PanelAlignment PanelAlignment { get => _c.PanelAlignment; set { _c.PanelAlignment = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<PanelAlignment, string>> PanelAlignments { get; } = new[]
    {
        new KeyValuePair<PanelAlignment, string>(PanelAlignment.HotZone, L.T("At the hover zone")),
        new KeyValuePair<PanelAlignment, string>(PanelAlignment.Start, L.T("Top or left")),
        new KeyValuePair<PanelAlignment, string>(PanelAlignment.Center, L.T("Centered")),
        new KeyValuePair<PanelAlignment, string>(PanelAlignment.End, L.T("Bottom or right"))
    };
    public ItemLayout Layout { get => _c.Layout; set { _c.Layout = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<ItemLayout, string>> Layouts { get; } = new[]
    {
        new KeyValuePair<ItemLayout, string>(ItemLayout.List, L.T("List")),
        new KeyValuePair<ItemLayout, string>(ItemLayout.Grid, L.T("Grid (tiles)"))
    };
    public ItemSize ItemSize { get => _c.ItemSize; set { _c.ItemSize = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<ItemSize, string>> ItemSizes { get; } = new[]
    {
        new KeyValuePair<ItemSize, string>(ItemSize.Small, L.T("Small")),
        new KeyValuePair<ItemSize, string>(ItemSize.Medium, L.T("Medium")),
        new KeyValuePair<ItemSize, string>(ItemSize.Large, L.T("Large"))
    };
    public bool ShowRecent { get => _c.ShowRecent; set { _c.ShowRecent = value; OnPropertyChanged(); } }
    public bool ShowFavorites { get => _c.ShowFavorites; set { _c.ShowFavorites = value; OnPropertyChanged(); } }

    // ---- Tastenkürzel & Filter ----------------------------------------------

    public string Hotkey
    {
        get => _c.Hotkey;
        set { _c.Hotkey = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(HotkeyDisplay)); OnPropertyChanged(nameof(Summary)); }
    }
    public string HotkeyDisplay => string.IsNullOrWhiteSpace(_c.Hotkey) ? L.T("No hotkey") : DisplayText.Hotkey(_c.Hotkey);
    public ICommand ClearHotkeyCommand { get; }

    // ---- Farbe ------------------------------------------------------------

    private IReadOnlyList<ColorPresetViewModel>? _presets;
    private string _customColor = string.Empty;
    private string? _colorError;

    public IReadOnlyList<ColorPresetViewModel> ColorPresets =>
        _presets ??= PanelColors.Presets.Select(p => new ColorPresetViewModel(this, p)).ToList();

    // Wie die Akzentfarbe in den Windows-Einstellungen: Auswahl „Wie das Design“ / „Manuell“, darunter zuletzt
    // verwendete Farben, Panelfarben, Windows-Farben und „Benutzerdefinierte Farben“.

    private readonly List<string> _recentColors;
    private IReadOnlyList<ColorPresetViewModel>? _panelColors, _windowsColors, _recent;

    public static IReadOnlyList<KeyValuePair<string, string>> ColorModes { get; } = new[]
    {
        new KeyValuePair<string, string>("design", L.T("Like the design")),
        new KeyValuePair<string, string>("manual", L.T("Manual"))
    };

    public IReadOnlyList<KeyValuePair<string, string>> ColorModeList => ColorModes;

    /// <summary>„design“ (Farbe des hellen bzw. dunklen Designs) oder „manual“ (eigene Farbe).</summary>
    public string ColorMode
    {
        get => _manualMode || _c.PanelColor.Length > 0 ? "manual" : "design";
        set
        {
            _manualMode = value == "manual";
            if (_manualMode) ColorsExpanded = true; // wie in Windows: „Manuell“ klappt die Farben auf
            if (!_manualMode) PanelColor = string.Empty;
            else if (_c.PanelColor.Length == 0) PanelColor = _recentColors.FirstOrDefault() ?? "#1E1E1E";
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowManualColors));
        }
    }

    private bool _manualMode;

    public bool ShowManualColors => ColorMode == "manual";

    private bool _colorsExpanded, _backdropExpanded;

    /// <summary>Ausklappbereich „Panelfarbe“ offen.</summary>
    public bool ColorsExpanded { get => _colorsExpanded; set => Set(ref _colorsExpanded, value); }

    /// <summary>Ausklappbereich „Hintergrund“ offen (Regler der gewählten Variante).</summary>
    public bool BackdropExpanded { get => _backdropExpanded; set => Set(ref _backdropExpanded, value); }

    /// <summary>Zuletzt verwendete Farben (neueste zuerst).</summary>
    public IReadOnlyList<ColorPresetViewModel> RecentColors =>
        _recent ??= _recentColors.Select(h => new ColorPresetViewModel(this, new ColorPreset(h, h))).ToList();

    public bool HasRecentColors => _recentColors.Count > 0;

    /// <summary>Ruhige Panelfarben (dunkel und hell).</summary>
    public IReadOnlyList<ColorPresetViewModel> PanelColorChoices =>
        _panelColors ??= PanelColors.Presets.Where(p => p.Hex.Length > 0).Select(p => new ColorPresetViewModel(this, p, remember: true)).ToList();

    /// <summary>Die Farben aus den Windows-Einstellungen.</summary>
    public IReadOnlyList<ColorPresetViewModel> WindowsColors =>
        _windowsColors ??= PanelColors.WindowsPalette.Select(h => new ColorPresetViewModel(this, new ColorPreset(h, h), remember: true)).ToList();

    /// <summary>#RRGGBB oder leer (= Farbe des Designs).</summary>
    public string PanelColor
    {
        get => _c.PanelColor;
        set
        {
            var v = value ?? string.Empty;
            if (v.Length > 0 && PanelColors.TryParse(v, out var c)) v = c.Hex;
            _c.PanelColor = v;
            _customColor = v;
            _colorError = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CustomColor));
            OnPropertyChanged(nameof(ColorError));
            OnPropertyChanged(nameof(PreviewColor));
            OnPropertyChanged(nameof(PreviewText));
            OnPropertyChanged(nameof(ColorMode));
            OnPropertyChanged(nameof(ShowManualColors));
            foreach (var p in ColorPresets.Concat(PanelColorChoices).Concat(WindowsColors).Concat(RecentColors)) p.Refresh();
        }
    }

    /// <summary>Farbe aus einer Kachel wählen; aus den Paletten gewählte Farben kommen unter „Zuletzt verwendet“.</summary>
    internal void SelectColor(string hex, bool remember)
    {
        PanelColor = hex;
        if (remember && hex.Length > 0) RememberColor(_c.PanelColor);
    }

    /// <summary>Farbe vorn in „Zuletzt verwendet“ übernehmen.</summary>
    private void RememberColor(string hex)
    {
        var updated = PanelColors.AddRecent(_recentColors, hex);
        if (updated.SequenceEqual(_recentColors, StringComparer.OrdinalIgnoreCase)) return;
        _recentColors.Clear();
        _recentColors.AddRange(updated);
        _recent = null;
        OnPropertyChanged(nameof(RecentColors));
        OnPropertyChanged(nameof(HasRecentColors));
    }

    /// <summary>Eigene Panelfarbe im Farbwähler aussuchen.</summary>
    public System.Windows.Input.ICommand PickColorCommand => _pickColor ??= new RelayCommand(() =>
    {
        var picked = _app.Dialogs.PickColor(L.T("Panel color"), _c.PanelColor, _app.SettingsDialogOwner);
        if (picked == null) return;
        PanelColor = picked;
        RememberColor(_c.PanelColor); // benutzerdefinierte Farben landen unter „Zuletzt verwendet“
    });
    private System.Windows.Input.ICommand? _pickColor;

    public string CustomColor
    {
        get => _customColor.Length > 0 ? _customColor : _c.PanelColor;
        set
        {
            _customColor = value ?? string.Empty;
            if (_customColor.Trim().Length == 0) { PanelColor = string.Empty; return; }
            if (PanelColors.TryParse(_customColor, out _)) PanelColor = _customColor;
            else { _colorError = "Bitte als #RRGGBB angeben, z. B. #1B2A3D."; OnPropertyChanged(nameof(ColorError)); }
        }
    }

    public string? ColorError => _colorError;

    /// <summary>Vorschau der Panelfarbe mit Deckkraft; null = Farbe des Designs.</summary>
    public ArgbColor? PreviewColor => ArgbColor.FromHex(_c.PanelColor, (byte)(255 * _c.EffectiveOpacity / 100));

    public string PreviewText => PanelColors.TryParse(_c.PanelColor, out var c)
        ? (PanelColors.IsLight(c) ? L.T("Light surface – dark text") : L.T("Dark surface – light text"))
        : L.T("Design color");

    /// <summary>Hintergrund hinter dem Panel: Klarglas (WPF) oder Glas mit eigenem Weichzeichner (Windows 11).</summary>
    public PanelBackdrop Backdrop
    {
        get => _c.EffectiveBackdropSetting;
        set { _c.Backdrop = value; RaiseBackdrop(); }
    }

    public static IReadOnlyList<KeyValuePair<PanelBackdrop, string>> BackdropChoices { get; } = new[]
    {
        new KeyValuePair<PanelBackdrop, string>(PanelBackdrop.Clear, PanelBackdrops.Label(PanelBackdrop.Clear)),
        new KeyValuePair<PanelBackdrop, string>(PanelBackdrop.Glass, PanelBackdrops.Label(PanelBackdrop.Glass)),
        new KeyValuePair<PanelBackdrop, string>(PanelBackdrop.Solid, PanelBackdrops.Label(PanelBackdrop.Solid))
    };

    public IReadOnlyList<KeyValuePair<PanelBackdrop, string>> Backdrops => BackdropChoices;

    /// <summary>Variante, die tatsächlich läuft (Rückfall auf Klarglas: Windows 10, Kontrastdesign, Transparenzeffekte aus).</summary>
    private PanelBackdrop EffectiveBackdrop => PanelBackdrops.Effective(_c.EffectiveBackdropSetting, _app.Theme.OsBuild,
        _app.Theme.IsHighContrast, _app.Theme.TransparencyEffects);

    /// <summary>Was die gewählte Variante macht – und warum gegebenenfalls Klarglas läuft.</summary>
    public string BackdropDescription
    {
        get
        {
            if (_c.EffectiveBackdropSetting == PanelBackdrop.Solid)
                return L.T("Solid: the panel is opaque in its color – without transparency, frosted glass or blur.");
            if (_c.EffectiveBackdropSetting != PanelBackdrop.Glass)
                return L.T("Transparent: the desktop shows straight through, frosted glass calms it down.");
            if (EffectiveBackdrop == PanelBackdrop.Glass)
                return L.T("Windows blurs the desktop behind the panel, the panel tints it with its color. Windows 11 only.");
            if (_app.Theme.IsHighContrast) return L.T("In a contrast theme the background is opaque.");
            if (_app.Theme.OsBuild < PanelBackdrops.GlassBuild) return L.T("Windows 11 or later only – “Transparent” is used here.");
            return L.T("Windows transparency effects are off – “Transparent” is used.");
        }
    }

    /// <summary>Regler des Weichzeichners (nur wenn Glas gewählt ist und läuft).</summary>
    public bool ShowGlass => EffectiveBackdrop == PanelBackdrop.Glass;

    /// <summary>Blur-Stärke 0–100.</summary>
    public int GlassBlur { get => _c.GlassBlur; set { _c.GlassBlur = Math.Clamp(value, 0, 100); OnPropertyChanged(); } }

    /// <summary>Sättigung 0–200 % (100 = unverändert).</summary>
    public int GlassSaturation { get => _c.GlassSaturation; set { _c.GlassSaturation = Math.Clamp(value, 0, 200); OnPropertyChanged(); } }


    /// <summary>Hinweis zur Deckkraft.</summary>
    public string OpacityDescription => EffectiveBackdrop == PanelBackdrop.Glass
        ? L.T("Strength of the panel color tint over the blurred desktop. Text stays fully opaque.")
        : L.T("Below 100 % the desktop shows through; frosted glass lightens or darkens it. Text stays fully opaque.");

    /// <summary>Was das Milchglas macht – und warum es im Kontrastdesign fehlt.</summary>
    public string FrostDescription => _app.Theme.IsHighContrast
        ? L.T("In a contrast theme the panel is opaque, without frosted glass.")
        : EffectiveBackdrop == PanelBackdrop.Glass
            ? L.T("Brightness layer over the blur (dark panels darkened, light ones lightened) – text stays readable.")
            : L.T("Brightness layer under the panel color (dark panels darkened, light ones lightened) – ")
              + L.T("the desktop behind becomes calmer, text stays readable. Only takes effect below 100 % opacity.");

    public bool ShowFrost => PanelBackdrops.SupportsFrost(EffectiveBackdrop);

    /// <summary>Deckkraft-Regler (bei Vollton ohne Wirkung, daher ausgeblendet).</summary>
    public bool ShowOpacity => EffectiveBackdrop != PanelBackdrop.Solid;

    /// <summary>Milchglas 0–60 (0 = aus).</summary>
    public int Frost { get => _c.Frost; set { _c.Frost = Math.Clamp(value, 0, 60); OnPropertyChanged(); } }

    private void RaiseBackdrop()
    {
        foreach (var p in new[] { nameof(Backdrop), nameof(BackdropDescription), nameof(ShowGlass), nameof(FrostDescription), nameof(ShowFrost),
                     nameof(PreviewColor), nameof(OpacityDescription), nameof(ShowOpacity) }) OnPropertyChanged(p);
    }

    /// <summary>Mini-Dock mit den Favoriten am Rand ein- oder ausschalten; Einschalten klappt die Anzahl auf.</summary>
    public bool ShowDock { get => _c.ShowDock; set { _c.ShowDock = value; OnPropertyChanged(); if (value) DockExpanded = true; } }

    private bool _dockExpanded, _sizeExpanded, _hotZoneExpanded;
    public bool DockExpanded { get => _dockExpanded; set => Set(ref _dockExpanded, value); }
    public bool SizeExpanded { get => _sizeExpanded; set => Set(ref _sizeExpanded, value); }
    public bool HotZoneExpanded { get => _hotZoneExpanded; set => Set(ref _hotZoneExpanded, value); }

    /// <summary>Kopfzeile „Panel-Abmessungen“: z. B. „420 px × 100 %“.</summary>
    public string SizeSummary => $"{_c.PanelWidth} px × {_c.PanelHeightPercent} %";

    // ---- Eigene Akzentfarbe (nur ohne „Akzentfarbe von Windows“) ----------------------------

    /// <summary>Akzentfarbe des Panels; leer = aus der Panelfarbe abgeleitet.</summary>
    public string AccentColor
    {
        get => _c.AccentColor;
        set
        {
            var v = value ?? string.Empty;
            if (v.Length > 0 && PanelColors.TryParse(v, out var c)) v = c.Hex;
            _c.AccentColor = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AccentPreview));
            OnPropertyChanged(nameof(AccentText));
            OnPropertyChanged(nameof(HasAccentColor));
        }
    }

    public bool HasAccentColor => _c.AccentColor.Length > 0;
    public ArgbColor? AccentPreview => ArgbColor.FromHex(_c.AccentColor, 255) ?? ArgbColor.FromHex(_c.PanelColor, 255);
    public string AccentText => HasAccentColor ? _c.AccentColor : L.T("From the panel color");

    public System.Windows.Input.ICommand PickAccentCommand => _pickAccent ??= new RelayCommand(() =>
    {
        var picked = _app.Dialogs.PickColor(L.T("Accent color"), _c.AccentColor.Length > 0 ? _c.AccentColor : _c.PanelColor, _app.SettingsDialogOwner);
        if (picked != null) AccentColor = picked;
    });
    public System.Windows.Input.ICommand ResetAccentCommand => _resetAccent ??= new RelayCommand(() => AccentColor = string.Empty);
    private System.Windows.Input.ICommand? _pickAccent, _resetAccent;

    /// <summary>Designvorlage übernehmen (Farbe, Deckkraft, Hintergrund, Milchglas, Weichzeichner, Akzent).</summary>
    public void ApplyDesign(DesignPreset p)
    {
        Appearance.Apply(_c, p);
        _customColor = _c.PanelColor;
        _manualMode = _c.PanelColor.Length > 0;
        foreach (var name in new[] { nameof(PanelColor), nameof(CustomColor), nameof(PanelOpacity), nameof(Frost), nameof(GlassBlur), nameof(GlassSaturation),
                     nameof(PreviewText), nameof(ColorMode), nameof(ShowManualColors) })
            OnPropertyChanged(name);
        AccentColor = _c.AccentColor;
        RaiseBackdrop();
        foreach (var c in ColorPresets.Concat(PanelColorChoices).Concat(WindowsColors).Concat(RecentColors)) c.Refresh();
    }

    /// <summary>Anzahl der Favoriten im Mini-Dock.</summary>
    public int DockCount { get => _c.DockCount; set { _c.DockCount = Math.Clamp(value, 1, 20); OnPropertyChanged(); } }

    public bool ShowJumpRail { get => _c.ShowJumpRail; set { _c.ShowJumpRail = value; OnPropertyChanged(); } }

    /// <summary>Aussehen der Gruppen: getönte Karten oder Fluent-Karten mit Akzentstreifen.</summary>
    public GroupCardStyle GroupStyle { get => _c.GroupStyle; set { _c.GroupStyle = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<GroupCardStyle, string>> GroupStyles { get; } = new[]
    {
        new KeyValuePair<GroupCardStyle, string>(GroupCardStyle.Accent, L.T("Accent stripe (Fluent cards)")),
        new KeyValuePair<GroupCardStyle, string>(GroupCardStyle.Tinted, L.T("Colored cards"))
    };
    public bool ShowScrollBar { get => _c.ShowScrollBar; set { _c.ShowScrollBar = value; OnPropertyChanged(); } }

    /// <summary>Tastenkürzel öffnet die Suche in der Bildschirmmitte statt des Panels.</summary>
    public bool HotkeyOpensSpotlight { get => _c.HotkeyOpensSpotlight; set { _c.HotkeyOpensSpotlight = value; OnPropertyChanged(); } }

    public int PanelOpacity
    {
        get => _c.PanelOpacity;
        set { _c.PanelOpacity = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewColor)); OnPropertyChanged(nameof(OpacityDescription)); }
    }

    /// <summary>Erstellt die gespeicherte Panel-Konfiguration aus dem Formular.</summary>
    public PanelConfig ToConfig() => _c.Clone();
}


public sealed class ColorPresetViewModel : ObservableObject
{
    private readonly PanelSettingsViewModel _owner;

    /// <param name="remember">Beim Wählen unter „Zuletzt verwendet“ aufnehmen (Paletten, nicht die Zuletzt-Liste selbst).</param>
    public ColorPresetViewModel(PanelSettingsViewModel owner, ColorPreset preset, bool remember = false)
    {
        _owner = owner;
        Name = preset.Name;
        Hex = preset.Hex;
        SwatchColor = ArgbColor.FromHex(preset.Hex);
        SelectCommand = new RelayCommand(() => _owner.SelectColor(Hex, remember));
    }

    public string Name { get; }
    public string Hex { get; }
    /// <summary>Farbpunkt; null = Farbe des Designs (die Oberfläche zeigt dann die Panelfarbe des Designs).</summary>
    public ArgbColor? SwatchColor { get; }
    public string ToolTip => Hex.Length == 0 ? L.T("Design color (light/dark)") : Name == Hex ? Hex : $"{Name} ({Hex})";
    public bool IsSelected => string.Equals(_owner.PanelColor, Hex, StringComparison.OrdinalIgnoreCase);
    public ICommand SelectCommand { get; }
    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>Eine Gruppe in den Einstellungen (Inhalte › Gruppen); smarte Gruppen haben einen Ordner.</summary>
