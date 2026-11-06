using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using PowerExec.Core.Models;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

public enum ActivityState { Running, Success, Warning, Error }

/// <summary>Ein laufender oder gerade beendeter Start (Aktivitäten-Leiste im Panel).</summary>
public sealed class ActivityViewModel : ObservableObject
{
    private ActivityState _state = ActivityState.Running;
    private string _detail;
    private double? _progress;
    private DateTime? _finished;

    public ActivityViewModel(string title, string detail, string badge, Action? open = null, Action? cancel = null)
    {
        Title = title;
        _detail = detail;
        Badge = badge;
        Started = DateTime.Now;
        OpenAction = open;
        CancelAction = cancel;
        OpenCommand = new RelayCommand(() => OpenAction?.Invoke(), () => OpenAction != null);
        CancelCommand = new RelayCommand(() => CancelAction?.Invoke(), () => CancelAction != null && IsRunning);
        RestartCommand = new RelayCommand(() => RestartAction?.Invoke(), () => RestartAction != null && !IsRunning);
    }

    /// <summary>Gleichen Start wiederholen (gleiche Werte, gleiches Profil); nur nach dem Ende.</summary>
    public Action? RestartAction { get; set; }
    public ICommand RestartCommand { get; }
    public bool CanRestart => RestartAction != null && !IsRunning;

    /// <summary>Script ist fertig, sein Fenster aber noch offen.</summary>
    public bool WindowStillOpen { get; internal set; }

    public string Title { get; }
    /// <summary>Typ-Kürzel wie im Panel (PS, CMD …).</summary>
    public string Badge { get; }
    public DateTime Started { get; }
    public DateTime? Finished => _finished;

    public Action? OpenAction { get; set; }
    public Action? CancelAction { get; set; }
    public ICommand OpenCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand? DismissCommand { get; internal set; }

    public ActivityState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value)) return;
            foreach (var p in new[] { nameof(IsRunning), nameof(IsSuccess), nameof(IsWarning), nameof(IsError), nameof(StatusGlyph), nameof(TimeText), nameof(CanCancel), nameof(CanRestart) })
                OnPropertyChanged(p);
            CommandRequery.Invalidate();
        }
    }

    public bool IsRunning => _state == ActivityState.Running;
    public bool IsSuccess => _state == ActivityState.Success;
    public bool IsWarning => _state == ActivityState.Warning;
    public bool IsError => _state == ActivityState.Error;
    public bool CanCancel => IsRunning && CancelAction != null;
    public bool CanOpen => OpenAction != null;

    /// <summary>Segoe-Fluent-Symbol für den Status (Häkchen, Warnung, Fehler).</summary>
    public string StatusGlyph => _state switch
    {
        ActivityState.Success => "",
        ActivityState.Warning => "",
        ActivityState.Error => "",
        _ => ""
    };

    /// <summary>Zweite Zeile: Ziel, Fortschritt, Ergebnis.</summary>
    public string Detail { get => _detail; set => Set(ref _detail, value); }

    /// <summary>Fortschritt 0…1; null = unbestimmt (lokaler Prozess).</summary>
    public double? Progress
    {
        get => _progress;
        set { if (Set(ref _progress, value)) { OnPropertyChanged(nameof(HasProgress)); OnPropertyChanged(nameof(ProgressValue)); } }
    }
    public bool HasProgress => _progress != null && IsRunning;
    public double ProgressValue => (_progress ?? 0) * 100;

    /// <summary>„läuft seit 0:42“ bzw. „fertig 09:41 · 1,2 s“.</summary>
    public string TimeText
    {
        get
        {
            if (IsRunning) return "seit " + Format(DateTime.Now - Started);
            var end = _finished ?? DateTime.Now;
            return $"{end:HH:mm} · {Format(end - Started)}";
        }
    }

    internal static string Format(TimeSpan t) => DisplayText.Duration(t);

    internal void Tick() => OnPropertyChanged(nameof(TimeText));

    /// <summary>Gestarteter Eintrag (für die Anzeige „läuft“ in der Liste).</summary>
    public string? ItemPath { get; init; }

    /// <summary>Exit-Code eines lokalen Prozesses (falls lesbar).</summary>
    public int? ExitCode { get; internal set; }

    /// <summary>Wird einmal ausgelöst, wenn der Start beendet ist.</summary>
    public event Action? Completed;

    public void Complete(ActivityState state, string? detail = null)
    {
        if (!IsRunning) return;
        _finished = DateTime.Now;
        if (detail != null) Detail = detail;
        State = state;
        OnPropertyChanged(nameof(HasProgress));
        Completed?.Invoke();
    }
}

