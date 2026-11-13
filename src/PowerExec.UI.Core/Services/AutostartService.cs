using Microsoft.Win32;

namespace PowerExec.UI.Services;

/// <summary>Autostart über HKCU\...\Run (keine Adminrechte nötig).</summary>
public static class AutostartService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Power-Exec";

    /// <summary>Ist ein Autostart-Eintrag vorhanden (z. B. vom Installer angelegt)?</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(KeyPath);
            if (enabled && Environment.ProcessPath is { } exe)
                key.SetValue(ValueName, $"\"{exe}\"");
            else if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Ohne Zugriff auf die Registry bleibt der Autostart unverändert.
        }
    }
}
