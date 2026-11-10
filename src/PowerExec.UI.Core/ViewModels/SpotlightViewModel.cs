using PowerExec.Core.Localization;
using System.Windows.Input;
using PowerExec.Core.Execution;
using PowerExec.Core.Search;
using PowerExec.Infrastructure;
using PowerExec.Services;

namespace PowerExec.ViewModels;

/// <summary>
/// Spotlight-Modus (A1): schwebende Suche in der Bildschirmmitte mit den besten Treffern des Panels.
/// Gleiche Suche, gleiche Filter (typ:, #schlagwort …), gleiche Startwege.
/// </summary>
public sealed class SpotlightViewModel : ObservableObject
{
    public const int MaxResults = 8;
    private readonly IPanelHost _app;
    private readonly IPanelContext _host;
    private string _searchText = string.Empty;
    private IReadOnlyList<ItemViewModel> _results = Array.Empty<ItemViewModel>();
    private int _selectedIndex;

    public SpotlightViewModel(IPanelHost app, IPanelContext host)
    {
        _app = app;
        _host = host;
        Refresh();
    }

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value ?? string.Empty)) Refresh(); }
    }

    public IReadOnlyList<ItemViewModel> Results { get => _results; private set { if (Set(ref _results, value)) { OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(Heading)); } } }
    public bool IsEmpty => _results.Count == 0;
    public string Heading => _searchText.Trim().Length == 0 ? L.T("Häufig verwendet und Favoriten") : L.F("{0} beste Treffer", _results.Count);
    public string EmptyText => _searchText.Trim().Length == 0
        ? L.T("Noch nichts gestartet. Tippe einen Namen – oder Tab für das volle Panel.")
        : L.T("Keine Treffer. Tab öffnet das volle Panel mit allen Einträgen.");

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var v = _results.Count == 0 ? 0 : Math.Clamp(value, 0, _results.Count - 1);
            if (!Set(ref _selectedIndex, v)) return;
            for (int i = 0; i < _results.Count; i++) _results[i].IsSelected = i == v;
            OnPropertyChanged(nameof(Selected));
        }
    }
    public ItemViewModel? Selected => _results.Count == 0 ? null : _results[Math.Clamp(_selectedIndex, 0, _results.Count - 1)];

    public void Refresh()
    {
        var terms = SearchQuery.Parse(_searchText).Terms;
        Results = _host.ViewModel.SearchFlat(_searchText, MaxResults)
            .Select(i => new ItemViewModel(_app, _host.ViewModel, i, "spotlight") { HighlightTerms = terms })
            .ToList();
        _selectedIndex = -1;
        SelectedIndex = 0;
    }

    /// <summary>Ausgewählten Treffer starten (Standardprofil oder mit erzwungener Ausführungsart).</summary>
    public bool Run(ExecutionMode? mode = null, bool remote = false)
    {
        if (Selected is not { } item) return false;
        var cmd = remote ? item.RunRemoteCommand
            : mode == ExecutionMode.Administrator ? item.RunAdminCommand
            : mode == ExecutionMode.OtherUser ? item.RunAsUserCommand
            : item.RunCommand;
        if (!cmd.CanExecute(null)) return false;
        CloseRequested?.Invoke();
        cmd.Execute(null);
        return true;
    }

    /// <summary>Tab: ins volle Panel wechseln, Suchtext übernehmen.</summary>
    public void OpenPanel()
    {
        CloseRequested?.Invoke();
        _app.ShowPanelFromHotkey(_host, forcePanel: true);
        _host.ViewModel.SearchText = _searchText;
    }

    public ICommand RunSelectedCommand => _runSelected ??= new RelayCommand(p =>
    {
        if (p is ItemViewModel vm) SelectedIndex = _results.ToList().IndexOf(vm);
        Run();
    });
    private ICommand? _runSelected;

    public event Action? CloseRequested;
}
