using PowerExec.Core.Search;

namespace PowerExec.Core.Config;

public enum ScreenEdge { Right, Left, Top, Bottom }

public enum MonitorMode { Primary, Specific, All }

public enum HotZoneMode { Middle, FirstHalf, Full }

/// <summary>Lage des Panels entlang der Kante, wenn es nicht die volle Höhe hat.</summary>
public enum PanelAlignment { HotZone, Start, Center, End }

public enum AnimationMode { Smooth, Fast, Off }

public enum ThemeMode { System, Dark, Light }

/// <summary>Gruppierung im Panel.</summary>
public enum GroupingMode { Folder, Category }

public enum PsEngine { WindowsPowerShell, PowerShell7 }

public enum RemoteEngineMode { WindowsPowerShell, PreferPowerShell7 }

public enum EditorKind { VSCode, Ise, Notepad, Custom }

/// <summary>Wie neue Dateien in einem Ordner behandelt werden.</summary>
public enum NewFilesMode { AutoShow, RequireApproval }

public sealed class FolderConfig
{
    public string Path { get; set; } = string.Empty;
    public bool Recursive { get; set; } = true;

    /// <summary>Dateiendungen inkl. Punkt. Leer = Standard-Dateiendungen verwenden.</summary>
    public List<string> Extensions { get; set; } = new();

    /// <summary>Muster-Ausschlüsse: Typ (*.exe) oder Muster (*\Archiv\*). Einzelne Dateien stehen in <see cref="Hidden"/>.</summary>
    public List<string> Excludes { get; set; } = new();

    /// <summary>Einzeln ausgeblendete Dateien (Pfad relativ zum Ordner; ohne \ = Dateiname in allen Unterordnern).</summary>
    public List<string> Hidden { get; set; } = new();

    /// <summary>Bereits bekannte Dateien (relativ). Im Freigabe-Modus ist alles andere „neu“.</summary>
    public List<string> KnownFiles { get; set; } = new();

    public NewFilesMode NewFiles { get; set; } = NewFilesMode.AutoShow;

    public FolderConfig Clone() => new()
    {
        Path = Path,
        Recursive = Recursive,
        Extensions = new List<string>(Extensions),
        Excludes = new List<string>(Excludes),
        Hidden = new List<string>(Hidden),
        KnownFiles = new List<string>(KnownFiles),
        NewFiles = NewFiles,
        ShowGroup = ShowGroup,
        Group = Group,
        SubfoldersAsGroups = SubfoldersAsGroups
    };

    /// <summary>Smarte Gruppe, in der der Ordnerinhalt erscheint (Pfad wie „Server/Tools“).</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Unterordner im Panel als Untergruppen abbilden (nur mit „Unterordner einbeziehen“).</summary>
    public bool SubfoldersAsGroups { get; set; }

    /// <summary>Bis 0.12: Ordnergruppe anzeigen. Wird in die Gruppenauswahl der Arbeitsbereiche übernommen.</summary>
    public bool ShowGroup { get; set; } = true;
}

public sealed class MachineEntry
{
    public string Name { get; set; } = string.Empty;
    public bool Favorite { get; set; }
    public DateTime LastUsedUtc { get; set; }
}

public sealed class AppSettings
{
    public static readonly string[] StandardExtensions = [".ps1", ".cmd", ".bat", ".exe", ".lnk", ".msc"];

    // Ordner
    public List<FolderConfig> Folders { get; set; } = new();
    public List<string> DefaultExtensions { get; set; } = new(StandardExtensions);
    public List<string> GlobalExcludes { get; set; } = new();
    public bool AutoRescan { get; set; } = true;

    /// <summary>Sprache der Oberfläche: "" = wie Windows, "de" oder "en".</summary>
    public string Language { get; set; } = string.Empty;

    // Panel
    public ScreenEdge Edge { get; set; } = ScreenEdge.Right;
    public MonitorMode MonitorMode { get; set; } = MonitorMode.Primary;
    public int MonitorIndex { get; set; }
    public HotZoneMode HotZone { get; set; } = HotZoneMode.Middle;

    /// <summary>Breite (Dicke) der Hover-Zone in px.</summary>
    public int HotZoneThickness { get; set; } = 4;

