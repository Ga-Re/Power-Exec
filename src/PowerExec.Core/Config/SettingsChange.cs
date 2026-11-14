using System.Text.Json;

namespace PowerExec.Core.Config;

/// <summary>
/// Was sich zwischen zwei Ständen der Einstellungen geändert hat – daraus leitet die App ab, was neu aufzubauen ist
/// (Ordner neu einlesen, Panels anpassen oder neu erzeugen, Farben neu anwenden, Autostart setzen).
/// </summary>
public sealed record SettingsChange(bool Folders, bool Panels, bool Appearance, bool Autostart)
{
    public static SettingsChange Between(AppSettings before, AppSettings after) => new(
        Folders: FolderKey(before) != FolderKey(after),
        Panels: PanelKey(before) != PanelKey(after),
        Appearance: AppearanceKey(before) != AppearanceKey(after),
        Autostart: before.StartWithWindows != after.StartWithWindows);

    /// <summary>Design, Akzent, Größe, Ecken und der app-weite Teil des Designs: eine Änderung erzeugt Panels und Farben neu.</summary>
    private static (ThemeMode Theme, bool UseWindowsAccent, int UiScalePercent, CornerStyle Corners, string Look) AppearanceKey(AppSettings s) =>
        (s.Theme, s.UseWindowsAccent, s.UiScalePercent, s.Corners, s.Look.Key);

    /// <summary>Alles, was Panelfenster und Hover-Zonen betrifft (ohne „angeheftet“).</summary>
    private static string PanelKey(AppSettings s) =>
        JsonSerializer.Serialize(s.Panels.Select(p => { var c = p.Clone(); c.Pinned = false; return c; })) +
        $"|{s.HoverDelayMs}|{s.HideHotZoneInFullscreen}";

    /// <summary>Ordner samt Filtern: eine Änderung liest die Ordner neu ein.</summary>
    private static string FolderKey(AppSettings s) =>
        string.Join("|", s.Folders.Select(f => $"{f.Path}*{f.Recursive}*{string.Join(",", f.Extensions)}*{string.Join(",", f.Excludes)}"))
        + "#" + string.Join(",", s.DefaultExtensions) + "#" + string.Join(",", s.GlobalExcludes);
}
