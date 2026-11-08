using System.Diagnostics;
using PowerExec.Interop;

namespace PowerExec.Services;

/// <summary>Merkt sich das Fenster, das vor dem Öffnen des Panels aktiv war (für „Text direkt einfügen“).</summary>
public static class ForegroundTracker
{
    private static IntPtr _last;

    public static void Remember(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId) return; // eigene Fenster nicht
        _last = hwnd;
    }

    /// <summary>Den Fokus an das vorher aktive Fenster zurückgeben (das Panel wird nicht versteckt, sondern geparkt).</summary>
    public static void ReturnFocus()
    {
        if (_last != IntPtr.Zero && NativeMethods.IsWindow(_last)) NativeMethods.SetForegroundWindow(_last);
    }

    /// <summary>Vorher aktives Fenster nach vorn holen und Strg+V senden. false = kein Zielfenster.</summary>
    /// <remarks>Läuft das Zielfenster mit Adminrechten (Power-Exec nicht), blockiert Windows die Eingabe – dann bleibt der Text nur kopiert.</remarks>
    public static async Task<bool> PasteIntoPreviousAsync()
    {
        var target = _last;
        if (target == IntPtr.Zero || !NativeMethods.IsWindow(target)) return false;
        NativeMethods.SetForegroundWindow(target);
        await Task.Delay(180); // Panel einfahren lassen, Fokus übergeben
        if (NativeMethods.GetForegroundWindow() != target) return false;
        NativeMethods.SendCtrlV();
        return true;
    }
}
