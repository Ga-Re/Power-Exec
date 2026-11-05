using System.Text;
using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Execution;

/// <summary>Beschreibt einen lokalen Prozessstart (plattformunabhängig, wird in der App umgesetzt).</summary>
public sealed class LocalLaunchSpec
{
    public required string FileName { get; init; }
    public string Arguments { get; init; } = string.Empty;

    /// <summary>true: über die Shell starten (nötig für "runas" und für Dateien ohne eigene EXE).</summary>
    public bool UseShellExecute { get; init; }

    /// <summary>"runas" für Administrator, sonst null.</summary>
    public string? Verb { get; init; }

    public bool RequiresCredential { get; init; }

    public string? WorkingDirectory { get; init; }

    /// <summary>Fenster beim Start (nur über die Shell bzw. bei Programmen wirksam).</summary>
    public LaunchWindowState WindowState { get; set; }

    /// <summary>Lesbare Vorschau des Befehls (ohne Base64).</summary>
    public required string Preview { get; init; }
}

public static class CommandBuilder
{
    public static string EngineExe(PsEngine engine) =>
        engine == PsEngine.PowerShell7 ? "pwsh.exe" : "powershell.exe";

    /// <summary>Der PowerShell-Befehl, der ein Script mit Parametern aufruft.</summary>
    public static string ScriptInvocation(string scriptPath, IReadOnlyDictionary<string, object> parameters) =>
        "& " + ParameterValueConverter.Quote(scriptPath) + ParameterValueConverter.ToPowerShellArguments(parameters);

    /// <param name="doneFile">
    /// Bleibt das Fenster offen (<see cref="ExecutionRequest.NoExit"/>), schreibt das Script nach dem Ende „0“ (ok) bzw.
    /// den Exit-Code in diese Datei – so erkennt Power-Exec das Ende, obwohl der Prozess weiterläuft.
    /// </param>
    public static LocalLaunchSpec BuildLocal(ExecutionRequest r, string? doneFile = null)
    {
        var spec = BuildLocalCore(r, SupportsDoneFile(r) ? doneFile : null);
        spec.WindowState = r.Item.WindowState;
        return spec;
    }

    /// <summary>Fertig-Markierung nur für Scripte mit offen bleibendem Fenster, nicht als anderer Benutzer (kein Zugriff auf die Datei).</summary>
    public static bool SupportsDoneFile(ExecutionRequest r) =>
        r.NoExit && r.Mode != ExecutionMode.OtherUser && r.Item.Type is ItemType.PowerShell or ItemType.Batch;

    /// <summary>PowerShell: nach dem Script Ergebnis in die Datei schreiben ($LASTEXITCODE, sonst 1 bei Fehler, sonst 0).</summary>
    public static string DoneSuffix(string doneFile) =>
        "; $__peOk = $?; $__peCode = if ($LASTEXITCODE -is [int] -and $LASTEXITCODE -ne 0) { $LASTEXITCODE } elseif (-not $__peOk) { 1 } else { 0 }" +
        "; try { [System.IO.File]::WriteAllText(" + ParameterValueConverter.Quote(doneFile) + ", [string]$__peCode) } catch { }" +
        "; Remove-Variable __peOk, __peCode -ErrorAction SilentlyContinue";

