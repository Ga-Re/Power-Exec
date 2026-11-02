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
        NewFiles = NewFiles
    };
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

    /// <summary>Gruppen im Panel: nach Ordner oder nach eigenen Kategorien.</summary>
    public GroupingMode Grouping { get; set; } = GroupingMode.Folder;

    /// <summary>Reihenfolge der Kategorien im Panel.</summary>
    public List<string> CategoryOrder { get; set; } = new();

    /// <summary>Anzahl der Einträge in „Zuletzt ausgeführt“ (0 = Gruppe ausblenden).</summary>
    public int RecentRunsCount { get; set; } = 5;

    // Panel-Zustand
    public List<string> Favorites { get; set; } = new();
    public List<string> CollapsedGroups { get; set; } = new();

    /// <summary>Zuletzt ausgeführte Einträge (neueste zuerst).</summary>
    public List<RecentRun> RecentRuns { get; set; } = new();

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
