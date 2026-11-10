using PowerExec.Core.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

public sealed class ResultRowViewModel : ObservableObject
{
    private RemoteResult _result;
    private bool _isExpanded;

    public ResultRowViewModel(string computer)
    {
        _result = new RemoteResult { Computer = computer, Status = RemoteStatus.Running };
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public RemoteResult Result => _result;
    public string Computer => _result.Computer;
    public RemoteStatus Status => _result.Status;
    public string StatusText => ResultExport.StatusText(_result.Status);
    public string Duration => _result.Status == RemoteStatus.Running ? "…" : $"{_result.DurationMs / 1000.0:0.0} s";
    public string Output => string.Join("\n", new[] { _result.Note, _result.Output }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string FirstLine
    {
        get
        {
            if (_result.Status == RemoteStatus.Running) return L.T("running …");
            var text = !string.IsNullOrWhiteSpace(_result.Error) ? _result.Error : _result.Output;
            var line = text.Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? string.Empty;
            return _result.Note.Length > 0 ? $"{line}  ({_result.Note})" : line;
        }
    }

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public ICommand ToggleCommand { get; }

    /// <summary>Sortierung der Kachelkarte: Probleme zuerst, dann laufende, dann erfolgreiche.</summary>
    public int StatusOrder => _result.Status switch
    {
        RemoteStatus.Error => 0,
        RemoteStatus.Unreachable => 1,
        RemoteStatus.Running => 2,
        _ => 3
    };

    /// <summary>Kurzinfo an der Kachel.</summary>
    public string TileToolTip => $"{Computer} · {StatusText}" + (FirstLine.Length > 0 && _result.Status != RemoteStatus.Running ? $"\n{FirstLine}" : string.Empty);

    private bool _isSelected;
    /// <summary>In der Kachelkarte ausgewählt (Details unten).</summary>
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public void Update(RemoteResult r)
    {
        _result = r;
        foreach (var p in new[] { nameof(Status), nameof(StatusText), nameof(Duration), nameof(Output), nameof(FirstLine), nameof(Result), nameof(StatusOrder), nameof(TileToolTip) })
            OnPropertyChanged(p);
    }
}

public sealed class ResultsViewModel : ObservableObject, IRemoteRunProgress
{
    private readonly IResultsHost _app;
    private readonly ExecutionRequest _request;
    private readonly string _account;
    private RemoteSession? _session;
    private string _filter = "all";
    private bool _isRunning;
    private bool _showLog;
    private string _logText = string.Empty;
    private string? _runnerError;
    private DateTime _started;

    public ResultsViewModel(IResultsHost app, ExecutionRequest request, string account)
    {
        _app = app;
        _request = request;
        _account = account;
        foreach (var t in request.Targets) Rows.Add(new ResultRowViewModel(t));
        _rowsView = new FilteredView<ResultRowViewModel>(Rows, MatchesFilter);
        // Kachelkarte: eigene Ansicht, Probleme zuerst
        _tilesView = new FilteredView<ResultRowViewModel>(Rows, MatchesFilter, rows => rows.OrderBy(r => r.StatusOrder));
        _showTiles = request.Targets.Count >= TilesThreshold;
        SelectTileCommand = new RelayCommand(p => SelectedTile = p as ResultRowViewModel);
        SetViewCommand = new RelayCommand(p => ShowTiles = p?.ToString() == "tiles");

        ToggleLogCommand = new RelayCommand(() => ShowLog = !ShowLog);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => !IsRunning && Rows.Count > 0);
        RetryFailedCommand = new RelayCommand(RetryFailed, () => !IsRunning && Rows.Any(r => r.Status is RemoteStatus.Error or RemoteStatus.Unreachable));
        CancelCommand = new RelayCommand(() => _session?.Cancel(), () => IsRunning);
        SetFilterCommand = new RelayCommand(p => Filter = p?.ToString() ?? "all");
        OpenLogFolderCommand = new RelayCommand(() => _app.Shell.OpenLocation(_app.Settings.EffectiveLogFolder, isFolder: true));
    }

    public ObservableCollection<ResultRowViewModel> Rows { get; } = new();
    private readonly FilteredView<ResultRowViewModel> _rowsView;
    private readonly FilteredView<ResultRowViewModel> _tilesView;

    /// <summary>Liste: Rechner, die zum Filter passen (in Startreihenfolge).</summary>
    public ObservableCollection<ResultRowViewModel> VisibleRows => _rowsView.Items;
    /// <summary>Kachelkarte: Rechner, die zum Filter passen – Probleme zuerst, dann laufende, dann erfolgreiche.</summary>
    public ObservableCollection<ResultRowViewModel> VisibleTiles => _tilesView.Items;

    /// <summary>Filter und Sortierung von Liste und Kachelkarte neu anwenden.</summary>
    internal void RefreshViews()
    {
        _rowsView.Refresh();
        _tilesView.Refresh();
    }

    /// <summary>Ab so vielen Rechnern startet die Ergebnisliste als Kachelkarte.</summary>
    public const int TilesThreshold = 16;

    private bool _showTiles;
    /// <summary>Kachelkarte (eine Kachel je Rechner) statt Liste.</summary>
    public bool ShowTiles { get => _showTiles; set { if (Set(ref _showTiles, value)) OnPropertyChanged(nameof(ShowList)); } }
    public bool ShowList => !_showTiles;
    public ICommand SetViewCommand { get; }

    private ResultRowViewModel? _selectedTile;
    /// <summary>Ausgewählte Kachel: Ausgabe erscheint unter der Karte.</summary>
    public ResultRowViewModel? SelectedTile
    {
        get => _selectedTile;
        set
        {
            if (_selectedTile != null) _selectedTile.IsSelected = false;
            if (ReferenceEquals(_selectedTile, value)) value = null; // zweiter Klick hebt die Auswahl auf
            if (Set(ref _selectedTile, value) && value != null) value.IsSelected = true;
            OnPropertyChanged(nameof(HasSelectedTile));
        }
    }
    public bool HasSelectedTile => _selectedTile != null;
    public ICommand SelectTileCommand { get; }

    public string Title => _request.Item.DisplayName;
    public string Meta => L.F("Launched {0:t} · {1} computers · as {2} · ", _started, Rows.Count, _account) +
                          (_request.Engine == PsEngine.PowerShell7 ? L.T("PowerShell 7 (if available)") : "Windows PowerShell 5.1") +
                          (_request.CopyFolder ? L.T(" · with script folder") : string.Empty);

    public int Total => Rows.Count;
    public int SuccessCount => Rows.Count(r => r.Status == RemoteStatus.Success);
    public int ErrorCount => Rows.Count(r => r.Status == RemoteStatus.Error);
    public int UnreachableCount => Rows.Count(r => r.Status == RemoteStatus.Unreachable);
    public int DoneCount => Rows.Count(r => r.Status != RemoteStatus.Running);
    public double Progress => Total == 0 ? 0 : (double)DoneCount / Total;

    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value)) RefreshViews(); }
    }

    public bool IsRunning { get => _isRunning; private set { if (Set(ref _isRunning, value)) OnPropertyChanged(nameof(StatusLine)); } }
    public bool ShowLog { get => _showLog; set { if (Set(ref _showLog, value)) OnPropertyChanged(nameof(LogLabel)); } }
    public string LogLabel => ShowLog ? L.T("Hide log") : L.T("Show log");
    public string LogText { get => _logText; private set => Set(ref _logText, value); }
    public string? RunnerError { get => _runnerError; private set => Set(ref _runnerError, value); }
    public string? LogFile { get; private set; }

    public string StatusLine => IsRunning
        ? L.F("Running … {0} of {1} done", DoneCount, Total)
        : L.F("Done · {0} succeeded · {1} errors · {2} unreachable", SuccessCount, ErrorCount, UnreachableCount);

    public ICommand ToggleLogCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand RetryFailedCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SetFilterCommand { get; }
    public ICommand OpenLogFolderCommand { get; }

    public void Start(RunCredential? credential)
    {
        _started = DateTime.Now;
        OnPropertyChanged(nameof(Meta));
        IsRunning = true;
        RunnerError = null;
        _session = new RemoteSession(_app.UiDispatcher);
        _session.ResultReceived += OnResult;
        _session.Completed += OnCompleted;
        try
        {
            _session.Start(_request, _app.Settings, credential);
        }
        catch (Exception ex)
        {
            OnCompleted(L.T("The remote runner couldn't be started: ") + ex.Message);
        }
        finally
        {
            credential?.Dispose();
        }
    }

    private void OnResult(RemoteResult r)
    {
        var row = Rows.FirstOrDefault(x => x.Computer.Equals(r.Computer, StringComparison.OrdinalIgnoreCase) && x.Status == RemoteStatus.Running)
                  ?? Rows.FirstOrDefault(x => x.Computer.Equals(r.Computer, StringComparison.OrdinalIgnoreCase));
        if (row == null)
        {
            row = new ResultRowViewModel(r.Computer);
            Rows.Add(row);
        }
        row.Update(r);
        if (Rows.Count == 1) row.IsExpanded = true; // Bei einem Rechner direkt die Ausgabe zeigen
        RaiseCounts();
        RefreshViews();
    }

    /// <summary>Wird ausgelöst, wenn alle Rechner fertig sind (oder der Lauf abgebrochen wurde).</summary>
    public event Action? Finished;

    void IRemoteRunProgress.Cancel() => CancelCommand.Execute(null);

    private void OnCompleted(string? error)
    {
        foreach (var row in Rows.Where(r => r.Status == RemoteStatus.Running))
            row.Update(new RemoteResult { Computer = row.Computer, Status = RemoteStatus.Error, Error = error != null ? L.T("Canceled") : L.T("No response") });
        RunnerError = error;
        IsRunning = false;
        RaiseCounts();
        WriteLog();
        AppLog.Info(L.F("Remote run finished: {0} · {1}", _request.Item.FileName, StatusLine) + (error != null ? " · Runner: " + error : string.Empty));
        Finished?.Invoke();
        _session?.Dispose();
        _session = null;
        CommandRequery.Invalidate();
    }

    private void RaiseCounts()
    {
        foreach (var p in new[] { nameof(SuccessCount), nameof(ErrorCount), nameof(UnreachableCount), nameof(DoneCount), nameof(Progress), nameof(StatusLine), nameof(Total) })
            OnPropertyChanged(p);
    }

    private bool MatchesFilter(ResultRowViewModel r) => _filter switch
    {
        "ok" => r.Status == RemoteStatus.Success,
        "err" => r.Status == RemoteStatus.Error,
        "off" => r.Status == RemoteStatus.Unreachable,
        _ => true
    };

    private void WriteLog()
    {
        var engine = _request.Engine == PsEngine.PowerShell7 ? L.T("PowerShell 7 (fallback 5.1)") : "Windows PowerShell 5.1";
        LogText = ResultExport.ToLog(_request.EffectivePath, _account, engine, _started, Rows.Select(r => r.Result));
        if (RunnerError != null) LogText += Environment.NewLine + L.T("Runner error: ") + RunnerError + Environment.NewLine;
        try
        {
            var dir = _app.Settings.EffectiveLogFolder;
            Directory.CreateDirectory(dir);
            var name = $"{_started:yyyy-MM-dd_HHmmss}_{Path.GetFileNameWithoutExtension(_request.Item.FileName)}.log";
            LogFile = Path.Combine(dir, name);
            File.WriteAllText(LogFile, LogText, Encoding.UTF8);
        }
        catch
        {
            LogFile = null;
        }
    }

    private void ExportCsv()
    {
        var file = _app.Dialogs.SaveFile(L.T("CSV file (*.csv)|*.csv"),
            $"{Path.GetFileNameWithoutExtension(_request.Item.FileName)}_{_started:yyyy-MM-dd_HHmm}.csv");
        if (file == null) return;
        try
        {
            File.WriteAllText(file, ResultExport.ToCsv(Rows.Select(r => r.Result)), new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            _app.Dialogs.Warn(ex.Message, L.T("CSV export"));
        }
    }

    private void RetryFailed()
    {
        var failed = Rows.Where(r => r.Status is RemoteStatus.Error or RemoteStatus.Unreachable).Select(r => r.Computer).ToList();
        if (failed.Count == 0) return;
        var retry = new ExecutionRequest
        {
            Item = _request.Item,
            Mode = _request.Mode,
            Engine = _request.Engine,
            NoExit = _request.NoExit,
            NoProfile = _request.NoProfile,
            ExecutionPolicy = _request.ExecutionPolicy,
            Parameters = _request.Parameters,
            Targets = failed,
            CopyFolder = _request.CopyFolder,
            ResolvedPath = _request.ResolvedPath
        };
        _app.RetryRemote(retry);
    }
}
