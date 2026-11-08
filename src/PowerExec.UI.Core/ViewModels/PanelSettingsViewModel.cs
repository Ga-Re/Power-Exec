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

    public PanelSettingsViewModel(ISettingsHost app, PanelConfig config, IReadOnlyList<KeyValuePair<int, string>> monitors)
    {
        _app = app;
        _c = config.Clone();
        Monitors = monitors;
        ClearHotkeyCommand = new RelayCommand(() => Hotkey = string.Empty);
    }

    public string Id => _c.Id;

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
            var hk = string.IsNullOrWhiteSpace(_c.Hotkey) ? "kein Tastenkürzel" : DisplayText.Hotkey(_c.Hotkey);
            return $"{edge} · {mon} · {hk}";
        }
    }

    // ---- Position ---------------------------------------------------------

    public ScreenEdge Edge { get => _c.Edge; set { _c.Edge = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); } }
    public static IReadOnlyList<KeyValuePair<ScreenEdge, string>> Edges { get; } = new[]
    {
        new KeyValuePair<ScreenEdge, string>(ScreenEdge.Right, "Rechts"),
        new KeyValuePair<ScreenEdge, string>(ScreenEdge.Left, "Links"),
        new KeyValuePair<ScreenEdge, string>(ScreenEdge.Top, "Oben"),
        new KeyValuePair<ScreenEdge, string>(ScreenEdge.Bottom, "Unten")
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
        new KeyValuePair<HotZoneMode, string>(HotZoneMode.Middle, "Mitte · eigene Länge"),
        new KeyValuePair<HotZoneMode, string>(HotZoneMode.FirstHalf, "Erste Hälfte (oben bzw. links)"),
        new KeyValuePair<HotZoneMode, string>(HotZoneMode.Full, "Ganze Kante")
    };

    // ---- Größe ------------------------------------------------------------

    public int PanelWidth { get => _c.PanelWidth; set { _c.PanelWidth = value; OnPropertyChanged(); } }
    public int PanelHeightPercent
    {
        get => _c.PanelHeightPercent;
        set { _c.PanelHeightPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPartialHeight)); }
    }
    public bool IsPartialHeight => _c.PanelHeightPercent < 100;
    public PanelAlignment PanelAlignment { get => _c.PanelAlignment; set { _c.PanelAlignment = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<PanelAlignment, string>> PanelAlignments { get; } = new[]
    {
        new KeyValuePair<PanelAlignment, string>(PanelAlignment.HotZone, "An der Hover-Zone"),
        new KeyValuePair<PanelAlignment, string>(PanelAlignment.Start, "Oben bzw. links"),
        new KeyValuePair<PanelAlignment, string>(PanelAlignment.Center, "Mittig"),
        new KeyValuePair<PanelAlignment, string>(PanelAlignment.End, "Unten bzw. rechts")
    };
    public ItemLayout Layout { get => _c.Layout; set { _c.Layout = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<ItemLayout, string>> Layouts { get; } = new[]
    {
        new KeyValuePair<ItemLayout, string>(ItemLayout.List, "Liste"),
        new KeyValuePair<ItemLayout, string>(ItemLayout.Grid, "Raster (Kacheln)")
    };
    public ItemSize ItemSize { get => _c.ItemSize; set { _c.ItemSize = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<ItemSize, string>> ItemSizes { get; } = new[]
    {
        new KeyValuePair<ItemSize, string>(ItemSize.Small, "Klein"),
        new KeyValuePair<ItemSize, string>(ItemSize.Medium, "Mittel"),
        new KeyValuePair<ItemSize, string>(ItemSize.Large, "Groß")
    };
    public bool ShowRecent { get => _c.ShowRecent; set { _c.ShowRecent = value; OnPropertyChanged(); } }
    public bool ShowFavorites { get => _c.ShowFavorites; set { _c.ShowFavorites = value; OnPropertyChanged(); } }

    // ---- Tastenkürzel & Filter ----------------------------------------------

    public string Hotkey
    {
        get => _c.Hotkey;
        set { _c.Hotkey = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(HotkeyDisplay)); OnPropertyChanged(nameof(Summary)); }
    }
    public string HotkeyDisplay => string.IsNullOrWhiteSpace(_c.Hotkey) ? "Kein Tastenkürzel" : DisplayText.Hotkey(_c.Hotkey);
    public ICommand ClearHotkeyCommand { get; }

    // ---- Farbe ------------------------------------------------------------

    private IReadOnlyList<ColorPresetViewModel>? _presets;
    private string _customColor = string.Empty;
    private string? _colorError;

    public IReadOnlyList<ColorPresetViewModel> ColorPresets =>
        _presets ??= PanelColors.Presets.Select(p => new ColorPresetViewModel(this, p)).ToList();

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
            foreach (var p in ColorPresets) p.Refresh();
        }
    }

    /// <summary>Eigene Panelfarbe im Farbwähler aussuchen.</summary>
    public System.Windows.Input.ICommand PickColorCommand => _pickColor ??= new RelayCommand(() =>
    {
        var picked = _app.Dialogs.PickColor("Panelfarbe", _c.PanelColor, _app.SettingsDialogOwner);
        if (picked != null) PanelColor = picked;
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
    public ArgbColor? PreviewColor => ArgbColor.FromHex(_c.PanelColor, (byte)(255 * _c.PanelOpacity / 100));

    public string PreviewText => PanelColors.TryParse(_c.PanelColor, out var c)
        ? (PanelColors.IsLight(c) ? "Helle Fläche – dunkle Schrift" : "Dunkle Fläche – helle Schrift")
        : "Farbe des Designs";

    /// <summary>Hintergrund hinter dem Panel (derzeit nur Klarglas mit Milchglas).</summary>
    public PanelBackdrop Backdrop
    {
        get => _c.EffectiveBackdropSetting;
        set { _c.Backdrop = value; RaiseBackdrop(); }
    }

    /// <summary>Hinweis zur Deckkraft.</summary>
    public string OpacityDescription =>
        "Unter 100 % scheint der Desktop durch; Milchglas hellt ihn auf bzw. dunkelt ihn ab. Schrift bleibt voll deckend.";

    /// <summary>Was das Milchglas macht – und warum es im Kontrastdesign fehlt.</summary>
    public string FrostDescription => _app.Theme.IsHighContrast
        ? "Im Kontrastdesign ist das Panel deckend, Milchglas entfällt."
        : "Helligkeitsschicht unter der Panelfarbe (dunkle Panels abgedunkelt, helle aufgehellt) mit feiner Körnung – "
          + "der Desktop dahinter wird ruhiger, Schrift bleibt gut lesbar. Wirkt nur unter 100 % Deckkraft.";

    public bool ShowFrost => PanelBackdrops.SupportsFrost(PanelBackdrops.Effective(_c.EffectiveBackdropSetting, Environment.OSVersion.Version.Build, false));

    /// <summary>Milchglas 0–60 (0 = aus).</summary>
    public int Frost { get => _c.Frost; set { _c.Frost = Math.Clamp(value, 0, 60); OnPropertyChanged(); } }

    private void RaiseBackdrop()
    {
        foreach (var p in new[] { nameof(Backdrop), nameof(FrostDescription), nameof(ShowFrost), nameof(PreviewColor), nameof(OpacityDescription) }) OnPropertyChanged(p);
    }

    /// <summary>Mini-Dock mit den Favoriten am Rand ein- oder ausschalten.</summary>
    public bool ShowDock { get => _c.ShowDock; set { _c.ShowDock = value; OnPropertyChanged(); } }

    /// <summary>Anzahl der Favoriten im Mini-Dock.</summary>
    public int DockCount { get => _c.DockCount; set { _c.DockCount = Math.Clamp(value, 1, 20); OnPropertyChanged(); } }

    public bool ShowJumpRail { get => _c.ShowJumpRail; set { _c.ShowJumpRail = value; OnPropertyChanged(); } }
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

    public ColorPresetViewModel(PanelSettingsViewModel owner, ColorPreset preset)
    {
        _owner = owner;
        Name = preset.Name;
        Hex = preset.Hex;
        SwatchColor = ArgbColor.FromHex(preset.Hex);
        SelectCommand = new RelayCommand(() => _owner.PanelColor = Hex);
    }

    public string Name { get; }
    public string Hex { get; }
    /// <summary>Farbpunkt; null = Farbe des Designs (die Oberfläche zeigt dann die Panelfarbe des Designs).</summary>
    public ArgbColor? SwatchColor { get; }
    public string ToolTip => Hex.Length == 0 ? "Farbe des Designs (hell/dunkel)" : $"{Name} ({Hex})";
    public bool IsSelected => string.Equals(_owner.PanelColor, Hex, StringComparison.OrdinalIgnoreCase);
    public ICommand SelectCommand { get; }
    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>Eine Gruppe in den Einstellungen (Inhalte › Gruppen); smarte Gruppen haben einen Ordner.</summary>
