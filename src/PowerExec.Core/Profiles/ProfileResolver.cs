using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Profiles;

/// <summary>Ermittelt die Startprofile eines Eintrags und wertet sie aus.</summary>
public static class ProfileResolver
{
    /// <summary>Ausführungsart eines Profils: Administrator, anderer Benutzer oder aktuelles Konto.</summary>
    public static Execution.ExecutionMode ModeOf(LaunchProfile p) =>
        p.RunAsAdmin ? Execution.ExecutionMode.Administrator : p.RunAsOtherUser ? Execution.ExecutionMode.OtherUser : Execution.ExecutionMode.CurrentUser;

    /// <summary>
    /// Profile des Eintrags. Ohne gespeicherte Konfiguration gibt es ein automatisches Standardprofil,
    /// das dem bisherigen Verhalten entspricht (mit Parametern: Dialog, sonst direkter Start).
    /// </summary>
    public static IReadOnlyList<LaunchProfile> GetProfiles(AppSettings settings, LaunchItem item)
    {
        var entry = settings.FindEntry(item.FullPath);
        if (entry != null && entry.Profiles.Count > 0) return entry.Profiles;
        return new[] { CreateImplicit(settings, item) };
    }

    /// <summary>Beim Start nach dem Profil fragen? Nur bei mehr als einem Profil; Eintrag geht vor der globalen Einstellung.</summary>
    public static bool AsksProfile(AppSettings settings, LaunchItem item) =>
        (settings.FindEntry(item.FullPath)?.AskProfile ?? settings.AskProfile) && GetProfiles(settings, item).Count > 1;

    public static LaunchProfile GetDefault(AppSettings settings, LaunchItem item)
    {
        var profiles = GetProfiles(settings, item);
        return profiles.FirstOrDefault(p => p.IsDefault) ?? profiles[0];
    }

    public static LaunchProfile CreateImplicit(AppSettings settings, LaunchItem item) => new()
    {
        Name = "Standard",
        IsDefault = true,
        IsImplicit = true,
        Engine = settings.DefaultEngine,
        NoExit = settings.NoExit,
        NoProfile = settings.NoProfile,
        Target = ProfileTarget.Local,
        Dialog = item.IsPowerShell && item.HasParameters ? DialogMode.Always : DialogMode.WhenNeeded
    };

    /// <summary>Ein neues, leeres Profil für den Profil-Dialog.</summary>
    public static LaunchProfile CreateNew(AppSettings settings, LaunchItem item, string name)
    {
        var p = CreateImplicit(settings, item);
        p.IsImplicit = false;
        p.IsDefault = false;
        p.Name = name;
        foreach (var param in item.Parameters)
        {
            if (param.Kind == ParameterKind.Secure) continue;
            p.Parameters[param.Name] = new ParameterSetting
            {
                Mode = param.DefaultValueLiteral != null ? ParameterMode.Preset : ParameterMode.Ask,
                Value = param.DefaultValueLiteral ?? string.Empty
            };
        }
        return p;
    }

    public static ParameterSetting SettingFor(LaunchProfile profile, ScriptParameter parameter)
    {
        if (parameter.Kind == ParameterKind.Secure) return new ParameterSetting { Mode = ParameterMode.Ask };
        if (profile.Parameters.TryGetValue(parameter.Name, out var s)) return s;
        // Ohne Einstellung: Standardwert aus dem Script vorbelegen
        return parameter.DefaultValueLiteral != null
            ? new ParameterSetting { Mode = ParameterMode.Preset, Value = parameter.DefaultValueLiteral }
            : new ParameterSetting { Mode = ParameterMode.Ask };
    }

    /// <summary>Werte, mit denen gestartet bzw. der Dialog vorbelegt wird (Fest und Vorbelegt). Fragen = null.</summary>
    public static Dictionary<string, string?> InitialValues(LaunchProfile profile, LaunchItem item)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in item.Parameters)
        {
            var s = SettingFor(profile, p);
            values[p.Name] = s.Mode == ParameterMode.Ask ? null : s.Value;
        }
        return values;
    }

    public static bool IsFixed(LaunchProfile profile, ScriptParameter parameter) =>
        parameter.Kind != ParameterKind.Secure && SettingFor(profile, parameter).Mode == ParameterMode.Fixed;

    /// <summary>Muss der Dialog vor dem Start erscheinen?</summary>
    public static bool NeedsDialog(LaunchProfile profile, LaunchItem item)
    {
        if (!item.IsPowerShell) return false; // Programme starten immer direkt
        if (profile.Dialog == DialogMode.Always) return true;
        if (profile.Target == ProfileTarget.Ask) return true;
        if (profile.Target == ProfileTarget.Fixed && profile.Machines.Count == 0) return true;
        // Pflichtfelder ohne Wert erzwingen den Dialog (Secure-Parameter fragt das Script selbst)
        foreach (var p in item.Parameters)
        {
            if (!p.IsMandatory || p.Kind is ParameterKind.Secure or ParameterKind.Switch) continue;
            var s = SettingFor(profile, p);
            if (s.Mode == ParameterMode.Ask || string.IsNullOrWhiteSpace(s.Value)) return true;
        }
        return false;
    }

    /// <summary>Kurzbeschreibung für Listen und Menüs, z. B. „Dieser PC · Dialog wenn nötig · Admin“.</summary>
    public static string Summary(LaunchProfile profile, LaunchItem item)
    {
        var parts = new List<string>();
        if (item.IsPowerShell)
        {
            parts.Add(profile.Target switch
            {
                ProfileTarget.Fixed => profile.Machines.Count == 1 ? profile.Machines[0] : $"{profile.Machines.Count} Rechner",
                ProfileTarget.Ask => "Rechner wählen",
                _ => "Dieser PC"
            });
            parts.Add(NeedsDialog(profile, item)
                ? (profile.Dialog == DialogMode.Always ? "mit Dialog" : "Dialog wenn nötig")
                : "startet direkt");
        }
        else
        {
            parts.Add(string.IsNullOrWhiteSpace(profile.Arguments) ? "ohne Argumente" : profile.Arguments.Trim());
        }
        if (profile.RunAsAdmin) parts.Add("Admin");
        else if (profile.RunAsOtherUser) parts.Add("anderer Benutzer");
        return string.Join(" · ", parts);
    }
}
