using PowerExec.Core.Updates;
using PowerExec.Services;
using PowerExec.ViewModels;
using PowerExec.Views;

namespace PowerExec;

/// <summary>Einführungs-Assistent und „Neu in dieser Version“.</summary>
public sealed partial class AppController
{
    private WelcomeWindow? _welcome;

    /// <summary>Neuerungen seit der zuletzt gesehenen Version (Karte im Panel), sonst null.</summary>
    public ReleaseNote? WhatsNew { get; private set; }

    /// <summary>Einführung zeigen (erster Start oder Einstellungen › Allgemein).</summary>
    public void ShowWelcome()
    {
        HidePanels(force: true);
        if (_welcome != null) { _welcome.Activate(); return; }
        _welcome = new WelcomeWindow(new WelcomeViewModel(this), tryPanel =>
        {
            _welcome = null;
            if (tryPanel) ShowPanelFromHotkey(_hosts.FirstOrDefault());
        });
        _welcome.Closed += (_, _) => _welcome = null;
        _welcome.Show();
        _welcome.Activate();
    }

    /// <summary>Karte „Neu in dieser Version“ schließen; erscheint erst beim nächsten Update wieder.</summary>
    public void DismissWhatsNew()
    {
        WhatsNew = null;
        Settings.LastSeenVersion = UpdateService.CurrentVersionText;
        SaveSettings();
        foreach (var h in _hosts) h.ViewModel.RefreshWhatsNew();
    }

    /// <summary>Für den Selbsttest: Karte mit einer bestimmten Version zeigen.</summary>
    internal void ShowWhatsNewForTest(ReleaseNote note)
    {
        WhatsNew = note;
        foreach (var h in _hosts) h.ViewModel.RefreshWhatsNew();
    }
}
