using PowerExec.Core.Config;
using PowerExec.ViewModels;
using PowerExec.Views;

namespace PowerExec.Services;

/// <summary>Ein laufendes Panel: Einstellungen, ViewModel, Fenster, Hover-Zonen und Tastenkürzel-Kennung.</summary>
public sealed class PanelHost : IDisposable
{
    public PanelHost(AppController app, PanelConfig config, int hotkeyId)
    {
        Config = config;
        HotkeyId = hotkeyId;
        ViewModel = new PanelViewModel(app, this);
        Window = new PanelWindow(app, this);
    }

    public PanelConfig Config { get; }
    public int HotkeyId { get; }
    public PanelViewModel ViewModel { get; }
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

    public void Dispose()
    {
        foreach (var t in Triggers) t.Close();
        Triggers.Clear();
        foreach (var d in Docks) d.Close();
        Docks.Clear();
        Window.CloseForGood();
        ViewModel.Detach();
    }
}
