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

    /// <returns>Fehlermeldung zum Tastenkürzel oder null.</returns>
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
        var foldersChanged = !FoldersEqual(Settings, updated);
        var panelsChanged = PanelKey(Settings) != PanelKey(updated);
        var previousPanels = Settings.Panels;
        var themeChanged = AppearanceKey(Settings) != AppearanceKey(updated);
        var autostartChanged = Settings.StartWithWindows != updated.StartWithWindows;
        Settings = updated;
        WarmVariables();
        SaveSettings();
        if (themeChanged) Ui.ApplyTheme(Settings);
        if (autostartChanged) AutostartService.Apply(Settings.StartWithWindows);
        string? hotkeyError = null;
        // Panels möglichst im bestehenden Fenster anpassen (kein Neuaufbau, kein Flackern); neu erzeugt wird nur, wenn sich
        // das Design oder die Fensterart (Klarglas ↔ Glas) ändert oder Panels hinzukommen bzw. wegfallen
        if (themeChanged || _hosts.Count == 0 || (panelsChanged && !CanUpdateInPlace(previousPanels))) hotkeyError = CreateHosts();
        else if (panelsChanged) hotkeyError = UpdateHostsInPlace(previousPanels);
        if (foldersChanged) Repository.UpdateSettings(Settings);
        else
        {
            Repository.SetupWatchers();
            RebuildPanels();
        }
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

    /// <summary>Design, Akzent, Fensterhintergrund und Größe: Änderung erzeugt Panels und Farben neu.</summary>
    private static (ThemeMode Theme, bool UseWindowsAccent, int UiScalePercent, CornerStyle Corners) AppearanceKey(AppSettings s) =>
        (s.Theme, s.UseWindowsAccent, s.UiScalePercent, s.Corners);

    /// <summary>Windows-Design, Akzentfarbe oder Kontrastdesign geändert: Farben neu anwenden.</summary>
    private void OnSystemThemeChanged()
    {
        var before = Ui.ThemeStateKey;
        Ui.ApplyTheme(Settings);
        if (before != Ui.ThemeStateKey) CreateHosts();
    }

    /// <summary>Alles, was Panelfenster und Hover-Zonen betrifft (ohne „angeheftet“).</summary>
    private static string PanelKey(AppSettings s) =>
        System.Text.Json.JsonSerializer.Serialize(s.Panels.Select(p => { var c = p.Clone(); c.Pinned = false; return c; })) +
        $"|{s.HoverDelayMs}|{s.HideHotZoneInFullscreen}";

    private static bool FoldersEqual(AppSettings a, AppSettings b)
    {
        string Key(AppSettings s) => string.Join("|", s.Folders.Select(f => $"{f.Path}*{f.Recursive}*{string.Join(",", f.Extensions)}*{string.Join(",", f.Excludes)}"))
                                     + "#" + string.Join(",", s.DefaultExtensions) + "#" + string.Join(",", s.GlobalExcludes);
        return Key(a) == Key(b);
    }
}
