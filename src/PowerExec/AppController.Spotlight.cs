using PowerExec.Services;
using PowerExec.ViewModels;
using PowerExec.Views;

namespace PowerExec;

/// <summary>Spotlight-Modus (A1): Tastenkürzel öffnet eine schwebende Suche in der Bildschirmmitte.</summary>
public sealed partial class AppController
{
    private IAppWindow? _spotlight;

    /// <summary>Selbsttest: Fenster nicht beim Wechsel des aktiven Fensters schließen.</summary>
    internal bool SpotlightStaysOpen { get; set; }

    public void ToggleSpotlight(AppPanel host)
    {
        if (_spotlight != null) { _spotlight.Close(); return; }
        HidePanels();
        var w = Ui.ShowSpotlight(new SpotlightViewModel(this, host), closeOnDeactivate: !SpotlightStaysOpen);
        _spotlight = w;
        w.Closed += () => { if (_spotlight == w) _spotlight = null; };
    }

    /// <summary>Für den Selbsttest.</summary>
    internal IAppWindow? Spotlight => _spotlight;
}
