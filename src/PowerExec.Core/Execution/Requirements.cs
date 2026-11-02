using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Execution;

/// <summary>Wertet #Requires eines Scripts für den Start aus.</summary>
public static class Requirements
{
    /// <summary>true, wenn #Requires -Version 6 oder höher verlangt (nur mit pwsh.exe lauffähig).</summary>
    public static bool NeedsPowerShell7(LaunchItem item) =>
        item.IsPowerShell && Version.TryParse(Normalize(item.RequiredPsVersion), out var v) && v.Major >= 6;

    /// <summary>Engine nach #Requires: verlangt das Script 6+, wird PowerShell 7 verwendet.</summary>
    public static PsEngine EngineFor(LaunchItem item, PsEngine wanted) =>
        NeedsPowerShell7(item) ? PsEngine.PowerShell7 : wanted;

    /// <summary>Lokaler Start: #Requires -RunAsAdministrator hebt „aktuelles Konto“ auf „Administrator“ an.</summary>
    public static ExecutionMode ModeFor(LaunchItem item, ExecutionMode wanted, bool remote) =>
        !remote && item.RequiresAdmin && wanted == ExecutionMode.CurrentUser ? ExecutionMode.Administrator : wanted;

    private static string Normalize(string v)
    {
        v = (v ?? string.Empty).Trim();
        return v.Length > 0 && !v.Contains('.') ? v + ".0" : v;
    }
}
