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

    private static readonly MarketplaceTab[] ItemTabs =
    {
        new("All", L.T("Discover")),
        new("Entry", L.T("Entries")),
        new("Script", L.T("Scripts")),
        new("Installed", L.T("Installed")),
        new("Sources", L.T("Sources"))
    };

    private static readonly MarketplaceTab[] DesignTabs =
    {
        new("Design", L.T("Discover")),
        new("DesignInstalled", L.T("My designs")),
        new("Sources", L.T("Sources"))
    };

    private bool _designArea;

    /// <summary>Oben gewählter Bereich: Designs (true) oder Einträge und Scripte.</summary>
    public bool IsDesignArea
    {
        get => _designArea;
        set
        {
            if (_designArea == value) return;
            _designArea = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsItemArea));
            OnPropertyChanged(nameof(Tabs));
            Tab = value ? "Design" : "All";
        }
    }
    public bool IsItemArea { get => !_designArea; set => IsDesignArea = !value; }

    /// <summary>Listen und Befehle des Marktplatzes.</summary>
    public MarketplaceViewModel Page { get; private set; }

    /// <summary>Navigation des gewählten Bereichs (Schlüssel wie <see cref="MarketplaceViewModel.Tab"/>), mit Anzahl.</summary>
    public IReadOnlyList<MarketplaceTab> Tabs => _designArea ? DesignTabs : ItemTabs;

    private void UpdateCounts()
    {
        foreach (var t in ItemTabs.Concat(DesignTabs))
            t.CountText = t.Key switch
            {
                "All" => Page.AllCount.ToString(CultureInfo.CurrentCulture),
                "Entry" => Page.EntryCount.ToString(CultureInfo.CurrentCulture),
                "Script" => Page.ScriptCount.ToString(CultureInfo.CurrentCulture),
                "Design" => Page.DesignCount.ToString(CultureInfo.CurrentCulture),
                "Installed" => Page.InstalledCount.ToString(CultureInfo.CurrentCulture),
                "DesignInstalled" => Page.DesignInstalledCount.ToString(CultureInfo.CurrentCulture),
                _ => string.Empty
            };
    }

    /// <summary>Gewählter Bereich der Navigation; „designs“ (Verweis aus den Einstellungen) öffnet die Designs.</summary>
    public string Tab
    {
        get => Page.Tab;
        set
        {
            var key = value.Equals("designs", StringComparison.OrdinalIgnoreCase) ? "Design" : value;
            if (key is "Design" or "DesignInstalled" && !_designArea) { _designArea = true; RaiseArea(); }
            else if (key is "All" or "Entry" or "Script" or "Installed" && _designArea) { _designArea = false; RaiseArea(); }
            Page.Tab = key;
            OnPropertyChanged();
        }
    }

    private void RaiseArea()
    {
        OnPropertyChanged(nameof(IsDesignArea));
        OnPropertyChanged(nameof(IsItemArea));
        OnPropertyChanged(nameof(Tabs));
    }

    /// <summary>Fenster schließt: ein ausprobiertes, nicht übernommenes Design wird zurückgenommen.</summary>
    public void Closing() => Page.EndTry();

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