    /// <summary>Länge der Hover-Zone in px bei <see cref="HotZoneMode.Middle"/>.</summary>
    public int HotZoneLength { get; set; } = 200;

    /// <summary>Höhe des Panels in Prozent des Arbeitsbereichs (30–100).</summary>
    public int PanelHeightPercent { get; set; } = 100;

    public PanelAlignment PanelAlignment { get; set; } = PanelAlignment.HotZone;
    public int HoverDelayMs { get; set; } = 250;
    public int CollapseDelayMs { get; set; } = 400;
    public int PanelWidth { get; set; } = 400;
    public AnimationMode Animation { get; set; } = AnimationMode.Smooth;
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>Akzentfarbe von Windows übernehmen (statt der festen Akzentfarbe des Designs).</summary>
    public bool UseWindowsAccent { get; set; }


    /// <summary>Parameter-Formular direkt im aufgeklappten Eintrag statt nur im Ausführen-Dialog.</summary>
    public bool InlineParameters { get; set; } = true;

    /// <summary>Zuletzt gesehene Version (für „Neu in dieser Version“); leer = noch nie.</summary>
    public string LastSeenVersion { get; set; } = string.Empty;

    /// <summary>Einführungs-Assistent wurde abgeschlossen oder übersprungen.</summary>
    public bool WelcomeDone { get; set; }

    /// <summary>Größe von Schrift und Bedienelementen in Prozent (80–150).</summary>
    public int UiScalePercent { get; set; } = 100;
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public bool StartWithWindows { get; set; }
    public bool Pinned { get; set; }

    /// <summary>Filter, der beim Öffnen des Panels ausgewählt ist.</summary>
    public TypeFilter DefaultFilter { get; set; } = TypeFilter.All;

    /// <summary>Hintergrundfarbe des Panels als #RRGGBB. Leer = Farbe des Designs.</summary>
    public string PanelColor { get; set; } = string.Empty;

    /// <summary>Deckkraft des Panels in Prozent (50–100).</summary>
    public int PanelOpacity { get; set; } = 100;

    /// <summary>Programmsymbole für EXE, Verknüpfungen und Snap-ins anzeigen.</summary>
    public bool ShowProgramIcons { get; set; } = true;

    /// <summary>Hover-Zone ausblenden, solange auf dem Monitor ein Programm im Vollbild läuft.</summary>
    public bool HideHotZoneInFullscreen { get; set; } = true;

    // Gruppen ziehen (Panel und Einstellungen): an der Zielstelle öffnet sich eine Lücke mit Platzhalter
    /// <summary>Beim Ziehen einer Gruppe Einträge bzw. Kartendetails zuklappen (kürzere Wege).</summary>
    public bool GroupDragCollapse { get; set; }
    /// <summary>An ungültigen Stellen am Mauszeiger erklären, warum dort nicht abgelegt werden kann.</summary>
    public bool GroupDragExplain { get; set; }
    /// <summary>Ziehbild am Mauszeiger: Name, Farbe und Zahl der Untergruppen.</summary>
    public bool GroupDragGhost { get; set; } = true;
    /// <summary>Lücke öffnet und schließt sich weich (nur, wenn Animationen eingeschaltet sind).</summary>
    public bool GroupDragAnimate { get; set; } = true;

    /// <summary>Gruppen im Panel: nach Ordner oder nach eigenen Kategorien.</summary>
    public GroupingMode Grouping { get; set; } = GroupingMode.Folder;

    /// <summary>Reihenfolge der Kategorien im Panel.</summary>
    public List<string> CategoryOrder { get; set; } = new();

    /// <summary>Anzahl der Einträge in „Zuletzt ausgeführt“ (0 = Gruppe ausblenden).</summary>
    public int RecentRunsCount { get; set; } = 5;

    /// <summary>Zuletzt verwendete Größe des Eintragsdialogs je Art („Url“ → „900x700“).</summary>
    public Dictionary<string, string> EditorSizes { get; set; } = new();

    /// <summary>Zuletzt gewählte Panelfarben (#RRGGBB, neueste zuerst, höchstens <see cref="PanelColors.RecentCount"/>).</summary>
    public List<string> RecentColors { get; set; } = new();

