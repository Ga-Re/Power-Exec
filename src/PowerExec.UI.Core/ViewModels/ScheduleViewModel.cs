using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Infrastructure;

namespace PowerExec.ViewModels;

/// <summary>Wochentag zum Ankreuzen.</summary>
public sealed class DayChoice : ObservableObject
{
    private bool _isChecked;
    public DayChoice(DayOfWeek day, string name, bool isChecked, Action changed) { Day = day; Name = name; _isChecked = isChecked; _changed = changed; }
    private readonly Action _changed;
    public DayOfWeek Day { get; }
    public string Name { get; }
    public bool IsChecked { get => _isChecked; set { if (Set(ref _isChecked, value)) _changed(); } }
}

/// <summary>Bestehender Zeitplan in der Liste des Dialogs.</summary>
public sealed record ScheduleRow(Schedule Schedule, string Text, string NextText, ICommand EditCommand, ICommand RemoveCommand);

/// <summary>Planen-Dialog (D4): einmal, täglich an Wochentagen oder im Intervall; Profil, Benachrichtigung, nachholen.</summary>
public sealed class ScheduleViewModel : ObservableObject
{
    private readonly IScheduleHost _app;
    private Schedule _editing;
    private string _onceDate, _onceTime, _dailyTime, _interval;
    private string? _error;

    public ScheduleViewModel(IScheduleHost app, LaunchItem item)
    {
        _app = app;
        Item = item;
        Title = app.Settings.FindEntry(item.FullPath)?.DisplayName is { Length: > 0 } dn ? dn : item.DisplayName;
        Profiles = ProfileResolver.GetProfiles(app.Settings, item).Select(p => p.Name).Distinct().ToList();
        _editing = NewSchedule();
        _onceDate = _onceTime = _dailyTime = _interval = string.Empty;
        LoadFields();
        SaveCommand = new RelayCommand(Save);
        NewCommand = new RelayCommand(() => { _editing = NewSchedule(); LoadFields(); });
        RefreshRows();
    }

    public LaunchItem Item { get; }
    public string Title { get; }
    public IReadOnlyList<string> Profiles { get; }
    public bool HasProfiles => Profiles.Count > 1;
    public ObservableCollection<DayChoice> Days { get; } = new();
    public ObservableCollection<ScheduleRow> Existing { get; } = new();
    public bool HasExisting => Existing.Count > 0;

    public ICommand SaveCommand { get; }
    public ICommand NewCommand { get; }
    public event Action? CloseRequested;

    private Schedule NewSchedule() => new()
    {
        Path = Item.FullPath, Name = Title, Kind = ScheduleKind.Daily, TimeOfDay = new TimeSpan(7, 0, 0),
        Days = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
        OnceAt = DateTime.Now.Date.AddDays(1).AddHours(7), IntervalMinutes = 60
    };

    private void LoadFields()
    {
        _onceDate = _editing.OnceAt.ToString("dd.MM.yyyy");
        _onceTime = _editing.OnceAt.ToString("HH:mm");
        _dailyTime = _editing.TimeOfDay.ToString(@"hh\:mm");
        _interval = _editing.IntervalMinutes.ToString(CultureInfo.InvariantCulture);
        Days.Clear();
        foreach (var (d, n) in new[] { (DayOfWeek.Monday, "Mo"), (DayOfWeek.Tuesday, "Di"), (DayOfWeek.Wednesday, "Mi"), (DayOfWeek.Thursday, "Do"), (DayOfWeek.Friday, "Fr"), (DayOfWeek.Saturday, "Sa"), (DayOfWeek.Sunday, "So") })
            Days.Add(new DayChoice(d, n, _editing.Days.Count == 0 || _editing.Days.Contains(d), UpdatePreview));
        foreach (var p in new[] { nameof(Kind), nameof(IsOnce), nameof(IsDaily), nameof(IsInterval), nameof(OnceDate), nameof(OnceTime), nameof(DailyTime), nameof(Interval),
                                  nameof(ProfileName), nameof(NotifyOnlyOnError), nameof(CatchUp), nameof(Enabled), nameof(SaveLabel) })
            OnPropertyChanged(p);
        UpdatePreview();
    }

    public string SaveLabel => _app.Settings.Schedules.Any(s => s.Id == _editing.Id) ? "Änderungen speichern" : "Planen";

    public ScheduleKind Kind
    {
        get => _editing.Kind;
        set { _editing.Kind = value; foreach (var p in new[] { nameof(Kind), nameof(IsOnce), nameof(IsDaily), nameof(IsInterval) }) OnPropertyChanged(p); UpdatePreview(); }
    }
    public bool IsOnce { get => Kind == ScheduleKind.Once; set { if (value) Kind = ScheduleKind.Once; } }
    public bool IsDaily { get => Kind == ScheduleKind.Daily; set { if (value) Kind = ScheduleKind.Daily; } }
    public bool IsInterval { get => Kind == ScheduleKind.Interval; set { if (value) Kind = ScheduleKind.Interval; } }

