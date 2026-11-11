using PowerExec.UI.Infrastructure;

namespace PowerExec.UI.ViewModels;

/// <summary>
/// Gemeinsamer Stand des Update-Hinweises für alle Panels und das Einstellungsfenster.
/// Liegt im AppController, damit er neu erzeugte Panels und das Beenden der App übersteht.
/// </summary>
public sealed class UpdateState : ObservableObject
{
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private bool _available;
    private bool _busy;

    public string Title { get => _title; set => Set(ref _title, value); }
    public string Subtitle { get => _subtitle; set => Set(ref _subtitle, value); }
    public bool Available { get => _available; set => Set(ref _available, value); }
    public bool Busy { get => _busy; set => Set(ref _busy, value); }
}
