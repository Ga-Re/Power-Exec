using PowerExec.Core.Config;
using PowerExec.ViewModels;
using PowerExec.Views;

namespace PowerExec.Services;

/// <summary>WPF-Oberfläche eines Panels: Panelfenster, Hover-Zonen bzw. Mini-Docks (je Monitor).</summary>
public sealed class PanelHost : IPanelView
{
    public PanelHost(AppController app, AppPanel panel)
    {
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
    public void ShowFromTrigger(MonitorInfo monitor) => Window.ShowPanel(monitor, viaHotkey: false);

    public void ShowViaHotkey()
    {
        var monitor = Config.MonitorMode == MonitorMode.All
            ? ScreenService.MonitorAtCursor()
            : ScreenService.TriggerMonitors(Config)[0];
        Window.ShowPanel(monitor, viaHotkey: true);
    }

    public void Hide() => Window.HidePanel();

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
