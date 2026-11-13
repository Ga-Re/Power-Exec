using System.Globalization;
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
        Page.Tab = "All"; // das Fenster öffnet mit „Entdecken“
        UpdateCounts();
    }

    /// <summary>Listen und Befehle des Marktplatzes.</summary>
    public MarketplaceViewModel Page { get; private set; }

    /// <summary>Bereiche in der Navigation (Schlüssel wie <see cref="MarketplaceViewModel.Tab"/>), mit Anzahl.</summary>
    public IReadOnlyList<MarketplaceTab> Tabs { get; } = new[]
    {
        new MarketplaceTab("All", L.T("Discover")),
        new MarketplaceTab("Entry", L.T("Entries")),
        new MarketplaceTab("Script", L.T("Scripts")),
        new MarketplaceTab("Design", L.T("Designs")),
        new MarketplaceTab("Installed", L.T("Installed")),
        new MarketplaceTab("Sources", L.T("Sources"))
    };

    private void UpdateCounts()
    {
        foreach (var t in Tabs)
            t.CountText = t.Key switch
            {
                "All" => Page.AllCount.ToString(CultureInfo.CurrentCulture),
                "Entry" => Page.EntryCount.ToString(CultureInfo.CurrentCulture),
                "Script" => Page.ScriptCount.ToString(CultureInfo.CurrentCulture),
                "Design" => Page.DesignCount.ToString(CultureInfo.CurrentCulture),
                "Installed" => Page.InstalledCount.ToString(CultureInfo.CurrentCulture),
                _ => string.Empty
            };
    }

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
        var page = new MarketplaceViewModel(_app, _copy, OnChanged);
        page.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MarketplaceViewModel.SummaryText) && ReferenceEquals(page, Page)) UpdateCounts(); };
        return page;
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
        var tab = Page.Tab;
        Page = Load();
        Page.Tab = tab; // der gewählte Bereich bleibt
        UpdateCounts();
        OnPropertyChanged(nameof(Page));
        OnPropertyChanged(nameof(Tab));
        Page.EnsureLoadedAsync().Forget();
    }
}

/// <summary>Ein Bereich in der Navigation des Marktplatz-Fensters.</summary>
public sealed class MarketplaceTab : ObservableObject
{
    private string _countText = string.Empty;

    public MarketplaceTab(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public string Key { get; }
    public string Title { get; }
    /// <summary>Anzahl der Dateien im Bereich (leer bei „Quellen“).</summary>
    public string CountText { get => _countText; set => Set(ref _countText, value); }
}
