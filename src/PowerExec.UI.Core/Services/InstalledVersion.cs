using Microsoft.Win32;
using PowerExec.Core.Updates;

namespace PowerExec.UI.Services;

/// <summary>Welche Version von Power-Exec ist über den Installer installiert (Eintrag in „Apps &amp; Features“)?</summary>
public static class InstalledVersion
{
    private static string UninstallKey => $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{UpdateChecker.InstallerAppId}_is1";

    /// <summary>Installierte Version (z. B. „0.19.5“) oder null, wenn nicht installiert.</summary>
    public static string? Read()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = hive.OpenSubKey(UninstallKey);
                if (key?.GetValue("DisplayVersion") is string v && v.Length > 0) return v;
            }
        }
        catch
        {
            // Kein Zugriff auf die Registry: wie „nicht installiert“
        }
        return null;
    }
}