/// <summary>
/// Laufende und gerade beendete Starts für alle Panels. Beendete Einträge bleiben eine Weile stehen
/// (höchstens <see cref="MaxFinished"/>), damit man Ergebnis und Ausgabe noch erreicht.
/// </summary>
public sealed class ActivityList
{
    public const int MaxFinished = 3;
    public static readonly TimeSpan KeepFinished = TimeSpan.FromMinutes(15);

    private readonly IUiTimer _timer;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<Process, string?> _doneFileOf;

    /// <param name="doneFileOf">Fertig-Markierung eines gestarteten Scripts (Fenster bleibt offen), sonst null.</param>
    public ActivityList(IUiDispatcher dispatcher, Func<Process, string?>? doneFileOf = null)
    {
        _dispatcher = dispatcher;
        _doneFileOf = doneFileOf ?? (_ => null);
        _timer = dispatcher.CreateTimer(TimeSpan.FromSeconds(1), Tick);
    }

    public ObservableCollection<ActivityViewModel> Items { get; } = new();
    public bool HasRunning => Items.Any(a => a.IsRunning);
    public int RunningCount => Items.Count(a => a.IsRunning);

    /// <summary>Laufend ↔ nichts mehr laufend (für die Hover-Zone und die Panels).</summary>
    public event Action? Changed;