    // Panel-Zustand
    public List<string> Favorites { get; set; } = new();
    public List<string> CollapsedGroups { get; set; } = new();

    /// <summary>Zuletzt ausgeführte Einträge (neueste zuerst).</summary>
    public List<RecentRun> RecentRuns { get; set; } = new();

    /// <summary>Abonnierte Team-Kataloge.</summary>
    public List<CatalogSubscription> Catalogs { get; set; } = new();

    /// <summary>Geplante Starts (laufen, solange Power-Exec läuft).</summary>
    public List<Schedule> Schedules { get; set; } = new();

    /// <summary>Häufigkeit und letzter Start je Eintrag (Sortierung der Suche).</summary>
    public Dictionary<string, RunStat> RunStats { get; set; } = new();

    /// <summary>Eigene Reihenfolge je Gruppe (Drag &amp; Drop): Gruppenschlüssel → Pfade.</summary>
    public Dictionary<string, List<string>> CustomOrder { get; set; } = new();

    /// <summary>Fingerabdrücke (Größe:SHA-256) von Dateien mit Profil oder Favorit – zum Wiederfinden nach Umbenennen.</summary>
    public Dictionary<string, string> Fingerprints { get; set; } = new();

    // Ausführung
    public PsEngine DefaultEngine { get; set; } = PsEngine.PowerShell7;
    public RemoteEngineMode RemoteEngine { get; set; } = RemoteEngineMode.WindowsPowerShell;
    public string ExecutionPolicy { get; set; } = "Bypass";
    public bool NoProfile { get; set; } = true;
    public bool NoExit { get; set; } = true;
    /// <summary>Beim Start eines Eintrags mit mehreren Profilen nach dem Profil fragen (je Eintrag abweichend einstellbar).</summary>
    public bool AskProfile { get; set; }
    public int ThrottleLimit { get; set; } = 16;
    public int RemoteTimeoutSeconds { get; set; } = 60;

    /// <summary>Remote über HTTPS (WinRM-Port 5986).</summary>
    public bool RemoteUseSsl { get; set; }

    /// <summary>Zeitlimit beim Einlesen eines Ordners (Sekunden). Danach wird der zwischengespeicherte Stand gezeigt.</summary>
    public int ScanTimeoutSeconds { get; set; } = 10;

    /// <summary>Windows-Benachrichtigung, wenn eine Remote- oder Massenausführung fertig ist.</summary>
    public bool NotifyOnFinish { get; set; } = true;
    public string LogFolder { get; set; } = string.Empty;
    public EditorKind Editor { get; set; } = EditorKind.VSCode;
    public string EditorPath { get; set; } = string.Empty;
    public List<MachineEntry> Machines { get; set; } = new();
    public int MaxRecentMachines { get; set; } = 20;

    /// <summary>Anzeigename, Beschreibung und Startprofile je Eintrag (Schlüssel: vollständiger Pfad).</summary>
    public List<EntryConfig> Entries { get; set; } = new();

    /// <summary>Einträge ohne Datei (Webseiten, Ordner, RDP, Befehle) des aktiven Arbeitsbereichs.</summary>
    /// <summary>Symbole eigener Gruppen: Gruppenpfad → <see cref="IconRef"/>-Text.</summary>
    public Dictionary<string, string> GroupIcons { get; set; } = new();

    /// <summary>Gruppenfarbe (#RRGGBB) je Gruppenpfad; Untergruppen erben, wenn sie keine eigene haben.</summary>
    public Dictionary<string, string> GroupColors { get; set; } = new();

    /// <summary>Darstellung je Gruppe: „List“ oder „Grid“ (fehlt = wie das Panel).</summary>
    public Dictionary<string, string> GroupLayouts { get; set; } = new();
    /// <summary>Elementgröße je Gruppe (Small/Medium/Large); fehlt = wie das Panel.</summary>
    public Dictionary<string, string> GroupItemSizes { get; set; } = new();

    /// <summary>Farbe je Schlagwort (#RRGGBB).</summary>
    public Dictionary<string, string> TagColors { get; set; } = new();

    /// <summary>Stand des Datenmodells (13 = smarte Gruppen, globale Gruppen, ein Panel).</summary>
    public int DataVersion { get; set; }

    public List<CustomEntry> CustomEntries { get; set; } = new();

