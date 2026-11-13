using PowerExec.Core.Localization;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Win32;
using PowerExec.Core.Updates;

namespace PowerExec.UI.Services;

/// <summary>Sucht nach Updates in den GitHub Releases und installiert sie nach Bestätigung.</summary>
public sealed class UpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static Version CurrentVersion
    {
        get
        {
            // Version der laufenden App-EXE; alle Projekte erhalten sie aus Directory.Build.props
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (info != null && UpdateChecker.TryParseVersion(info, out var v)) return v;
            return UpdateChecker.Normalize(asm.GetName().Version ?? new Version(0, 0, 0));
        }
    }

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    public DateTime? LastCheck { get; private set; }
    public ReleaseInfo? Available { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    /// Wurde die laufende EXE über den Installer installiert? Dann wird auch über den Installer aktualisiert,
    /// damit Version und Deinstallationseintrag in „Apps &amp; Features“ stimmen.
    /// </summary>
    public static bool IsInstalledViaSetup
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{UpdateChecker.InstallerAppId}_is1");
                if (key?.GetValue("InstallLocation") is not string location || Environment.ProcessPath is not { } exe) return false;
                var installDir = Path.GetFullPath(location).TrimEnd('\\');
                var exeDir = Path.GetFullPath(Path.GetDirectoryName(exe)!).TrimEnd('\\');
                return string.Equals(installDir, exeDir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<ReleaseInfo?> CheckAsync(bool includePrereleases)
    {
        try
        {
            LastError = null;
            Available = await UpdateChecker.CheckAsync(CurrentVersion, includePrereleases, Http);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Available = null;
        }
        LastCheck = DateTime.Now;
        return Available;
    }

    /// <summary>
    /// Lädt die neue Version herunter, prüft die Prüfsumme und startet ein Hilfsscript, das nach dem Beenden
    /// von Power-Exec den Installer (still) ausführt bzw. die EXE ersetzt und Power-Exec neu startet.
    /// </summary>
    /// <returns>null bei Erfolg (die App muss sich danach beenden), sonst eine Fehlermeldung.</returns>
    public async Task<string?> DownloadAndPrepareAsync(ReleaseInfo release, IProgress<double>? progress = null)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe) || !currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return L.T("The path of the running application couldn't be determined.");

        bool useSetup = release.SetupUrl != null && IsInstalledViaSetup;
        var url = useSetup ? release.SetupUrl : release.ExeUrl;
        var checksumUrl = useSetup ? release.SetupChecksumUrl : release.ChecksumUrl;
        if (url == null)
            return L.T("The release contains no suitable file (PowerExec.exe or PowerExec-Setup.exe).");

        var targetDir = Path.GetDirectoryName(currentExe)!;
        if (!useSetup && !CanWrite(targetDir))
            return L.F("No write permission in {0}. Install Power-Exec with the installer or download the new version manually.", targetDir);

        var tempDir = Path.Combine(Path.GetTempPath(), "PowerExec-Update");
        Directory.CreateDirectory(tempDir);
        var download = Path.Combine(tempDir, useSetup ? UpdateChecker.SetupAssetName : UpdateChecker.ExeAssetName);

        using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(download);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n));
                read += n;
                if (total > 0) progress?.Report((double)read / total);
            }
        }

        if (checksumUrl != null)
        {
            var expected = UpdateChecker.ParseChecksum(await Http.GetStringAsync(checksumUrl));
            string actual;
            await using (var fs = File.OpenRead(download))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
            if (expected == null || expected != actual)
            {
                File.Delete(download);
                return L.T("The checksum of the downloaded file doesn't match. The update was discarded.");
            }
        }

        var pid = Environment.ProcessId;
        var script = Path.Combine(tempDir, "update.cmd");
        var install = useSetup
            ? $"start \"\" /wait \"{download}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS\r\n" +
              $"del \"{download}\"\r\n"
            : $"move /y \"{download}\" \"{currentExe}\" >nul\r\n";
        File.WriteAllText(script,
            "@echo off\r\n:wait\r\n" +
            $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
            install +
            $"start \"\" \"{currentExe}\"\r\n" +
            "del \"%~f0\"\r\n");

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        })?.Dispose();
        return null;
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".pe-write-test");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
