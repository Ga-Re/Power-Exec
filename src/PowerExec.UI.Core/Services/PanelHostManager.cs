using PowerExec.Core.Config;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI.Services;

/// <summary>
/// Verwaltet die laufenden Panels: anlegen, im bestehenden Fenster anpassen, freigeben, Tastenkürzel anmelden
/// und die Vollbild-Erkennung an die Fenster weitergeben. Kennt nur <see cref="IAppUi"/> und die Einstellungen.
/// </summary>
public sealed class PanelHostManager
{
    private readonly IAppUi _ui;
    private readonly Func<AppSettings> _settings;
    private readonly Func<AppPanel, PanelViewModel> _createViewModel;
    private readonly List<AppPanel> _hosts = new();
    private IUiTimer? _fullscreenTimer;

    /// <param name="createViewModel">Erzeugt das ViewModel zu einem Panel (braucht den App-Kern als Host).</param>
    public PanelHostManager(IAppUi ui, Func<AppSettings> settings, Func<AppPanel, PanelViewModel> createViewModel)
    {
        _ui = ui;
        _settings = settings;
        _createViewModel = createViewModel;
    }

    public IReadOnlyList<AppPanel> Hosts => _hosts;

    /// <summary>Erzeugt alle Panels neu (nach dem Start und nach geänderten Einstellungen).</summary>
    /// <returns>Fehlermeldungen zu Tastenkürzeln oder null.</returns>
    public string? Create()
    {
        var settings = _settings();
        // Offene Panels bleiben nach dem Neuaufbau offen – man soll sehen, was die geänderte Einstellung bewirkt.
        // Die alten Fenster schließen erst, wenn die neuen stehen: so verschwindet das Panel nicht kurz.
        var wasOpen = _hosts.Select(h => h.View.OpenState).ToList();
        var selected = _hosts.Select(h => h.ViewModel.SelectedKey).ToList();
        var old = _hosts.ToList();
        _hosts.Clear();
        _ui.Hotkeys.UnregisterAll();
        if (settings.Panels.Count == 0) settings.Panels.Add(new PanelConfig { Hotkey = PanelConfig.DefaultHotkey });
        var errors = new List<string>();
        int id = 0x5045; // "PE"
        foreach (var config in settings.Panels)
        {
            var host = new AppPanel(config, id++);
            host.ViewModel = _createViewModel(host);
            host.View = _ui.CreatePanelView(host);
            _hosts.Add(host);
            var err = _ui.Hotkeys.Register(host.HotkeyId, config.Hotkey);
            if (err != null) errors.Add($"{config.Name}: {err}");
        }
        for (var i = 0; i < Math.Min(selected.Count, _hosts.Count); i++) _hosts[i].ViewModel.PreselectKey(selected[i]);
        foreach (var h in _hosts) h.ViewModel.Rebuild();
        var restored = false;
        for (var i = 0; i < Math.Min(wasOpen.Count, _hosts.Count); i++)
            if (wasOpen[i] is { } state) { _hosts[i].View.RestoreOpenState(state); restored = true; }
        if (!restored) Release(old);
        else
        {
            // Die alten Fenster bleiben kurz darunter liegen, bis die neuen gezeichnet sind – sonst blitzt das Panel auf
            IUiTimer? later = null;
            later = _ui.Dispatcher.CreateTimer(TimeSpan.FromMilliseconds(250), () => { later!.Stop(); Release(old); });
            later.Start();
        }
        return errors.Count == 0 ? null : string.Join("\n", errors);
    }

    /// <summary>Passen die neuen Panel-Einstellungen in die bestehenden Fenster (gleiche Anzahl, gleiche Fensterart)?</summary>
    public bool CanUpdateInPlace(IReadOnlyList<PanelConfig> previous)
    {
        var panels = _settings().Panels;
        return _hosts.Count == panels.Count && previous.Count == panels.Count &&
               _hosts.Select((h, i) => PanelConfig.FitsSameWindow(h.Config, panels[i])).All(ok => ok);
    }

    /// <summary>Neue Panel-Einstellungen an die bestehenden Panels geben; Tastenkürzel neu anmelden.</summary>
    /// <returns>Fehlermeldungen zu Tastenkürzeln oder null.</returns>
    public string? UpdateInPlace(IReadOnlyList<PanelConfig> previous)
    {
        var panels = _settings().Panels;
        _ui.Hotkeys.UnregisterAll();
        var errors = new List<string>();
        for (var i = 0; i < _hosts.Count; i++)
        {
            var host = _hosts[i];
            var before = previous.Count > i ? previous[i] : host.Config;
            host.ReplaceConfig(panels[i]);
            host.View.ApplyConfig(before);
            host.ViewModel.ConfigReplaced();
            var err = _ui.Hotkeys.Register(host.HotkeyId, host.Config.Hotkey);
            if (err != null) errors.Add($"{host.Config.Name}: {err}");
        }
        return errors.Count == 0 ? null : string.Join("\n", errors);
    }

    /// <summary>Alle Panels schließen und freigeben.</summary>
    public void DisposeAll()
    {
        Release(_hosts);
        _hosts.Clear();
    }

    private static void Release(IEnumerable<AppPanel> panels)
    {
        foreach (var h in panels)
        {
            h.View.Dispose();
            h.ViewModel.Detach();
        }
    }

    public void Rebuild()
    {
        foreach (var h in _hosts) h.ViewModel.Rebuild();
    }

    /// <summary>Nach einer Aktion alle Panels einfahren (außer angeheftete, sofern nicht erzwungen).</summary>
    public void Hide(bool force = false)
    {
        foreach (var h in _hosts) h.View.HideAfterAction(force);
    }

    /// <summary>Prüft jede Sekunde, ob das Vordergrundfenster einen Monitor vollständig bedeckt.</summary>
    public void StartFullscreenWatch()
    {
        _fullscreenTimer ??= _ui.Dispatcher.CreateTimer(TimeSpan.FromSeconds(1), OnFullscreenTick);
        _fullscreenTimer.Start();
    }

    private void OnFullscreenTick()
    {
        try
        {
            var probe = _settings().HideHotZoneInFullscreen ? FullscreenProbe.Current() : null;
            foreach (var h in _hosts) h.View.ApplyFullscreen(probe);
        }
        catch
        {
            // Erkennung ist optional
        }
    }
}
