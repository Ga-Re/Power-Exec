using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

/// <summary>
/// Einführung beim ersten Start in vier Schritten: Scriptordner, Bildschirmrand, Tastenkürzel/Autostart, Fertig.
/// Übernommen wird erst mit „Fertig“; „Überspringen“ ändert nichts.
/// </summary>
public sealed class WelcomeViewModel : ObservableObject
{
    public const int StepCount = 4;
    private readonly IWelcomeHost _app;
    private int _step;
    private string? _folder;
    private bool _createSample;
    private ScreenEdge _edge;
    private bool _startWithWindows;
    private bool _useWindowsAccent;
    private string _hotkey;

    public WelcomeViewModel(IWelcomeHost app)
    {
        _app = app;
        var panel = app.Settings.Panels.FirstOrDefault();
        _edge = panel?.Edge ?? ScreenEdge.Right;
        _hotkey = panel is { Hotkey.Length: > 0 } ? panel.Hotkey : PanelConfig.DefaultHotkey;
        _startWithWindows = app.Settings.StartWithWindows;
        _useWindowsAccent = app.Settings.UseWindowsAccent;
        _createSample = app.Settings.Folders.Count == 0;
        NextCommand = new RelayCommand(() => { if (IsLast) Finish(); else Step++; });
        BackCommand = new RelayCommand(() => Step--, () => _step > 0);
        SkipCommand = new RelayCommand(Skip);
        ChooseFolderCommand = new RelayCommand(ChooseFolder);
        SetEdgeCommand = new RelayCommand(p => { if (Enum.TryParse<ScreenEdge>(p?.ToString(), out var e)) Edge = e; });
        ResetHotkeyCommand = new RelayCommand(() => Hotkey = PanelConfig.DefaultHotkey);
    }

    /// <summary>Schritt 0–3.</summary>
    public int Step
    {
        get => _step;
        set
        {
            if (!Set(ref _step, Math.Clamp(value, 0, StepCount - 1))) return;
            foreach (var p in new[] { nameof(IsLast), nameof(NextLabel), nameof(StepText), nameof(IsStep0), nameof(IsStep1), nameof(IsStep2), nameof(IsStep3) })
                OnPropertyChanged(p);
            CommandRequery.Invalidate();
        }
    }

    public bool IsStep0 => _step == 0;
    public bool IsStep1 => _step == 1;
    public bool IsStep2 => _step == 2;
    public bool IsStep3 => _step == 3;
    public bool IsLast => _step == StepCount - 1;
    public string NextLabel => IsLast ? "Fertig" : "Weiter";
    public string StepText => $"Schritt {_step + 1} von {StepCount}";

    // Schritt 1: Scripte
    public string? Folder { get => _folder; private set { if (Set(ref _folder, value)) OnPropertyChanged(nameof(HasFolder)); } }
    public bool HasFolder => _folder != null;
    public bool HasExistingFolders => _app.Settings.Folders.Count > 0;
    public string ExistingFoldersText => $"Bereits eingerichtet: {string.Join(", ", _app.Settings.Folders.Select(f => f.Path))}";
    /// <summary>Beispielordner mit einem Script anlegen (Dokumente\Power-Exec\Scripte).</summary>
    public bool CreateSample { get => _createSample; set => Set(ref _createSample, value); }
    public static string SampleFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Power-Exec", "Scripte");

    // Schritt 2: Rand
    public ScreenEdge Edge
    {
        get => _edge;
        set { if (Set(ref _edge, value)) OnPropertyChanged(nameof(EdgeText)); }
    }
    public string EdgeText => _edge switch
    {
        ScreenEdge.Left => "Links: Maus an den linken Bildschirmrand",
        ScreenEdge.Top => "Oben: Maus an den oberen Bildschirmrand",
        ScreenEdge.Bottom => "Unten: Maus an den unteren Bildschirmrand",
        _ => "Rechts: Maus an den rechten Bildschirmrand"
    };

    // Schritt 3: Tastenkürzel und Start
    /// <summary>Tastenkürzel des ersten Panels (Vorgabe Strg+Alt+Leertaste); leer = keins.</summary>
    public string Hotkey
    {
        get => _hotkey;
        set { if (Set(ref _hotkey, value ?? string.Empty)) OnPropertyChanged(nameof(HotkeyText)); }
    }

    public string HotkeyText => _hotkey.Length > 0 ? DisplayText.Hotkey(_hotkey) : "keins – Panel nur über die Hover-Zone";

    /// <summary>Zurück auf Strg+Alt+Leertaste.</summary>
    public ICommand ResetHotkeyCommand { get; }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public bool UseWindowsAccent { get => _useWindowsAccent; set => Set(ref _useWindowsAccent, value); }

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SkipCommand { get; }
    public ICommand ChooseFolderCommand { get; }
    public ICommand SetEdgeCommand { get; }

    /// <summary>Fenster schließen; true = Panel zur Probe öffnen.</summary>
    public event Action<bool>? CloseRequested;

    private void ChooseFolder()
    {
        if (_app.PickFolder("Ordner mit Scripten und Programmen auswählen") is { } folder)
        {
            Folder = folder;
            CreateSample = false;
        }
    }

    private void Skip()
    {
        var s = SettingsStore.Clone(_app.Settings);
        s.WelcomeDone = true;
        _app.ApplySettings(s);
        CloseRequested?.Invoke(false);
    }

    /// <summary>Gewählte Einstellungen übernehmen (über die normale Übernahme, damit Panels und Ordner neu aufgebaut werden).</summary>
    public void Finish()
    {
        var s = SettingsStore.Clone(_app.Settings);
        s.WelcomeDone = true;
        s.StartWithWindows = _startWithWindows;
        s.UseWindowsAccent = _useWindowsAccent;
        if (s.Panels.Count == 0) s.Panels.Add(new PanelConfig());
        foreach (var p in s.Panels) p.Edge = _edge;
        s.Panels[0].Hotkey = _hotkey;
        var folder = _folder;
        if (folder == null && _createSample) folder = CreateSampleFolder();
        if (folder != null && !s.Folders.Any(f => f.Path.Equals(folder, StringComparison.OrdinalIgnoreCase)))
            SmartGroups.Add(s, folder, null, null);
        _app.ApplySettings(s);
        AppLog.Info($"Einführung abgeschlossen: Rand {_edge}, Ordner {folder ?? "–"}");
        CloseRequested?.Invoke(true);
    }

    /// <summary>Beispielordner mit dem mitgelieferten Script anlegen; null bei Fehler.</summary>
    public static string? CreateSampleFolder()
    {
        try
        {
            Directory.CreateDirectory(SampleFolder);
            var target = Path.Combine(SampleFolder, "Get-DiskSpace.ps1");
            if (!File.Exists(target))
            {
                using var stream = typeof(WelcomeViewModel).Assembly.GetManifestResourceStream("PowerExec.Sample.Get-DiskSpace.ps1");
                if (stream == null) return null;
                using var file = File.Create(target);
                stream.CopyTo(file);
            }
            return SampleFolder;
        }
        catch (Exception ex)
        {
            AppLog.Error("Beispielordner konnte nicht angelegt werden", ex);
            return null;
        }
    }
}
