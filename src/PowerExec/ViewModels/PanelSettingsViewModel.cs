using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PowerExec.Core.Config;
using PowerExec.Core.Search;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

/// <summary>Ein Ordner oder eine Kategorie zum Anhaken im Panel-Inhalt.</summary>
public sealed class ContentChoiceViewModel : ObservableObject
{
    private bool _isChecked;

    public ContentChoiceViewModel(string key, string label, string detail, bool isChecked)
    {
        Key = key;
        Label = label;
        Detail = detail;
        _isChecked = isChecked;
    }

    public string Key { get; }
    public string Label { get; }
    public string Detail { get; }
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

/// <summary>Einstellungen eines Panels im Einstellungsfenster (Arbeitskopie von <see cref="PanelConfig"/>).</summary>
public sealed class PanelSettingsViewModel : ObservableObject
{
    private readonly PanelConfig _c;
    private bool _showAll;

    public PanelSettingsViewModel(PanelConfig config, IReadOnlyList<KeyValuePair<int, string>> monitors,
        IEnumerable<string> folders, IEnumerable<string> categories)
    {
        _c = config.Clone();
        Monitors = monitors;
        _showAll = _c.Folders.Count == 0 && _c.Categories.Count == 0;
        foreach (var f in folders)
            FolderChoices.Add(new ContentChoiceViewModel(f, System.IO.Path.GetFileName(f.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : f, f,
                _c.Folders.Count == 0 || _c.Folders.Contains(f, StringComparer.OrdinalIgnoreCase)));
        FolderChoices.Add(new ContentChoiceViewModel(PanelConfig.CustomEntriesKey, "Eigene Einträge", "Webseiten, Ordner, RDP, Befehle",
            _c.Folders.Count == 0 || _c.Folders.Contains(PanelConfig.CustomEntriesKey, StringComparer.OrdinalIgnoreCase)));
        foreach (var cat in categories)
            CategoryChoices.Add(new ContentChoiceViewModel(cat, GroupManager.Display(cat), string.Empty,
                _c.Categories.Count == 0 || _c.Categories.Contains(cat, StringComparer.CurrentCultureIgnoreCase)));
        CategoryChoices.Add(new ContentChoiceViewModel(string.Empty, "Ohne Gruppe", string.Empty,
            _c.Categories.Count == 0 || _c.Categories.Contains(string.Empty)));
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
            var hk = string.IsNullOrWhiteSpace(_c.Hotkey) ? "kein Tastenkürzel" : HotkeyService.Display(_c.Hotkey);
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
    public bool Compact { get => _c.Compact; set { _c.Compact = value; OnPropertyChanged(); } }
    public bool ShowRecent { get => _c.ShowRecent; set { _c.ShowRecent = value; OnPropertyChanged(); } }
    public bool ShowFavorites { get => _c.ShowFavorites; set { _c.ShowFavorites = value; OnPropertyChanged(); } }

    // ---- Tastenkürzel & Filter ----------------------------------------------

    public string Hotkey
    {
        get => _c.Hotkey;
        set { _c.Hotkey = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(HotkeyDisplay)); OnPropertyChanged(nameof(Summary)); }
    }
    public string HotkeyDisplay => string.IsNullOrWhiteSpace(_c.Hotkey) ? "Kein Tastenkürzel" : HotkeyService.Display(_c.Hotkey);
    public ICommand ClearHotkeyCommand { get; }

    public TypeFilter DefaultFilter { get => _c.DefaultFilter; set { _c.DefaultFilter = value; OnPropertyChanged(); } }
    public IReadOnlyList<KeyValuePair<TypeFilter, string>> Filters { get; } = new[]
    {
        new KeyValuePair<TypeFilter, string>(TypeFilter.All, "Alle"),
        new KeyValuePair<TypeFilter, string>(TypeFilter.Scripts, "Scripte"),
        new KeyValuePair<TypeFilter, string>(TypeFilter.Programs, "Programme"),
        new KeyValuePair<TypeFilter, string>(TypeFilter.Favorites, "Favoriten")
    };

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
            OnPropertyChanged(nameof(PreviewBrush));
            OnPropertyChanged(nameof(PreviewText));
            foreach (var p in ColorPresets) p.Refresh();
        }
    }

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

    public Brush PreviewBrush => PanelColors.TryParse(_c.PanelColor, out var c)
        ? new SolidColorBrush(Color.FromArgb((byte)(255 * _c.PanelOpacity / 100), c.R, c.G, c.B))
        : Application.Current.Resources["Bg.Panel"] as Brush ?? Brushes.Gray;

    public string PreviewText => PanelColors.TryParse(_c.PanelColor, out var c)
        ? (PanelColors.IsLight(c) ? "Helle Fläche – dunkle Schrift" : "Dunkle Fläche – helle Schrift")
        : "Farbe des Designs";

    public int PanelOpacity
    {
        get => _c.PanelOpacity;
        set { _c.PanelOpacity = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewBrush)); }
    }

    // ---- Inhalt -----------------------------------------------------------

    /// <summary>true = alle Ordner und Kategorien anzeigen (keine Auswahl).</summary>
    public bool ShowAll
    {
        get => _showAll;
        set { if (Set(ref _showAll, value)) OnPropertyChanged(nameof(ShowSelection)); }
    }

    public bool ShowSelection { get => !_showAll; set => ShowAll = !value; }

    public ObservableCollection<ContentChoiceViewModel> FolderChoices { get; } = new();
    public ObservableCollection<ContentChoiceViewModel> CategoryChoices { get; } = new();
    public bool HasCategories => CategoryChoices.Count > 1;

    /// <summary>Erstellt die gespeicherte Panel-Konfiguration aus dem Formular.</summary>
    public PanelConfig ToConfig()
    {
        var c = _c.Clone();
        if (_showAll)
        {
            c.Folders.Clear();
            c.Categories.Clear();
        }
        else
        {
            // Alles angehakt = keine Einschränkung (dann erscheinen auch neu hinzugefügte Ordner).
            c.Folders = FolderChoices.All(f => f.IsChecked) ? new List<string>() : FolderChoices.Where(f => f.IsChecked).Select(f => f.Key).ToList();
            c.Categories = CategoryChoices.All(f => f.IsChecked) ? new List<string>() : CategoryChoices.Where(f => f.IsChecked).Select(f => f.Key).ToList();
            if (c.Folders.Count == 0 && FolderChoices.All(f => !f.IsChecked)) c.Folders.Add("-"); // bewusst leer
        }
        return c;
    }
}
