using PowerExec.Core.Updates;
using PowerExec.UI.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI;

/// <summary>Einführungs-Assistent und „Neu in dieser Version“.</summary>
public partial class AppController
{
    private IAppWindow? _welcome;

    /// <summary>Neuerungen seit der zuletzt gesehenen Version (Karte im Panel), sonst null.</summary>
    public ReleaseNote? WhatsNew { get; private set; }

    /// <summary>Einführung zeigen (erster Start oder Einstellungen › Allgemein).</summary>
    public void ShowWelcome()
    {
        HidePanels(force: true);
        if (_welcome != null) { _welcome.BringToFront(); return; }
        var w = Ui.ShowWelcome(new WelcomeViewModel(this), tryPanel =>
        {
            _welcome = null;
            if (tryPanel) ShowPanelFromHotkey(Hosts.FirstOrDefault());
        });
        _welcome = w;
        w.Closed += () => { if (_welcome == w) _welcome = null; };
    }

    /// <summary>Karte „Neu in dieser Version“ schließen; erscheint erst beim nächsten Update wieder.</summary>
    public void DismissWhatsNew()
    {
        WhatsNew = null;
        Settings.LastSeenVersion = UpdateService.CurrentVersionText;
        SaveSettings();
        foreach (var h in Hosts) h.ViewModel.RefreshWhatsNew();
    }

    /// <summary>Selbsttest: Namenseingabe ohne Dialog beantworten.</summary>
    internal Func<string?>? AskForTest { get; set; }

    /// <summary>Für den Selbsttest: Karte mit einer bestimmten Version zeigen.</summary>
    internal void ShowWhatsNewForTest(ReleaseNote note)
    {
        WhatsNew = note;
        foreach (var h in Hosts) h.ViewModel.RefreshWhatsNew();
    }
}
