using PowerExec.Services;
using PowerExec.ViewModels;
using PowerExec.Views;

namespace PowerExec;

/// <summary>Spotlight-Modus (A1): Tastenkürzel öffnet eine schwebende Suche in der Bildschirmmitte.</summary>
public sealed partial class AppController
{
    private SpotlightWindow? _spotlight;

    /// <summary>Selbsttest: Fenster nicht beim Wechsel des aktiven Fensters schließen.</summary>
    internal bool SpotlightStaysOpen { get; set; }

    public void ToggleSpotlight(PanelHost host)
    {
        if (_spotlight != null) { _spotlight.Close(); return; }
        HidePanels();
        _spotlight = new SpotlightWindow(new SpotlightViewModel(this, host), ScreenService.MonitorAtCursor()) { CloseOnDeactivate = !SpotlightStaysOpen };
        _spotlight.Closed += (_, _) => _spotlight = null;
        _spotlight.Show();
    }

    /// <summary>Für den Selbsttest.</summary>
    internal SpotlightWindow? Spotlight => _spotlight;
}
