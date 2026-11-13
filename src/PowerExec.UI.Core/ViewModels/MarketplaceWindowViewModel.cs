using PowerExec.Core.Config;
using PowerExec.Core.Localization;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;

namespace PowerExec.UI.ViewModels;

/// <summary>
/// Der Marktplatz als eigenes Fenster (losgelöst von den Einstellungen): Einträge, Scripte und Designs laden und wieder
/// entfernen. Gearbeitet wird auf einer Kopie der Einstellungen; jede Installation wird sofort übernommen. Ändern sich
/// die Einstellungen anderswo (Einstellungsfenster, Panel), wird die Kopie erneuert – sonst überschriebe das nächste
/// Laden aus dem Marktplatz den neueren Stand.
/// </summary>
public sealed class MarketplaceWindowViewModel : ObservableObject
{
    private readonly ISettingsHost _app;
    private readonly Action<AppSettings> _apply;
    private AppSettings _copy = new();
    private bool _applying;

    /// <param name="app">App-Kern (gespeicherte Einstellungen, Dialoge, Laden aus dem Netz).</param>
    /// <param name="apply">Geänderte Einstellungen übernehmen.</param>
    public MarketplaceWindowViewModel(ISettingsHost app, Action<AppSettings> apply)
    {
        _app = app;
        _apply = apply;
        Page = Load();
    }

    /// <summary>Listen und Befehle des Marktplatzes.</summary>
    public MarketplaceViewModel Page { get; private set; }

    /// <summary>Bereiche des Fensters (Schlüssel wie <see cref="MarketplaceViewModel.Tab"/>).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Tabs { get; } = new[]
    {
        new KeyValuePair<string, string>("Entry", L.T("Entries")),
        new KeyValuePair<string, string>("Script", L.T("Scripts")),
        new KeyValuePair<string, string>("Design", L.T("Designs")),
        new KeyValuePair<string, string>("Installed", L.T("Installed")),
        new KeyValuePair<string, string>("Sources", L.T("Sources"))
    };

    public string Tab
    {
        get => Page.Tab;
        set { Page.Tab = value; OnPropertyChanged(); }
    }

    /// <summary>Dateilisten laden, falls noch nicht geschehen (beim Öffnen des Fensters).</summary>
    public Task EnsureLoadedAsync() => Page.EnsureLoadedAsync();

    private MarketplaceViewModel Load()
    {
        _copy = SettingsStore.Clone(_app.Settings);
        return new MarketplaceViewModel(_app, _copy, OnChanged);
    }

    private void OnChanged()
    {
        _applying = true;
        try { _apply(SettingsStore.Clone(_copy)); }
        finally { _applying = false; }
    }

    /// <summary>Die Einstellungen wurden anderswo geändert: mit dem neuen Stand weiterarbeiten.</summary>
    public void SettingsChanged()
    {
        if (_applying) return;
        Page = Load();
        OnPropertyChanged(nameof(Page));
        OnPropertyChanged(nameof(Tab));
    }
}
