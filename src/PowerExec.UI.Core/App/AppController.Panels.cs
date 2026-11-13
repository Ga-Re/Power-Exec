using System.Globalization;
using PowerExec.Core.Localization;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Search;
using PowerExec.Core.Updates;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI;

/// <content>Panels anlegen, im bestehenden Fenster anpassen und freigeben; Vollbild-Erkennung, Arbeitsbereiche, Tastenkürzel.</content>
public partial class AppController
{
    // ---- Panel ----------------------------------------------------------

    /// <summary>Erzeugt alle Panels neu (nach dem Start und nach geänderten Einstellungen).</summary>
    /// <returns>Fehlermeldungen zu Tastenkürzeln oder null.</returns>
    protected string? CreateHosts()
    {
        // Offene Panels bleiben nach dem Neuaufbau offen – man soll sehen, was die geänderte Einstellung bewirkt.
        // Die alten Fenster schließen erst, wenn die neuen stehen: so verschwindet das Panel nicht kurz.
        var wasOpen = _hosts.Select(h => h.View.OpenState).ToList();
        var selected = _hosts.Select(h => h.ViewModel.SelectedKey).ToList();
        var old = _hosts.ToList();
        _hosts.Clear();
        Ui.Hotkeys.UnregisterAll();
        if (Settings.Panels.Count == 0) Settings.Panels.Add(new PanelConfig { Hotkey = PanelConfig.DefaultHotkey });
        var errors = new List<string>();
        int id = 0x5045; // "PE"
        foreach (var config in Settings.Panels)
        {
            var host = new AppPanel(config, id++);
            host.ViewModel = new PanelViewModel(this, host);
            host.View = Ui.CreatePanelView(host);
            _hosts.Add(host);
            var err = Ui.Hotkeys.Register(host.HotkeyId, config.Hotkey);
            if (err != null) errors.Add($"{config.Name}: {err}");
        }
        for (var i = 0; i < Math.Min(selected.Count, _hosts.Count); i++) _hosts[i].ViewModel.PreselectKey(selected[i]);
        foreach (var h in _hosts) h.ViewModel.Rebuild();
        var restored = false;
        for (var i = 0; i < Math.Min(wasOpen.Count, _hosts.Count); i++)
            if (wasOpen[i] is { } state) { _hosts[i].View.RestoreOpenState(state); restored = true; }
        if (!restored) DisposePanels(old);
        else
        {
            // Die alten Fenster bleiben kurz darunter liegen, bis die neuen gezeichnet sind – sonst blitzt das Panel auf
            IUiTimer? later = null;
            later = Ui.Dispatcher.CreateTimer(TimeSpan.FromMilliseconds(250), () => { later!.Stop(); DisposePanels(old); });
            later.Start();
        }
        return errors.Count == 0 ? null : string.Join("\n", errors);
    }

    private bool CanUpdateInPlace(IReadOnlyList<PanelConfig> previous) =>
        _hosts.Count == Settings.Panels.Count && previous.Count == Settings.Panels.Count &&
        _hosts.Select((h, i) => PanelConfig.FitsSameWindow(h.Config, Settings.Panels[i])).All(ok => ok);

    /// <summary>Neue Panel-Einstellungen an die bestehenden Panels geben; Tastenkürzel neu anmelden.</summary>
    /// <returns>Fehlermeldungen zu Tastenkürzeln oder null.</returns>
    private string? UpdateHostsInPlace(IReadOnlyList<PanelConfig> previous)
    {
        Ui.Hotkeys.UnregisterAll();
        var errors = new List<string>();
        for (var i = 0; i < _hosts.Count; i++)
        {
            var host = _hosts[i];
            var before = previous.Count > i ? previous[i] : host.Config;
            host.ReplaceConfig(Settings.Panels[i]);
            host.View.ApplyConfig(before);
            host.ViewModel.ConfigReplaced();
            var err = Ui.Hotkeys.Register(host.HotkeyId, host.Config.Hotkey);
            if (err != null) errors.Add($"{host.Config.Name}: {err}");
        }
        return errors.Count == 0 ? null : string.Join("\n", errors);
    }

    protected void DisposePanels()
    {
        DisposePanels(_hosts);
        _hosts.Clear();
    }

    private static void DisposePanels(IEnumerable<AppPanel> panels)
    {
        foreach (var h in panels)
        {
            h.View.Dispose();
            h.ViewModel.Detach();
        }
    }

    public void RebuildPanels()
    {
        foreach (var h in _hosts) h.ViewModel.Rebuild();
    }

    private void ForEachPanel(Action<PanelViewModel> action)
    {
        foreach (var h in _hosts) action(h.ViewModel);
    }

    /// <summary>Nach einer Aktion alle Panels einfahren (außer angeheftete, sofern nicht erzwungen).</summary>
    public void HidePanels(bool force = false)
    {
        foreach (var h in _hosts) h.View.HideAfterAction(force);
    }

    private IUiTimer? _fullscreenTimer;

    /// <summary>Prüft jede Sekunde, ob das Vordergrundfenster einen Monitor vollständig bedeckt.</summary>
    private void StartFullscreenWatch()
    {
        _fullscreenTimer ??= UiDispatcher.CreateTimer(TimeSpan.FromSeconds(1), OnFullscreenTick);
        _fullscreenTimer.Start();
    }

    private void OnFullscreenTick()
    {
        try
        {
            var probe = Settings.HideHotZoneInFullscreen ? FullscreenProbe.Current() : null;
            foreach (var h in _hosts) h.View.ApplyFullscreen(probe);
        }
        catch
        {
            // Erkennung ist optional
        }
    }

    // ---- Arbeitsbereiche -----------------------------------------------

    /// <summary>Wechselt den Arbeitsbereich (gilt für alle Panels) und durchsucht die Ordner neu.</summary>
    public void SwitchWorkspace(string name)
    {
        if (name.Equals(Settings.ActiveWorkspace, StringComparison.OrdinalIgnoreCase)) return;
        if (!WorkspaceManager.Switch(Settings, name)) return;
        ClearUndo();
        SaveSettings();
        AppLog.Info(string.Format(CultureInfo.InvariantCulture, "Switched workspace: {0}", Settings.ActiveWorkspace));
        Repository.UpdateSettings(Settings);
        RebuildPanels();
        _settingsWindow?.Close(); // Einstellungen zeigen sonst noch den alten Arbeitsbereich
    }

    /// <summary>Tastenkürzel oder Tray: Panel öffnen bzw. wieder schließen.</summary>
    public void ShowPanelFromHotkey(AppPanel? host = null, bool forcePanel = false)
    {
        host ??= _hosts.FirstOrDefault();
        if (host == null) return;
        if (host.Config.HotkeyOpensSpotlight && !forcePanel)
        {
            ToggleSpotlight(host);
            return;
        }
        var w = host.View;
        if (w.IsOpen && w.OpenedViaHotkey)
        {
            w.Hide();
            return;
        }
        w.ShowViaHotkey();
    }
}
