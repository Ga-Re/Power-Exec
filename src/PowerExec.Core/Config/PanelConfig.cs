using PowerExec.Core.Search;

namespace PowerExec.Core.Config;

/// <summary>Darstellung der Einträge: Liste oder Raster (Kacheln). „Compact“ stammt aus 0.9 und wird zu Liste + klein.</summary>
public enum ItemLayout { List, Compact, Grid }

/// <summary>Größe der Einträge im Panel (Liste und Raster).</summary>
public enum ItemSize { Small, Medium, Large }

/// <summary>Einstellungen eines einzelnen Panels (Position, Hover-Zone, Aussehen, Inhalt).</summary>
public sealed class PanelConfig
{
    /// <summary>Kennung für „Eigene Einträge“ in <see cref="Folders"/>.</summary>
    public const string CustomEntriesKey = "custom:";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Hauptpanel";

    public ScreenEdge Edge { get; set; } = ScreenEdge.Right;
    public MonitorMode MonitorMode { get; set; } = MonitorMode.Primary;
    public int MonitorIndex { get; set; }
    public HotZoneMode HotZone { get; set; } = HotZoneMode.Middle;
    public int HotZoneThickness { get; set; } = 4;
    public int HotZoneLength { get; set; } = 200;
    public int PanelWidth { get; set; } = 400;
    public int PanelHeightPercent { get; set; } = 100;
    public PanelAlignment PanelAlignment { get; set; } = PanelAlignment.HotZone;
    public string PanelColor { get; set; } = string.Empty;
    public int PanelOpacity { get; set; } = 100;
    /// <summary>Acrylic: Hintergrund hinter dem Panel weichzeichnen (Windows 10 1803+; sonst nur Weichzeichnen).</summary>
    public bool Acrylic { get; set; }
    public string Hotkey { get; set; } = string.Empty;
    public TypeFilter DefaultFilter { get; set; } = TypeFilter.All;
    public bool Pinned { get; set; }

    /// <summary>Kompakt: in der Liste eine Zeile pro Eintrag, im Raster kleinere Kacheln.</summary>
    public bool Compact { get; set; }

    public ItemLayout Layout { get; set; } = ItemLayout.List;

    public ItemSize ItemSize { get; set; } = ItemSize.Medium;

    /// <summary>Gruppe „Zuletzt ausgeführt“ in diesem Panel zeigen.</summary>
    public bool ShowRecent { get; set; } = true;

    /// <summary>Gruppe „Favoriten“ in diesem Panel zeigen.</summary>
    public bool ShowFavorites { get; set; } = true;

    /// <summary>Angezeigte Ordner (Pfade, dazu ggf. <see cref="CustomEntriesKey"/>). Leer = alle.</summary>
    public List<string> Folders { get; set; } = new();

    /// <summary>Angezeigte Kategorien. Leer = alle.</summary>
    public List<string> Categories { get; set; } = new();

    public bool ShowsFolder(string folderPath) =>
        Folders.Count == 0 || Folders.Contains(folderPath, StringComparer.OrdinalIgnoreCase);

    public bool ShowsCategory(string category) =>
        Categories.Count == 0 || Categories.Contains(category, StringComparer.CurrentCultureIgnoreCase);

    public PanelConfig Clone() => new()
    {
        Id = Id, Name = Name, Edge = Edge, MonitorMode = MonitorMode, MonitorIndex = MonitorIndex, HotZone = HotZone,
        HotZoneThickness = HotZoneThickness, HotZoneLength = HotZoneLength, PanelWidth = PanelWidth,
        PanelHeightPercent = PanelHeightPercent, PanelAlignment = PanelAlignment, PanelColor = PanelColor,
        PanelOpacity = PanelOpacity, Acrylic = Acrylic, Hotkey = Hotkey, DefaultFilter = DefaultFilter, Pinned = Pinned, Compact = Compact,
        Layout = Layout, ShowRecent = ShowRecent, ShowFavorites = ShowFavorites, ItemSize = ItemSize,
        Folders = new List<string>(Folders), Categories = new List<string>(Categories)
    };

    /// <summary>Übernimmt die Panel-Einstellungen aus Versionen vor 0.7 (ein Panel, globale Felder).</summary>
    public static PanelConfig FromLegacy(AppSettings s) => new()
    {
        Name = "Hauptpanel",
        Edge = s.Edge, MonitorMode = s.MonitorMode, MonitorIndex = s.MonitorIndex, HotZone = s.HotZone,
        HotZoneThickness = s.HotZoneThickness, HotZoneLength = s.HotZoneLength, PanelWidth = s.PanelWidth,
        PanelHeightPercent = s.PanelHeightPercent, PanelAlignment = s.PanelAlignment, PanelColor = s.PanelColor,
        PanelOpacity = s.PanelOpacity, Hotkey = s.Hotkey, DefaultFilter = s.DefaultFilter, Pinned = s.Pinned
    };

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(Name)) Name = "Panel";
        if (Layout == ItemLayout.Compact) { Layout = ItemLayout.List; Compact = true; }
        // „Kompakt“ (bis 0.12) wird zur Elementgröße „Klein“
        if (Compact) { ItemSize = ItemSize.Small; Compact = false; }
        // Inhaltsauswahl pro Panel gibt es seit 0.13 nicht mehr (Arbeitsbereiche wählen Gruppen)
        Folders.Clear();
        Categories.Clear();
        PanelWidth = Math.Clamp(PanelWidth, 280, 900);
        PanelOpacity = Math.Clamp(PanelOpacity, 50, 100);
        PanelHeightPercent = Math.Clamp(PanelHeightPercent, 30, 100);
        HotZoneThickness = Math.Clamp(HotZoneThickness, 1, 40);
        HotZoneLength = Math.Clamp(HotZoneLength, 40, 4000);
        if (!PanelColors.TryParse(PanelColor, out _)) PanelColor = string.Empty;
    }
}
