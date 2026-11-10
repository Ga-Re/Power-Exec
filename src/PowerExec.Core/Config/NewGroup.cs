using PowerExec.Core.Localization;
namespace PowerExec.Core.Config;

/// <summary>Angaben aus dem Dialog „Neue Gruppe“: Name, Symbol oder Bild, optional Ordner (→ smarte Gruppe), Farbe, Darstellung.</summary>
public sealed record NewGroupSpec(string Name, IconRef Icon, string? Folder, string Color, ItemLayout? Layout)
{
    public bool IsSmart => !string.IsNullOrWhiteSpace(Folder);
}

public static class NewGroup
{
    /// <summary>
    /// Legt die Gruppe an (unter <paramref name="parent"/>, sonst auf oberster Ebene am Ende) und übernimmt Symbol, Farbe
    /// und Darstellung. Mit Ordner wird sie zur smarten Gruppe.
    /// </summary>
    /// <returns>Pfad der neuen Gruppe.</returns>
    public static string Create(AppSettings s, NewGroupSpec spec, string? parent = null)
    {
        parent = string.IsNullOrWhiteSpace(parent) ? null : GroupManager.Normalize(parent);
        var path = spec.IsSmart
            ? SmartGroups.Add(s, spec.Folder!, spec.Name, parent).Group
            : GroupManager.Create(s, spec.Name, parent);
        if (!spec.Icon.IsEmpty) GroupManager.SetIcon(s, path, spec.Icon);
        if (spec.Color.Length > 0) GroupManager.SetColor(s, path, spec.Color);
        if (spec.Layout != null) GroupManager.SetLayout(s, path, spec.Layout);
        return path;
    }

    /// <summary>Fehlermeldung, wenn der Ordner schon zu einer smarten Gruppe gehört (sonst null).</summary>
    public static string? FolderError(IEnumerable<FolderConfig> folders, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        var f = folders.FirstOrDefault(x => x.Path.Trim().TrimEnd('\\', '/').Equals(folder.Trim().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
        return f == null ? null : L.F("Dieser Ordner gehört bereits zur Gruppe „{0}“.", GroupManager.LeafOf(f.Group));
    }

    /// <summary>Vorschlag für den Namen aus dem Ordner („C:\Scripte\AD“ → „AD“).</summary>
    public static string NameFromFolder(string folder)
    {
        var leaf = folder.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault(x => x.Length > 0) ?? string.Empty;
        return GroupManager.CleanName(leaf.EndsWith(':') ? leaf.TrimEnd(':') : leaf);
    }
}
