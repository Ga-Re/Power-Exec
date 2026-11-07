using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;

namespace PowerExec.Services;

/// <summary>Startet Scripte und Programme lokal.</summary>
public sealed class Launcher
{
    private readonly Func<AppSettings> _settings;
    private readonly IDialogs _dialogs;
    private readonly Func<object?, string, bool, RunCredential?> _askCredential;

    /// <param name="askCredential">Anmeldedaten abfragen (Fenster, Ziel, remote); null = abgebrochen.</param>
    public Launcher(Func<AppSettings> settings, IDialogs dialogs, Func<object?, string, bool, RunCredential?> askCredential)
    {
        _settings = settings;
        _dialogs = dialogs;
        _askCredential = askCredential;
    }

    public static bool IsPwshInstalled
    {
        get
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (File.Exists(Path.Combine(pf, "PowerShell", "7", "pwsh.exe"))) return true;
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            return path.Split(';', StringSplitOptions.RemoveEmptyEntries)
                       .Any(p => { try { return File.Exists(Path.Combine(p.Trim(), "pwsh.exe")); } catch { return false; } });
        }
    }

    public static PsEngine EffectiveEngine(PsEngine wanted) =>
        wanted == PsEngine.PowerShell7 && !IsPwshInstalled ? PsEngine.WindowsPowerShell : wanted;

    /// <summary>Erstellt eine Anfrage mit den Standardwerten aus den Einstellungen.</summary>
    public ExecutionRequest DefaultRequest(LaunchItem item, ExecutionMode mode)
    {
        var s = _settings();
        return new ExecutionRequest
        {
            Item = item,
            Mode = mode,
            Engine = EffectiveEngine(s.DefaultEngine),
            NoExit = s.NoExit,
            NoProfile = s.NoProfile,
            ExecutionPolicy = s.ExecutionPolicy,
            ResolvedPath = PathResolver.ToUnc(item.FullPath)
        };
    }

    /// <returns>true, wenn der Prozess gestartet wurde.</returns>
    /// <param name="started">Erhält den gestarteten Prozess (dann gibt der Aufrufer ihn frei), z. B. für die Aktivitäten-Leiste.</param>
    /// <summary>Fertig-Markierung je gestartetem Prozess (siehe <see cref="CommandBuilder.BuildLocal"/>).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Process, string> _doneFiles = new();

    /// <summary>Datei, in die das Script nach seinem Ende das Ergebnis schreibt (Fenster bleibt offen), sonst null.</summary>
    public static string? DoneFileOf(Process process) => _doneFiles.TryGetValue(process, out var f) ? f : null;

    private static string NewDoneFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PowerExec");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"done-{Guid.NewGuid():N}.txt");
    }

    public bool StartLocal(ExecutionRequest request, object? owner, Action<Process>? started = null)
    {
        string? doneFile = null;
        if (started != null && CommandBuilder.SupportsDoneFile(request))
        {
            try { doneFile = NewDoneFile(); }
            catch (Exception ex) { AppLog.Info($"Fertig-Markierung nicht möglich: {ex.Message}"); }
        }
        var spec = CommandBuilder.BuildLocal(request, doneFile);
        RunCredential? cred = null;
        if (spec.RequiresCredential)
        {
            cred = _askCredential(owner, request.Item.DisplayName, false);
            if (cred == null) return false;
        }

        var psi = new ProcessStartInfo(spec.FileName, spec.Arguments)
        {
            UseShellExecute = spec.UseShellExecute,
            WorkingDirectory = spec.WorkingDirectory ?? string.Empty
        };
        if (spec.Verb != null) psi.Verb = spec.Verb;
        psi.WindowStyle = spec.WindowState switch
        {
            Core.Config.LaunchWindowState.Maximized => ProcessWindowStyle.Maximized,
            Core.Config.LaunchWindowState.Minimized => ProcessWindowStyle.Minimized,
            _ => ProcessWindowStyle.Normal
        };
        if (cred != null)
        {
            psi.UserName = cred.UserName;
            psi.Domain = cred.Domain;
            psi.Password = cred.Password;
            psi.LoadUserProfile = true;
            psi.WorkingDirectory = Environment.SystemDirectory;
        }

        try
        {
            var process = Process.Start(psi);
            if (process != null && doneFile != null) _doneFiles.AddOrUpdate(process, doneFile);
            if (process != null && started != null) started(process);
            else process?.Dispose();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // UAC-Abfrage abgebrochen
        }
        catch (Exception ex)
        {
            ShowStartError(owner, request, spec, cred, ex);
            return false;
        }
        finally
        {
            cred?.Dispose();
        }
    }

    private void ShowStartError(object? owner, ExecutionRequest r, LocalLaunchSpec spec, RunCredential? cred, Exception ex)
    {
        var path = r.EffectivePath;
        string hint = ex is Win32Exception w ? w.NativeErrorCode switch
        {
            2 when spec.FileName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)
                => "PowerShell 7 (pwsh.exe) ist nicht installiert. Wähle im Dialog Windows PowerShell 5.1.",
            2 or 3 => $"Die Datei wurde nicht gefunden:\n{spec.FileName}",
            1326 => "Benutzername oder Kennwort ist falsch.",
            1327 or 1385 => "Das Konto darf sich auf diesem Rechner nicht anmelden.",
            5 when cred != null && PathResolver.IsNetworkPath(path)
                => $"Das Konto {cred.Account} hat keinen Zugriff auf {PathResolver.ServerShare(path)}.",
            5 => "Zugriff verweigert.",
            267 => "Das Arbeitsverzeichnis ist für dieses Konto nicht erreichbar.",
            _ => ex.Message
        } : ex.Message;

        if (cred != null && PathResolver.IsNetworkPath(path) && !hint.Contains(PathResolver.ServerShare(path)))
            hint += $"\n\nHinweis: Das Script liegt auf {PathResolver.ServerShare(path)}. Prüfe, ob {cred.Account} dort Leserechte hat.";

        var title = $"{r.Item.DisplayName} konnte nicht gestartet werden";
        _dialogs.Warn(hint, title, owner);
    }

    public void OpenInEditor(LaunchItem item)
    {
        var s = _settings();
        var path = item.FullPath;
        try
        {
            switch (s.Editor)
            {
                case EditorKind.VSCode:
                    Process.Start(new ProcessStartInfo("code", $"\"{path}\"") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden })?.Dispose();
                    break;
                case EditorKind.Ise:
                    Process.Start(new ProcessStartInfo("powershell_ise.exe", $"-File \"{path}\"") { UseShellExecute = true })?.Dispose();
                    break;
                case EditorKind.Custom when !string.IsNullOrWhiteSpace(s.EditorPath):
                    Process.Start(new ProcessStartInfo(s.EditorPath, $"\"{path}\"") { UseShellExecute = true })?.Dispose();
                    break;
                default:
                    Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
                    break;
            }
        }
        catch (Exception ex)
        {
            _dialogs.Warn($"Der Editor konnte nicht gestartet werden.\n\n{ex.Message}\n\nDen Editor stellst du in den Einstellungen unter „Ausführung“ ein.", "Power-Exec");
        }
    }

    /// <summary>Im Explorer zeigen; Fehler meldet <paramref name="dialogs"/> (ohne: still).</summary>
    public static void OpenLocation(string path, bool isFolder = false, IDialogs? dialogs = null)
    {
        try
        {
            var args = isFolder ? $"\"{path}\"" : $"/select,\"{path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            dialogs?.Warn(ex.Message, "Power-Exec");
        }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); } catch { }
    }
}
