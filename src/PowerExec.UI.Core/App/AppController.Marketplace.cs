using PowerExec.UI.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI;

/// <content>Marktplatz: eigenes Fenster, losgelöst von den Einstellungen.</content>
public partial class AppController
{
    private IAppWindow? _marketplaceWindow;
    private MarketplaceWindowViewModel? _marketplace;

    /// <summary>Marktplatz öffnen bzw. nach vorn holen; <paramref name="tab"/> wählt den Bereich.</summary>
    public void ShowMarketplace(string? tab = null)
    {
        if (_marketplaceWindow is { IsOpen: true } && _marketplace != null)
        {
            if (tab != null) _marketplace.Tab = tab;
            _marketplaceWindow.BringToFront();
            return;
        }
        HidePanels();
        var vm = new MarketplaceWindowViewModel(this, updated =>
        {
            ApplySettings(updated);
            SettingsChangedOutside?.Invoke(); // ein offenes Einstellungsfenster liest den neuen Stand
        });
        if (tab != null) vm.Tab = tab;
        _marketplace = vm;
        _marketplaceWindow = Ui.ShowMarketplace(vm);
        _marketplaceWindow.Closed += () => { _marketplaceWindow = null; _marketplace = null; };
        vm.EnsureLoadedAsync().Forget();
    }
}
