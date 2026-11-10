using PowerExec.Core.Localization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;

namespace PowerExec.Services;

/// <summary>Führt den Verbindungstest (WinRM, Anmeldung, PowerShell 7) für mehrere Rechner aus.</summary>
public static class RemoteTestService
{
    public static async Task<IReadOnlyList<RemoteTestResult>> RunAsync(IReadOnlyList<string> targets, AppSettings settings, RunCredential? credential)
    {
        var psi = new ProcessStartInfo(RemoteSession.WindowsPowerShellPath, RemoteTester.ProcessArguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        var results = new List<RemoteTestResult>();
        try
        {
            using var p = Process.Start(psi)!;
            var stdin = p.StandardInput;
            stdin.WriteLine(RemoteTester.BuildConfigLine(targets, settings, credential != null));
            if (credential != null)
            {
                var account = credential.Domain is null or "." ? credential.UserName : credential.Domain + "\\" + credential.UserName;
                stdin.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(account)));
                WritePassword(stdin, credential.Password);
            }
            stdin.Close();

            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync()) != null)
            {
                var r = RemoteTester.ParseLine(line);
                if (r != null) results.Add(r);
            }
            await p.WaitForExitAsync();
            AppLog.Info(L.T("Verbindungstest: ") + string.Join("; ", results.Select(r => $"{r.Computer}: {r.Summary}")));
        }
        catch (Exception ex)
        {
            AppLog.Error(L.T("Verbindungstest fehlgeschlagen"), ex);
            foreach (var t in targets.Where(t => results.All(r => !r.Computer.Equals(t, StringComparison.OrdinalIgnoreCase))))
                results.Add(new RemoteTestResult { Computer = t, Error = L.T("Test konnte nicht gestartet werden: ") + ex.Message });
        }
        finally
        {
            credential?.Dispose();
        }
        return results;
    }

    private static void WritePassword(StreamWriter writer, System.Security.SecureString secure)
    {
        IntPtr ptr = IntPtr.Zero;
        char[]? chars = null;
        byte[]? bytes = null;
        char[]? b64 = null;
        try
        {
            ptr = Marshal.SecureStringToGlobalAllocUnicode(secure);
            chars = new char[secure.Length];
            Marshal.Copy(ptr, chars, 0, chars.Length);
            bytes = Encoding.UTF8.GetBytes(chars);
            b64 = new char[((bytes.Length + 2) / 3) * 4];
            Convert.TryToBase64Chars(bytes, b64, out var written);
            writer.Write(b64, 0, written);
            writer.WriteLine();
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            if (chars != null) Array.Clear(chars);
            if (bytes != null) Array.Clear(bytes);
            if (b64 != null) Array.Clear(b64);
        }
    }
}