    public string OnceDate { get => _onceDate; set { _onceDate = value ?? string.Empty; UpdatePreview(); } }
    public string OnceTime { get => _onceTime; set { _onceTime = value ?? string.Empty; UpdatePreview(); } }
    public string DailyTime { get => _dailyTime; set { _dailyTime = value ?? string.Empty; UpdatePreview(); } }
    public string Interval { get => _interval; set { _interval = value ?? string.Empty; UpdatePreview(); } }
    public string ProfileName
    {
        get => _editing.ProfileName.Length > 0 && Profiles.Contains(_editing.ProfileName) ? _editing.ProfileName : Profiles.FirstOrDefault() ?? "Standard";
        set { _editing.ProfileName = value ?? string.Empty; OnPropertyChanged(); }
    }
    public bool NotifyOnlyOnError { get => _editing.NotifyOnlyOnError; set { _editing.NotifyOnlyOnError = value; OnPropertyChanged(); } }
    public bool CatchUp { get => _editing.CatchUp; set { _editing.CatchUp = value; OnPropertyChanged(); } }
    public bool Enabled { get => _editing.Enabled; set { _editing.Enabled = value; OnPropertyChanged(); } }

    private string _preview = string.Empty;
    public string Preview { get => _preview; private set => Set(ref _preview, value); }
    public string? Error { get => _error; private set => Set(ref _error, value); }

    /// <summary>Felder übernehmen; Fehlermeldung oder null.</summary>
    public string? Apply()
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        switch (_editing.Kind)
        {
            case ScheduleKind.Once:
                if (!DateTime.TryParseExact($"{_onceDate.Trim()} {_onceTime.Trim()}", new[] { "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm", "dd.MM.yy HH:mm" }, de, DateTimeStyles.None, out var at))
                    return "Datum als TT.MM.JJJJ und Uhrzeit als HH:MM eingeben.";
                if (at <= DateTime.Now) return "Der Zeitpunkt liegt in der Vergangenheit.";
                _editing.OnceAt = at;
                break;
            case ScheduleKind.Daily:
                if (!TimeSpan.TryParseExact(_dailyTime.Trim(), new[] { @"hh\:mm", @"h\:mm" }, de, out var t)) return "Uhrzeit als HH:MM eingeben, z. B. 07:00.";
                _editing.TimeOfDay = t;
                _editing.Days = Days.Where(d => d.IsChecked).Select(d => d.Day).ToList();
                if (_editing.Days.Count == 0) return "Mindestens einen Wochentag wählen.";
                if (_editing.Days.Count == 7) _editing.Days.Clear();
                break;
            default:
                if (!int.TryParse(_interval.Trim(), out var m) || m < Schedules.MinIntervalMinutes) return $"Intervall in Minuten, mindestens {Schedules.MinIntervalMinutes}.";
                _editing.IntervalMinutes = m;
                break;
        }
        return null;
    }

    private void UpdatePreview()
    {
        var probe = _editing.Clone();
        var old = _editing;
        _editing = probe;
        var error = Apply();
        _editing = old;
        Error = error;
        Preview = error != null ? string.Empty
            : Schedules.Next(probe, DateTime.Now) is { } next ? $"{Schedules.Describe(probe)} · nächster Start {next:ddd dd.MM. HH:mm}" : "kein weiterer Termin";
    }

    private void Save()
    {
        var error = Apply();
        Error = error;
        if (error != null) return;
        if (_editing.ProfileName.Length == 0 && Profiles.Count > 0 && ProfileName != Profiles[0]) _editing.ProfileName = ProfileName;
        _app.SaveSchedule(_editing);
        CloseRequested?.Invoke();
    }

    private void RefreshRows()
    {
        Existing.Clear();
        foreach (var s in Schedules.For(_app.Settings, Item.FullPath))
        {
            var copy = s;
            Existing.Add(new ScheduleRow(s, Schedules.Describe(s) + (s.Enabled ? string.Empty : " · pausiert"),
                s.NextRunLocal is { } n && s.Enabled ? $"nächster Start {n:ddd dd.MM. HH:mm}" : s.LastRunLocal is { } l ? $"zuletzt {l:dd.MM. HH:mm}" : string.Empty,
                new RelayCommand(() => { _editing = copy.Clone(); LoadFields(); }),
                new RelayCommand(() => { _app.RemoveSchedule(copy); RefreshRows(); })));
        }
        OnPropertyChanged(nameof(HasExisting));
    }
}
