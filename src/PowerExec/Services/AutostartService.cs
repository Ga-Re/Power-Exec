using Microsoft.Win32;

namespace PowerExec.Services;

/// <summary>Autostart über HKCU\...\Run (keine Adminrechte nötig).</summary>
public static class AutostartService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Power-Exec";

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