    private static LocalLaunchSpec BuildLocalCore(ExecutionRequest r, string? doneFile)
    {
        var path = r.EffectivePath;
        var item = r.Item;
        // Eigene Einträge (URL, Befehl …) haben kein sinnvolles Verzeichnis.
        var dir = item.WorkingDirectory ?? (item.CustomKind is null or CustomEntryKind.File ? Path.GetDirectoryName(path) : null);
        // Andere Konten haben oft keinen Zugriff auf das Arbeitsverzeichnis (z. B. Netzfreigaben).
        var workDir = r.Mode == ExecutionMode.OtherUser ? null : dir;

        switch (item.Type)
        {
            case ItemType.PowerShell:
            {
                var exe = EngineExe(r.Engine);
                var flags = PowerShellFlags(r);
                var command = ScriptInvocation(path, r.Parameters);
                var args = flags + " -EncodedCommand " + Encode(doneFile != null ? command + DoneSuffix(doneFile) : command);
                return new LocalLaunchSpec
                {
                    FileName = exe,
                    Arguments = args.Trim(),
                    UseShellExecute = r.Mode == ExecutionMode.Administrator,
                    Verb = r.Mode == ExecutionMode.Administrator ? "runas" : null,
                    RequiresCredential = r.Mode == ExecutionMode.OtherUser,
                    WorkingDirectory = workDir,
                    Preview = $"{exe} {flags.Trim()} -Command {command}".Replace("  ", " ")
                };
            }
            case ItemType.Batch:
            {
                // cmd /k ""C:\Pfad mit Leerzeichen\x.cmd"" – die äußeren Anführungszeichen entfernt cmd selbst.
                var extra = r.ExtraArguments.Trim();
                var cmdArgs = (r.NoExit ? "/k " : "/c ") + "\"\"" + path + "\"" + (extra.Length > 0 ? " " + extra : string.Empty) + "\"";
                if (doneFile != null)
                {
                    // „call“, damit es nach dem Batch weitergeht; „if errorlevel“ wird erst zur Laufzeit ausgewertet
                    cmdArgs = "/k \"call \"" + path + "\"" + (extra.Length > 0 ? " " + extra : string.Empty) +
                              " & if errorlevel 1 (>\"" + doneFile + "\" echo 1) else (>\"" + doneFile + "\" echo 0)\"";
                }
                return new LocalLaunchSpec
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    UseShellExecute = r.Mode != ExecutionMode.OtherUser,
                    Verb = r.Mode == ExecutionMode.Administrator ? "runas" : null,
                    RequiresCredential = r.Mode == ExecutionMode.OtherUser,
                    WorkingDirectory = workDir,
                    Preview = "cmd.exe " + cmdArgs
                };
            }
            case ItemType.Executable:
                return Program(r, path, r.ExtraArguments.Trim(), workDir);
            case ItemType.ConsoleSnapIn:
                if (r.Mode == ExecutionMode.OtherUser)
                    return Program(r, "mmc.exe", Join(Quoted(path), r.ExtraArguments), workDir);
                return Shell(path, r.Mode == ExecutionMode.Administrator ? "runas" : null, workDir, r.ExtraArguments);
            case ItemType.Shortcut:
                if (r.Mode == ExecutionMode.OtherUser && item.Shortcut != null && item.Shortcut.TargetPath.Length > 0)
                {
                    var wd = string.IsNullOrWhiteSpace(item.Shortcut.WorkingDirectory) ? null : item.Shortcut.WorkingDirectory;
                    return Program(r, item.Shortcut.TargetPath, Join(item.Shortcut.Arguments, r.ExtraArguments), wd);
                }
                return Shell(path, r.Mode == ExecutionMode.Administrator ? "runas" : null, workDir, r.ExtraArguments);
            default:
                if (r.Mode == ExecutionMode.OtherUser)
                    return Program(r, "cmd.exe", Join("/c start \"\" " + Quoted(path), r.ExtraArguments), null);
                return Shell(path, r.Mode == ExecutionMode.Administrator ? "runas" : null, workDir, r.ExtraArguments);
        }
    }

    /// <summary>Lesbare Vorschau für einen Remote-Start (die Ausführung übernimmt der RemoteRunner).</summary>
    public static string RemotePreview(ExecutionRequest r)
    {
        var targets = r.Targets.Count > 0 ? string.Join(",", r.Targets) : "<Rechner>";
        var opts = (r.Engine == PsEngine.PowerShell7 ? " -ConfigurationName PowerShell.7" : string.Empty) +
                   (r.Mode == ExecutionMode.OtherUser ? " -Credential $cred" : string.Empty);
        var args = ParameterValueConverter.ToPowerShellArguments(r.Parameters);
        if (r.CopyFolder)
        {
            var dir = Path.GetDirectoryName(r.EffectivePath) ?? string.Empty;
            var file = Path.GetFileName(r.EffectivePath);
            return $"$s = New-PSSession -ComputerName {targets}{opts}\n" +
                   $"Copy-Item {ParameterValueConverter.Quote(dir + "\\*")} -ToSession $s -Destination $tmp -Recurse\n" +
                   $"Invoke-Command -Session $s {{ & \"$tmp\\{file}\"{args} }}\n" +
                   "Invoke-Command -Session $s { Remove-Item $tmp -Recurse -Force }";
        }
        return $"Invoke-Command -ComputerName {targets}{opts} -ScriptBlock {{ <Inhalt von {Path.GetFileName(r.EffectivePath)}> }}{(args.Length > 0 ? " # Parameter:" + args : string.Empty)}";
    }

    public static string Encode(string command) => Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

    private static string PowerShellFlags(ExecutionRequest r)
    {
        var sb = new StringBuilder();
        if (r.NoProfile) sb.Append(" -NoProfile");
        if (r.NoExit) sb.Append(" -NoExit");
        if (!string.IsNullOrWhiteSpace(r.ExecutionPolicy)) sb.Append(" -ExecutionPolicy ").Append(r.ExecutionPolicy);
        return sb.ToString();
    }

    private static LocalLaunchSpec Program(ExecutionRequest r, string exe, string args, string? workDir) => new()
    {
        FileName = exe,
        Arguments = args,
        UseShellExecute = r.Mode != ExecutionMode.OtherUser,
        Verb = r.Mode == ExecutionMode.Administrator ? "runas" : null,
        RequiresCredential = r.Mode == ExecutionMode.OtherUser,
        WorkingDirectory = workDir,
        Preview = (Quoted(exe) + " " + args).Trim()
    };

    private static LocalLaunchSpec Shell(string path, string? verb, string? workDir, string arguments) => new()
    {
        FileName = path,
        Arguments = arguments.Trim(),
        UseShellExecute = true,
        Verb = verb,
        WorkingDirectory = workDir,
        Preview = Join(Quoted(path), arguments)
    };

    private static string Join(string a, string b) => (a.Trim() + " " + b.Trim()).Trim();

    private static string Quoted(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;
}
