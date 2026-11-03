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

    public void CreateTriggers(AppController app)
    {
        foreach (var t in Triggers) t.Close();
        Triggers.Clear();
        foreach (var m in ScreenService.TriggerMonitors(Config))
        {
            var t = new EdgeTriggerWindow(app, this, m);
            t.Show();
            Triggers.Add(t);
        }
    }

    public void Dispose()
    {
        foreach (var t in Triggers) t.Close();
        Triggers.Clear();
        Window.CloseForGood();
        ViewModel.Detach();
    }
}
