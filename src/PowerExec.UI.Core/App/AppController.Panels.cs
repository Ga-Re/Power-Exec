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

/// <content>Panels (über <see cref="PanelHostManager"/>), Arbeitsbereiche, Tastenkürzel.</content>
public partial class AppController
{
    // ---- Panel ----------------------------------------------------------

    /// <summary>Die laufenden Panels (anlegen, anpassen, freigeben).</summary>
    public PanelHostManager Panels { get; }

    public IReadOnlyList<AppPanel> Hosts => Panels.Hosts;

    /// <summary>Erzeugt alle Panels neu.</summary>
    /// <returns>Fehlermeldungen zu Tastenkürzeln oder null.</returns>
    protected string? CreateHosts() => Panels.Create();

    protected void DisposePanels() => Panels.DisposeAll();

    public void RebuildPanels() => Panels.Rebuild();

    /// <summary>Nach einer Aktion alle Panels einfahren (außer angeheftete, sofern nicht erzwungen).</summary>
    public void HidePanels(bool force = false) => Panels.Hide(force);

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
        host ??= Hosts.FirstOrDefault();
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
