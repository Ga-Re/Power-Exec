using PowerExec.Core.Localization;
using PowerExec.Core.Models;

namespace PowerExec.Core.Config;

/// <summary>
/// Parameter-Sets (B2): häufige Wertekombinationen eines Scripts als eigenes Startprofil mit festen Werten.
/// Sie erscheinen damit automatisch im Split-Button, im Profil-Dialog und im Verlauf.
/// </summary>
public static class ParameterSets
{
    /// <summary>Startprofil für einen einmaligen Start mit diesen Werten (Kennwörter fragt das Script selbst).</summary>
    public static LaunchProfile WithValues(LaunchProfile baseProfile, LaunchItem item, IReadOnlyDictionary<string, string?> values, string? name = null)
    {
        var p = baseProfile.Clone();
        p.Name = name ?? L.T("Panel");
        p.IsDefault = false;
        p.IsImplicit = false;
        p.Dialog = DialogMode.WhenNeeded;
        foreach (var param in item.Parameters)
        {
            if (param.IsSecure || param.Kind == ParameterKind.Secure) { p.Parameters[param.Name] = new ParameterSetting { Mode = ParameterMode.Ask }; continue; }
            values.TryGetValue(param.Name, out var v);
            p.Parameters[param.Name] = new ParameterSetting { Mode = ParameterMode.Fixed, Value = v ?? string.Empty };
        }
        return p;
    }

    /// <summary>
    /// Als benanntes Set speichern. Gibt es noch keine eigenen Profile, wird das bisherige Standardverhalten
    /// als Profil „Standard“ übernommen, damit ein Klick im Panel weiter dasselbe tut.
    /// </summary>
    /// <returns>null bei Erfolg, sonst eine Fehlermeldung (z. B. Name schon vergeben).</returns>
    public static string? Save(AppSettings s, LaunchItem item, string name, IReadOnlyDictionary<string, string?> values, LaunchProfile defaultProfile)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0) return L.T("Bitte einen Namen eingeben.");
        var entry = s.FindEntry(item.FullPath);
        if (entry == null)
        {
            entry = new EntryConfig { Path = item.FullPath };
            s.Entries.Add(entry);
        }
        if (entry.Profiles.Any(p => p.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))) return L.F("Ein Startprofil „{0}“ gibt es schon.", name);
        if (entry.Profiles.Count == 0)
        {
            var standard = defaultProfile.Clone();
            standard.IsImplicit = false;
            standard.IsDefault = true;
            entry.Profiles.Add(standard);
        }
        entry.Profiles.Add(WithValues(defaultProfile, item, values, name));
        return null;
    }
}
