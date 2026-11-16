using System.Text.Json;
using PowerExec.Core.Config;

namespace PowerExec.Core.Profiles;

/// <summary>
/// Vererbung zwischen Startprofilen: Das Standardprofil (★) ist die Grundlage, jedes andere Profil legt nur einzelne
/// Werte selbst fest (<see cref="LaunchProfile.Own"/>) und übernimmt alles andere beim Start vom Standard. Ändert man
/// den Standard, wirkt das in allen Profilen, die den Wert nicht selbst festlegen.
/// </summary>
public static class ProfileInheritance
{
    /// <summary>Ziel bzw. Code des Eintrags.</summary>
    public const string Target = "Target";
    /// <summary>Ausführen als, wo (Rechner) und PowerShell-Version.</summary>
    public const string RunAs = "RunAs";
    /// <summary>Startoptionen (Browser, Anzeige …), Dialog, Konsole, Rückfrage.</summary>
    public const string Window = "Window";
    /// <summary>Zusätzliche Argumente bei Programmen.</summary>
    public const string Arguments = "Arguments";
    /// <summary>Ein Script-Parameter: „Param:Name“.</summary>
    public const string ParamPrefix = "Param:";
    /// <summary>Eine Variable bzw. Abfrage: „Var:$Name“ bzw. „Var:Name“.</summary>
    public const string VarPrefix = "Var:";

    /// <summary>Feste Gruppen (ohne Parameter und Variablen).</summary>
    public static readonly IReadOnlyList<string> Groups = new[] { Target, RunAs, Window, Arguments };

    /// <summary>Legt das Profil den Wert selbst fest? Alte Profile ohne <see cref="LaunchProfile.Own"/> legen alles fest.</summary>
    public static bool Owns(LaunchProfile p, string key) =>
        p.Own == null || p.Own.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Alle Schlüssel, die für die beiden Profile eine Rolle spielen.</summary>
    public static IReadOnlyList<string> Keys(LaunchProfile a, LaunchProfile b) =>
        Groups
            .Concat(a.Parameters.Keys.Concat(b.Parameters.Keys).Select(n => ParamPrefix + n))
            .Concat(a.Variables.Keys.Concat(b.Variables.Keys).Select(n => VarPrefix + n))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Vergleichswert eines Schlüssels (gleich = gleicher Wert).</summary>
    public static string Snapshot(LaunchProfile p, string key)
    {
        if (key.StartsWith(ParamPrefix, StringComparison.OrdinalIgnoreCase))
            return p.Parameters.TryGetValue(key[ParamPrefix.Length..], out var s) ? $"{s.Mode}|{s.Value}" : "–";
        if (key.StartsWith(VarPrefix, StringComparison.OrdinalIgnoreCase))
            return p.Variables.TryGetValue(key[VarPrefix.Length..], out var v) ? v.Signature : "–";
        return key switch
        {
            Target => $"{p.TargetText}|{p.Code}",
            RunAs => $"{p.RunAsAdmin}|{p.RunAsOtherUser}|{p.Target}|{string.Join(",", p.Machines)}|{p.Engine}",
            Window => $"{(p.Options == null ? "" : JsonSerializer.Serialize(p.Options))}|{p.Dialog}|{p.NoExit}|{p.NoProfile}|{p.ConfirmBeforeRun}",
            Arguments => p.Arguments,
            _ => string.Empty
        };
    }

    /// <summary>Schlüssel, in denen sich das Profil vom Standard unterscheidet (Umzug alter Profile ins Vererbungsmodell).</summary>
    public static List<string> Differences(LaunchProfile standard, LaunchProfile p) =>
        Keys(standard, p).Where(k => Snapshot(standard, k) != Snapshot(p, k)).ToList();

    /// <summary>Den Wert eines Schlüssels von <paramref name="from"/> nach <paramref name="to"/> übernehmen.</summary>
    public static void Copy(LaunchProfile from, LaunchProfile to, string key)
    {
        if (key.StartsWith(ParamPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = key[ParamPrefix.Length..];
            if (from.Parameters.TryGetValue(name, out var s)) to.Parameters[name] = s.Clone();
            else to.Parameters.Remove(name);
            return;
        }
        if (key.StartsWith(VarPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = key[VarPrefix.Length..];
            if (from.Variables.TryGetValue(name, out var v)) to.Variables[name] = v.Clone();
            else to.Variables.Remove(name);
            return;
        }
        switch (key)
        {
            case Target:
                to.TargetText = from.TargetText;
                to.Code = from.Code;
                break;
            case RunAs:
                to.RunAsAdmin = from.RunAsAdmin;
                to.RunAsOtherUser = from.RunAsOtherUser;
                to.Target = from.Target;
                to.Machines = from.Machines.ToList();
                to.Engine = from.Engine;
                break;
            case Window:
                to.Options = from.Options?.Clone();
                to.Dialog = from.Dialog;
                to.NoExit = from.NoExit;
                to.NoProfile = from.NoProfile;
                to.ConfirmBeforeRun = from.ConfirmBeforeRun;
                break;
            case Arguments:
                to.Arguments = from.Arguments;
                break;
        }
    }

    /// <summary>
    /// Wirksames Profil: alles vom Standard, darüber die eigenen Werte. Das Standardprofil und alte Profile ohne
    /// Vererbung kommen unverändert zurück.
    /// </summary>
    public static LaunchProfile Effective(LaunchProfile standard, LaunchProfile p)
    {
        if (ReferenceEquals(standard, p) || p.Own == null) return p;
        var r = standard.Clone();
        r.Name = p.Name;
        r.IsDefault = p.IsDefault;
        r.IsImplicit = p.IsImplicit;
        r.Own = p.Own.ToList();
        r.OwnBase = p.OwnBase;
        foreach (var key in p.Own) Copy(p, r, key);
        return r;
    }

    /// <summary>Alle Profile eines Eintrags mit ihren wirksamen Werten (für den Start).</summary>
    public static IReadOnlyList<LaunchProfile> Resolve(IReadOnlyList<LaunchProfile> profiles)
    {
        if (profiles.Count == 0) return profiles;
        var standard = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles[0];
        return profiles.Select(p => ProfileVariables.ApplyToValues(Effective(standard, p))).ToList();
    }

    /// <summary>Eigene Werte, bei denen sich der Standard seit dem Anpassen geändert hat.</summary>
    public static IReadOnlyList<string> ChangedInStandard(LaunchProfile standard, LaunchProfile p) =>
        p.Own == null || p.OwnBase == null || ReferenceEquals(standard, p)
            ? Array.Empty<string>()
            : p.Own.Where(k => p.OwnBase.TryGetValue(k, out var b) && b != Snapshot(standard, k)).ToList();
}
