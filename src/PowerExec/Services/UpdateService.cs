using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using PowerExec.Core.Updates;

namespace PowerExec.Services;

/// <summary>Sucht nach Updates in den GitHub Releases und installiert sie nach Bestätigung.</summary>
public sealed class UpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static Version CurrentVersion
    {
        get
        {
            var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (info != null && UpdateChecker.TryParseVersion(info, out var v)) return v;
            return UpdateChecker.Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));
        }
    }

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    public DateTime? LastCheck { get; private set; }
    public ReleaseInfo? Available { get; private set; }
    public string? LastError { get; private set; }

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
    /// Lädt die neue Version herunter, prüft die Prüfsumme und ersetzt die laufende EXE über ein kleines Hilfsscript.
    /// </summary>
    /// <returns>null bei Erfolg (die App muss sich danach beenden), sonst eine Fehlermeldung.</returns>
    public async Task<string?> DownloadAndPrepareAsync(ReleaseInfo release, IProgress<double>? progress = null)
    {
        if (release.ExeUrl == null)
            return "Das Release enthält keine PowerExec.exe.";

        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe) || !currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return "Der Pfad der laufenden Anwendung konnte nicht ermittelt werden.";

        var targetDir = Path.GetDirectoryName(currentExe)!;
        if (!CanWrite(targetDir))
            return $"Keine Schreibrechte in {targetDir}. Lade die neue Version manuell herunter.";

        var tempDir = Path.Combine(Path.GetTempPath(), "PowerExec-Update");
        Directory.CreateDirectory(tempDir);
        var newExe = Path.Combine(tempDir, "PowerExec.exe");

        using (var resp = await Http.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(newExe);
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

        if (release.ChecksumUrl != null)
        {
            var expected = UpdateChecker.ParseChecksum(await Http.GetStringAsync(release.ChecksumUrl));
            await using var fs = File.OpenRead(newExe);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
            if (expected == null || expected != actual)
            {
                File.Delete(newExe);
                return "Die Prüfsumme der heruntergeladenen Datei stimmt nicht. Das Update wurde verworfen.";
            }
        }

        var pid = Environment.ProcessId;
        var script = Path.Combine(tempDir, "update.cmd");
        File.WriteAllText(script,
            "@echo off\r\n" +
            ":wait\r\n" +
            $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
            $"move /y \"{newExe}\" \"{currentExe}\" >nul\r\n" +
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