    public ActivityViewModel Add(ActivityViewModel activity)
    {
        activity.DismissCommand = new RelayCommand(() => Remove(activity), () => !activity.IsRunning);
        Items.Insert(0, activity);
        activity.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActivityViewModel.State)) { Trim(); Changed?.Invoke(); }
        };
        _timer.Start();
        Changed?.Invoke();
        return activity;
    }

    public void Remove(ActivityViewModel activity)
    {
        if (Items.Remove(activity)) Changed?.Invoke();
    }

    private void Tick()
    {
        if (_doneFiles.Count > 0) CheckDoneFiles();
        foreach (var a in Items.Where(a => a.IsRunning)) a.Tick();
        var before = Items.Count;
        Trim();
        if (Items.Count != before) Changed?.Invoke();
        if (!HasRunning && Items.Count == 0) _timer.Stop();
    }

    private void Trim()
    {
        var old = Items.Where(a => !a.IsRunning && a.Finished is { } f && DateTime.Now - f > KeepFinished).ToList();
        foreach (var a in old) Items.Remove(a);
        foreach (var a in Items.Where(a => !a.IsRunning).Skip(MaxFinished).ToList()) Items.Remove(a);
    }

    /// <summary>
    /// Lokal gestarteten Prozess verfolgen (Scripte): Ende und Exit-Code. Programme mit Fenster werden nicht verfolgt,
    /// sie würden dauerhaft als „läuft“ erscheinen.
    /// </summary>
    public void TrackProcess(LaunchItem item, string displayName, Process process, string detail, Action<ActivityViewModel>? created = null, Action? restart = null)
    {
        var activity = Add(new ActivityViewModel(displayName, detail, BadgeOf(item)) { RestartAction = restart, ItemPath = item.FullPath });
        created?.Invoke(activity);
        if (_doneFileOf(process) is { } doneFile) _doneFiles[activity] = doneFile;
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => _dispatcher.Post(() => Finish(activity, process));
            if (process.HasExited) _dispatcher.Post(() => Finish(activity, process));
        }
        catch (Exception ex)
        {
            // Kein Zugriff auf den Prozess (z. B. anderes Konto): nicht weiter verfolgen
            AppLog.Info($"Prozess wird nicht verfolgt: {ex.Message}");
            activity.Complete(ActivityState.Success, detail + " · gestartet");
            process.Dispose();
        }
    }

    /// <summary>Fertig-Markierungen laufender Scripte (Fenster bleibt offen) – im Sekundentakt geprüft.</summary>
    private readonly Dictionary<ActivityViewModel, string> _doneFiles = new();

    private void CheckDoneFiles()
    {
        foreach (var (activity, file) in _doneFiles.ToList())
        {
            if (!activity.IsRunning) { Forget(activity, file); continue; }
            string text;
            try
            {
                if (!File.Exists(file)) continue;
                text = File.ReadAllText(file).Trim();
            }
            catch (IOException) { continue; } // wird gerade geschrieben
            catch (UnauthorizedAccessException) { continue; }
            Forget(activity, file);
            activity.WindowStillOpen = true;
            // „E“: Batch mit Fehler (Exit-Code dort nicht sicher ermittelbar)
            if (text.Equals("E", StringComparison.OrdinalIgnoreCase))
            {
                activity.Complete(ActivityState.Error, "fertig mit Fehler · Fenster noch offen");
                continue;
            }
            var code = int.TryParse(text, out var c) ? c : 0;
            activity.ExitCode = code;
            if (code == 0) activity.Complete(ActivityState.Success, "fertig · Fenster noch offen");
            else activity.Complete(ActivityState.Error, $"fertig · Exit-Code {code} · Fenster noch offen");
        }
    }

    private void Forget(ActivityViewModel activity, string file)
    {
        _doneFiles.Remove(activity);
        try { File.Delete(file); } catch { /* später beim nächsten Start egal */ }
    }

    private void Finish(ActivityViewModel activity, Process process)
    {
        if (_doneFiles.TryGetValue(activity, out var file))
        {
            // Fenster geschlossen, bevor die Markierung gelesen wurde: zuerst die Markierung auswerten
            CheckDoneFiles();
            if (activity.IsRunning) Forget(activity, file);
        }
        if (!activity.IsRunning) { process.Dispose(); return; }
        int? code = null;
        try { code = process.ExitCode; } catch { /* erhöhter Prozess: Exit-Code nicht lesbar */ }
        process.Dispose();
        activity.ExitCode = code;
        // 0xC000013A: Konsolenfenster geschlossen (Strg+C / ✕) – abgebrochen, kein Script-Fehler
        if (code == unchecked((int)0xC000013A)) activity.Complete(ActivityState.Warning, "Fenster geschlossen");
        else if (code is null or 0) activity.Complete(ActivityState.Success, code == null ? "beendet" : "beendet · Exit-Code 0");
        else activity.Complete(ActivityState.Error, $"beendet · Exit-Code {code}");
    }

    /// <summary>Remote-/Massenlauf aus der Ergebnisliste anzeigen.</summary>
    public ActivityViewModel TrackRemote(IRemoteRunProgress results, Action open)
    {
        var activity = Add(new ActivityViewModel(results.Title, RemoteDetail(results), "PS", open, results.Cancel));
        activity.Progress = 0;
        results.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IRemoteRunProgress.DoneCount) or nameof(IRemoteRunProgress.Progress))
            {
                activity.Progress = results.Progress;
                activity.Detail = RemoteDetail(results);
            }
        };
        results.Finished += () =>
        {
            var problems = results.ErrorCount + results.UnreachableCount;
            activity.Complete(results.RunnerError != null ? ActivityState.Error : problems > 0 ? ActivityState.Warning : ActivityState.Success,
                results.RunnerError ?? RemoteDetail(results));
        };
        return activity;
    }

    private static string RemoteDetail(IRemoteRunProgress r)
    {
        var parts = new List<string> { r.IsRunning ? $"{r.DoneCount} / {r.Total} Rechner" : $"{r.Total} Rechner" };
        if (r.SuccessCount > 0) parts.Add($"{r.SuccessCount} ✓");
        if (r.ErrorCount > 0) parts.Add($"{r.ErrorCount} Fehler");
        if (r.UnreachableCount > 0) parts.Add($"{r.UnreachableCount} nicht erreichbar");
        return string.Join(" · ", parts);
    }

    public static string BadgeOf(LaunchItem item) => item.Type switch
    {
        ItemType.PowerShell => "PS",
        ItemType.Batch => "CMD",
        ItemType.Executable => "EXE",
        _ => "…"
    };

    /// <summary>Lokale Starts, die verfolgt werden: Scripte und Snippets (keine Programme mit eigenem Fenster).</summary>
    public static bool ShouldTrack(LaunchItem item) =>
        item.Type is ItemType.PowerShell or ItemType.Batch;
}
