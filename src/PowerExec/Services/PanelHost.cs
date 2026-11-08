using PowerExec.Core.Config;
using PowerExec.ViewModels;
using PowerExec.Views;

namespace PowerExec.Services;

/// <summary>WPF-Oberfläche eines Panels: Panelfenster, Hover-Zonen bzw. Mini-Docks (je Monitor).</summary>
public sealed class PanelHost : IPanelView
{
    private readonly AppController _app;

    public PanelHost(AppController app, AppPanel panel)
    {
        _app = app;
        Panel = panel;
        Window = new PanelWindow(app, this);
    }

    /// <summary>Das Panel im App-Kern (Einstellungen, ViewModel, Tastenkürzel-Kennung).</summary>
    public AppPanel Panel { get; }
    public PanelConfig Config => Panel.Config;
    public int HotkeyId => Panel.HotkeyId;
    public PanelViewModel ViewModel => Panel.ViewModel;

    public PanelWindow Window { get; }
    public List<EdgeTriggerWindow> Triggers { get; } = new();

    /// <summary>Mini-Docks (je Monitor), wenn in den Einstellungen eingeschaltet.</summary>
    public List<DockWindow> Docks { get; } = new();

    public void CreateTriggers(AppController app)
    {
        foreach (var t in Triggers) t.Close();
        Triggers.Clear();
        foreach (var d in Docks) d.Close();
        Docks.Clear();
        foreach (var m in ScreenService.TriggerMonitors(Config))
        {
            // Mit Mini-Dock übernimmt das Dock („⋯“) das Ausfahren; die Hover-Zone entfällt
            if (!Config.ShowDock)
            {
                var t = new EdgeTriggerWindow(app, this, m);
                t.Show();
                Triggers.Add(t);
            }
            if (Config.ShowDock)
            {
                var d = new DockWindow(app, this, m);
                d.Show();
                Docks.Add(d);
            }
        }
    }

    // ── IPanelView ───────────────────────────────────────────────────

    public bool IsOpen => Window.IsOpen;
    public bool OpenedViaHotkey => Window.OpenedViaHotkey;
    public object Owner => Window;

    /// <summary>Über die Hover-Zone oder das Mini-Dock auf diesem Monitor ausfahren.</summary>
    public void ShowFromTrigger(MonitorInfo monitor)
    {
        var wasOpen = Window.IsOpen && !Window.IsHiding;
        Window.ShowPanel(monitor, viaHotkey: false);
        if (!wasOpen) _shownByTrigger = DateTime.UtcNow;
        KeepTriggersClickable();
    }

    private DateTime _shownByTrigger;

    /// <summary>
    /// Klick auf die Hover-Zone eines angehefteten Panels: ein- bzw. ausfahren. Hat das Darüberfahren das Panel gerade
    /// erst ausgefahren, bleibt es offen – der Klick gehört dann noch zum Öffnen.
    /// </summary>
    public void ToggleFromTrigger(MonitorInfo monitor)
    {
        if (Window.IsOpen && !Window.IsHiding)
        {
            if (DateTime.UtcNow - _shownByTrigger < TimeSpan.FromMilliseconds(600)) return;
            Window.HidePanel();
        }
        else ShowFromTrigger(monitor);
    }

    /// <summary>Angeheftet: Hover-Zonen über dem Panel halten, damit ein Klick darauf es wieder einfährt.</summary>
    private void KeepTriggersClickable()
    {
        if (!Config.Pinned) return;
        foreach (var t in Triggers) t.BringAboveWindows();
    }

    public void ShowViaHotkey()
    {
        var monitor = Config.MonitorMode == MonitorMode.All
            ? ScreenService.MonitorAtCursor()
            : ScreenService.TriggerMonitors(Config)[0];
        Window.ShowPanel(monitor, viaHotkey: true);
        KeepTriggersClickable();
    }

    public void Hide() => Window.HidePanel();

    /// <summary>Geänderte Einstellungen ins bestehende Fenster übernehmen; Hover-Zonen nur bei Bedarf neu.</summary>
    public void ApplyConfig(PanelConfig previous)
    {
        Window.ApplyConfigChange();
        if (previous.TriggerKey != Config.TriggerKey) CreateTriggers(_app);
        KeepTriggersClickable();
    }

    public PanelOpenState? OpenState =>
        Window.IsOpen && !Window.IsHiding && Window.CurrentMonitor is { } m ? new PanelOpenState(m.Index, Window.OpenedViaHotkey) : null;

    public void RestoreOpenState(PanelOpenState state)
    {
        var monitor = ScreenService.GetMonitors().FirstOrDefault(m => m.Index == state.MonitorIndex) ?? ScreenService.TriggerMonitors(Config)[0];
        Window.ShowInPlace(monitor, state.ViaHotkey);
        KeepTriggersClickable();
    }

    public void HideAfterAction(bool force) => Window.HideAfterAction(force);

    public void SetBusy(bool busy)
    {
        foreach (var t in Triggers) t.SetBusy(busy);
    }

    public void ApplyFullscreen(FullscreenProbe? probe)
    {
        foreach (var t in Triggers)
        {
            bool full = probe?.Covers(t.Monitor.Bounds) == true;
            if (full && !Window.IsOpen) t.SetSuspended(true);
            else if (!full) t.SetSuspended(false);
        }
        foreach (var d in Docks) d.SetSuspended(probe?.Covers(d.Monitor.Bounds) == true);
    }

    public void Dispose()
    {
        foreach (var t in Triggers) t.Close();
        Triggers.Clear();
        foreach (var d in Docks) d.Close();
        Docks.Clear();
        Window.CloseForGood();
    }
}
