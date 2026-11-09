using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;

namespace PowerExec.Services;

/// <summary>
/// Geheime Variablenwerte verschlüsseln: unter Windows mit dem Datenschutz des angemeldeten Benutzers (DPAPI) – nur dieser
/// Benutzer auf diesem PC kann den Wert lesen. Andere Systeme (Tests) speichern Base64.
/// </summary>
public static class SecretProtector
{
    private const string DpapiPrefix = "dpapi:";
    private const string PlainPrefix = "b64:";

    public static string Protect(string plain)
    {
        if (plain.Length == 0) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(plain);
        if (OperatingSystem.IsWindows()) return DpapiPrefix + Convert.ToBase64String(Dpapi(bytes, protect: true));
        return PlainPrefix + Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string stored)
    {
        if (stored.Length == 0) return string.Empty;
        if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Geheime Werte lassen sich nur unter Windows lesen.");
            return Encoding.UTF8.GetString(Dpapi(Convert.FromBase64String(stored[DpapiPrefix.Length..]), protect: false));
        }
        if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal)) return Encoding.UTF8.GetString(Convert.FromBase64String(stored[PlainPrefix.Length..]));
        return stored;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    private static byte[] Dpapi(byte[] data, bool protect)
    {
        var input = new DataBlob { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        var output = new DataBlob();
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            const int uiForbidden = 0x1;
            bool ok = protect
                ? CryptProtectData(ref input, "Power-Exec", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, uiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, uiForbidden, ref output);
            if (!ok) throw new InvalidOperationException("Der geheime Wert ließ sich nicht " + (protect ? "verschlüsseln" : "entschlüsseln") +
                                                         $" (Fehler {Marshal.GetLastWin32Error()}). Bitte neu eingeben.");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
}

/// <summary>Führt den PowerShell-Befehl einer Variable unsichtbar aus und liefert die Ausgabe.</summary>
public static class VariableCommandRunner
{
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

    public static string Run(string command, PsEngine engine)
    {
        if (command.Trim().Length == 0) return string.Empty;
        // Ausgabe als UTF-8, Fehler beenden den Befehl (damit sie hier ankommen)
        var script = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ErrorActionPreference = 'Stop'; " +
                     "$__r = & { " + command + " }; ($__r | Out-String).TrimEnd()";
        var psi = new ProcessStartInfo(CommandBuilder.EngineExe(Launcher.EffectiveEngine(engine)),
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + CommandBuilder.Encode(script))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell ließ sich nicht starten.");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Der Befehl hat nach {Timeout.TotalSeconds:0} s nicht geantwortet.");
        }
        var error = CleanError(stderr.Result);
        if (p.ExitCode != 0 || error.Length > 0 && stdout.Result.Trim().Length == 0)
            throw new InvalidOperationException(error.Length > 0 ? error : $"PowerShell endete mit Code {p.ExitCode}.");
        return stdout.Result.Trim();
    }

    /// <summary>CLIXML-Hülle und Positionsangaben aus der Fehlerausgabe entfernen – nur die Meldung bleibt.</summary>
    private static string CleanError(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("#< CLIXML", StringComparison.Ordinal))
        {
            var msgs = System.Text.RegularExpressions.Regex.Matches(t, "<S S=\"Error\">(.*?)</S>").Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Replace("_x000D__x000A_", " ").Trim());
            t = string.Join(" ", msgs);
        }
        var firstLine = t.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? string.Empty;
        return firstLine;
    }
}