    /// <summary>Panels (ab 0.7). Das erste ist das Hauptpanel.</summary>
    public List<PanelConfig> Panels { get; set; } = new();

    /// <summary>Arbeitsbereiche; die Daten des aktiven stehen direkt in diesen Einstellungen.</summary>
    public List<Workspace> Workspaces { get; set; } = new();
    public string ActiveWorkspace { get; set; } = "Standard";

    /// <summary>Eigene Variablen ({{$Name}} in Texten, Links, Befehlen und Code).</summary>
    public List<UserVariable> Variables { get; set; } = new();
    /// <summary>Format von {{$Date}} (.NET-Format, z. B. dd.MM.yyyy).</summary>
    public string DateFormat { get; set; } = Config.Variables.DefaultDateFormat;
    /// <summary>Format von {{$Time}} (.NET-Format, z. B. HH:mm).</summary>
    public string TimeFormat { get; set; } = Config.Variables.DefaultTimeFormat;

    /// <summary>Aus dem Marktplatz installierte Elemente (Herkunft und Stand, für „Update verfügbar“).</summary>
    public List<MarketplaceInstall> MarketplaceInstalls { get; set; } = new();
    /// <summary>Zusätzliche Marktplatz-Quellen (Repository-URLs) neben der mitgelieferten.</summary>
    public List<MarketplaceSource> MarketplaceSources { get; set; } = new();

    /// <summary>Benannte Rechnerlisten für die Massenausführung („@Name“ in Profilen).</summary>
    public List<MachineGroup> MachineGroups { get; set; } = new();

    /// <summary>Eigene, gespeicherte Designs (erscheinen hinter den mitgelieferten Designvorlagen).</summary>
    public List<DesignPreset> CustomDesigns { get; set; } = new();

    /// <summary>Position der festen Gruppen unter den Gruppen der obersten Ebene (siehe <see cref="FixedGroups"/>).</summary>
    public Dictionary<string, int> FixedGroupPositions { get; set; } = new();

    /// <summary>Dynamische Rechnergruppen: beim Start und danach alle N Stunden aus dem AD aktualisieren.</summary>
    public int AdSyncIntervalHours { get; set; } = 4;

    /// <summary>Zuletzt verwendete Parameterwerte (Schlüssel: Pfad|Parameter).</summary>
    public Dictionary<string, List<string>> ParameterHistory { get; set; } = new();

    public EntryConfig? FindEntry(string fullPath) =>
        Entries.FirstOrDefault(e => string.Equals(e.Path, fullPath, StringComparison.OrdinalIgnoreCase));

    // Updates
    public bool CheckUpdatesOnStart { get; set; } = true;
    public bool IncludePrereleases { get; set; }

    public IReadOnlyList<string> ExtensionsFor(FolderConfig folder) =>
        folder.Extensions.Count > 0 ? folder.Extensions : DefaultExtensions;

    public string EffectiveLogFolder =>
        string.IsNullOrWhiteSpace(LogFolder)
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec", "Logs")
            : LogFolder;
}

/// <summary>Benannte Rechnerliste, z. B. „Druckserver“.</summary>
public sealed class MachineGroup
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Feste Liste bzw. bei dynamischen Gruppen das Ergebnis der letzten Synchronisierung (auch offline nutzbar).</summary>
    public List<string> Machines { get; set; } = new();

    /// <summary>Dynamische Gruppe: Regeln für das Active Directory; null = keine AD-Gruppe.</summary>
    public AdGroupQuery? AdQuery { get; set; }

    /// <summary>Dynamische Gruppe aus einer Liste (Text oder CSV mit Filtern); null = keine Listengruppe.</summary>
    public ListGroupQuery? ListQuery { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    /// <summary>Fehler der letzten Synchronisierung (die bisherige Liste bleibt dann erhalten).</summary>
    public string? LastSyncError { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDynamic => AdQuery != null || ListQuery != null;

    public MachineGroup Clone() => new()
    {
        Name = Name,
        Machines = new List<string>(Machines),
        AdQuery = AdQuery?.Clone(),
        ListQuery = ListQuery?.Clone(),
        LastSyncUtc = LastSyncUtc,
        LastSyncError = LastSyncError
    };
}
