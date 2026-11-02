using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
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
            if (_result.Status == RemoteStatus.Running) return "läuft …";
            var text = !string.IsNullOrWhiteSpace(_result.Error) ? _result.Error : _result.Output;
            var line = text.Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? string.Empty;
            return _result.Note.Length > 0 ? $"{line}  ({_result.Note})" : line;
        }
    }

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public ICommand ToggleCommand { get; }

    public void Update(RemoteResult r)
    {
        _result = r;
        foreach (var p in new[] { nameof(Status), nameof(StatusText), nameof(Duration), nameof(Output), nameof(FirstLine), nameof(Result) })
            OnPropertyChanged(p);
    }
}

public sealed class ResultsViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly ExecutionRequest _request;
    private readonly string _account;
    private RemoteSession? _session;
    private string _filter = "all";
    private bool _isRunning;
    private bool _showLog;
    private string _logText = string.Empty;
    private string? _runnerError;
    private DateTime _started;

    public ResultsViewModel(AppController app, ExecutionRequest request, string account)
    {
        _app = app;
        _request = request;
        _account = account;
        foreach (var t in request.Targets) Rows.Add(new ResultRowViewModel(t));
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => o is ResultRowViewModel r && MatchesFilter(r);

        ToggleLogCommand = new RelayCommand(() => ShowLog = !ShowLog);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => !IsRunning && Rows.Count > 0);
        RetryFailedCommand = new RelayCommand(RetryFailed, () => !IsRunning && Rows.Any(r => r.Status is RemoteStatus.Error or RemoteStatus.Unreachable));
        CancelCommand = new RelayCommand(() => _session?.Cancel(), () => IsRunning);
        SetFilterCommand = new RelayCommand(p => Filter = p?.ToString() ?? "all");
        OpenLogFolderCommand = new RelayCommand(() => Launcher.OpenLocation(_app.Settings.EffectiveLogFolder, isFolder: true));
    }

    public ObservableCollection<ResultRowViewModel> Rows { get; } = new();
    public ICollectionView RowsView { get; }

    public string Title => _request.Item.DisplayName;
    public string Meta => $"Gestartet {_started:HH:mm} · {Rows.Count} {(Rows.Count == 1 ? "Rechner" : "Rechner")} · als {_account} · " +
                          (_request.Engine == PsEngine.PowerShell7 ? "PowerShell 7 (wenn verfügbar)" : "Windows PowerShell 5.1") +
                          (_request.CopyFolder ? " · mit Scriptordner" : string.Empty);

    public int Total => Rows.Count;
    public int SuccessCount => Rows.Count(r => r.Status == RemoteStatus.Success);
    public int ErrorCount => Rows.Count(r => r.Status == RemoteStatus.Error);
    public int UnreachableCount => Rows.Count(r => r.Status == RemoteStatus.Unreachable);
    public int DoneCount => Rows.Count(r => r.Status != RemoteStatus.Running);
    public double Progress => Total == 0 ? 0 : (double)DoneCount / Total;

    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value)) RowsView.Refresh(); }
    }

    public bool IsRunning { get => _isRunning; private set { if (Set(ref _isRunning, value)) OnPropertyChanged(nameof(StatusLine)); } }
    public bool ShowLog { get => _showLog; set { if (Set(ref _showLog, value)) OnPropertyChanged(nameof(LogLabel)); } }
    public string LogLabel => ShowLog ? "Log ausblenden" : "Log anzeigen";
    public string LogText { get => _logText; private set => Set(ref _logText, value); }
    public string? RunnerError { get => _runnerError; private set => Set(ref _runnerError, value); }
    public string? LogFile { get; private set; }

    public string StatusLine => IsRunning
        ? $"Läuft … {DoneCount} von {Total} fertig"
        : $"Fertig · {SuccessCount} erfolgreich · {ErrorCount} Fehler · {UnreachableCount} nicht erreichbar";

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
        _session = new RemoteSession(Application.Current.Dispatcher);
        _session.ResultReceived += OnResult;
        _session.Completed += OnCompleted;
        try
        {
            _session.Start(_request, _app.Settings, credential);
        }
        catch (Exception ex)
        {
            OnCompleted("Der Remote-Runner konnte nicht gestartet werden: " + ex.Message);
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
        RowsView.Refresh();
    }

    /// <summary>Wird ausgelöst, wenn alle Rechner fertig sind (oder der Lauf abgebrochen wurde).</summary>
    public event Action? Finished;

    private void OnCompleted(string? error)
    {
        foreach (var row in Rows.Where(r => r.Status == RemoteStatus.Running))
            row.Update(new RemoteResult { Computer = row.Computer, Status = RemoteStatus.Error, Error = error != null ? "Abgebrochen" : "Keine Rückmeldung" });
        RunnerError = error;
        IsRunning = false;
        RaiseCounts();
        WriteLog();
        AppLog.Info($"Remote-Lauf beendet: {_request.Item.FileName} · {StatusLine}" + (error != null ? " · Runner: " + error : string.Empty));
        Finished?.Invoke();
        _session?.Dispose();
        _session = null;
        CommandManager.InvalidateRequerySuggested();
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
        var engine = _request.Engine == PsEngine.PowerShell7 ? "PowerShell 7 (Fallback 5.1)" : "Windows PowerShell 5.1";
        LogText = ResultExport.ToLog(_request.EffectivePath, _account, engine, _started, Rows.Select(r => r.Result));
        if (RunnerError != null) LogText += Environment.NewLine + "Runner-Fehler: " + RunnerError + Environment.NewLine;
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
        var dlg = new SaveFileDialog
        {
            Filter = "CSV-Datei (*.csv)|*.csv",
            FileName = $"{Path.GetFileNameWithoutExtension(_request.Item.FileName)}_{_started:yyyy-MM-dd_HHmm}.csv"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, ResultExport.ToCsv(Rows.Select(r => r.Result)), new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CSV-Export", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        _app.StartRemote(retry, null);
    }
}
