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

/// <content>Einstellungen speichern und anwenden, Variablen, Reaktion auf das Windows-Design.</content>
public partial class AppController
{
    public void SaveSettings()
    {
        try { Store.Save(Settings); }
        catch (Exception ex)
        {
            Dialogs.Warn(L.T("The settings couldn't be saved:\n") + ex.Message, "Power-Exec");
        }
    }

    /// <summary>
    /// Änderung aus dem Panel (Gruppe, Favorit, eigener Eintrag …): speichern, Panels neu aufbauen und
    /// ein offenes Einstellungsfenster informieren, damit es den neuen Stand zeigt.
    /// </summary>
    private void CommitUserChange()
    {
        SaveSettings();
        RebuildPanels();
        SettingsChangedOutside?.Invoke();
    }

    /// <summary>Einstellungen wurden außerhalb des Einstellungsfensters geändert (z. B. im Panel).</summary>
    public event Action? SettingsChangedOutside;

    /// <summary>
    /// Übernimmt neue Einstellungen (live aus dem Einstellungsfenster). Panels und Hover-Zonen werden nur neu erzeugt,
    /// wenn sich an ihnen etwas geändert hat; Ordner nur neu eingelesen, wenn Ordner oder Filter anders sind.
    /// </summary>
    /// <returns>Fehlermeldung zum Tastenkürzel oder null.</returns>
    public string? ApplySettings(AppSettings updated)
    {
        ClearUndo();
        var change = SettingsChange.Between(Settings, updated);
        var previousPanels = Settings.Panels;
        Settings = updated;
        WarmVariables();
        SaveSettings();
        if (change.Appearance) Ui.ApplyTheme(Settings);
        if (change.Autostart) AutostartService.Apply(Settings.StartWithWindows);
        string? hotkeyError = null;
        // Panels möglichst im bestehenden Fenster anpassen (kein Neuaufbau, kein Flackern); neu erzeugt wird nur, wenn sich
        // das Design oder die Fensterart (Klarglas ↔ Glas) ändert oder Panels hinzukommen bzw. wegfallen
        if (change.Appearance || Hosts.Count == 0 || (change.Panels && !Panels.CanUpdateInPlace(previousPanels))) hotkeyError = Panels.Create();
        else if (change.Panels) hotkeyError = Panels.UpdateInPlace(previousPanels);
        if (change.Folders) Repository.UpdateSettings(Settings);
        else
        {
            Repository.SetupWatchers();
            RebuildPanels();
        }
        _marketplace?.SettingsChanged(); // offenes Marktplatz-Fenster arbeitet mit dem neuen Stand weiter
        return hotkeyError;
    }

    /// <summary>Anzeigewert einer {{$Variable}} (Vorschau im Panel).</summary>
    public string VariablePreview(string name) => VariableResolver.Preview(name);

    /// <summary>Eigene Variablen für den Eintrags-Editor (Einfügen, Warnung bei unbekannten).</summary>
    public IReadOnlyList<UserVariable> UserVariables => Settings.Variables;

    /// <summary>Löst {{$Variablen}} auf (eingebaute und eigene, mit Zwischenspeicher für PowerShell-Befehle).</summary>
    public VariableResolver VariableResolver => _variableResolver ??= new VariableResolver(() => Settings,
        cmd => VariableCommandRunner.Run(cmd, Settings.DefaultEngine), SecretProtector.Unprotect, () => Shell.GetClipboardText());
    private VariableResolver? _variableResolver;

    /// <summary>PowerShell-Variablen mit Zwischenspeicher bzw. „je Sitzung“ im Hintergrund ermitteln.</summary>
    public void WarmVariables()
    {
        if (!Settings.Variables.Any(v => v.Kind == VariableKind.PowerShell && v.Refresh != VariableRefresh.EveryUse)) return;
        var resolver = VariableResolver;
        Task.Run(resolver.Warm).Forget();
    }

    /// <summary>Windows-Design, Akzentfarbe oder Kontrastdesign geändert: Farben neu anwenden.</summary>
    private void OnSystemThemeChanged()
    {
        var before = Ui.ThemeStateKey;
        Ui.ApplyTheme(Settings);
        if (before != Ui.ThemeStateKey) CreateHosts();
    }
}
