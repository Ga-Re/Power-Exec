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

    public static LocalLaunchSpec BuildLocal(ExecutionRequest r)
    {
        var path = r.EffectivePath;
        var item = r.Item;
        var dir = Path.GetDirectoryName(path);
        // Andere Konten haben oft keinen Zugriff auf das Arbeitsverzeichnis (z. B. Netzfreigaben).
        var workDir = r.Mode == ExecutionMode.OtherUser ? null : dir;

        switch (item.Type)
        {
            case ItemType.PowerShell:
            {
                var exe = EngineExe(r.Engine);
                var flags = PowerShellFlags(r);
                var command = ScriptInvocation(path, r.Parameters);
                var args = flags + " -EncodedCommand " + Encode(command);
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
                var cmdArgs = (r.NoExit ? "/k " : "/c ") + "\"\"" + path + "\"\"";
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
                return Program(r, path, string.Empty, workDir);
            case ItemType.ConsoleSnapIn:
                if (r.Mode == ExecutionMode.OtherUser)
                    return Program(r, "mmc.exe", Quoted(path), workDir);
                return Shell(path, r.Mode == ExecutionMode.Administrator ? "runas" : null, workDir, Quoted(path));
            case ItemType.Shortcut:
                if (r.Mode == ExecutionMode.OtherUser && item.Shortcut != null && item.Shortcut.TargetPath.Length > 0)
                {
                    var wd = string.IsNullOrWhiteSpace(item.Shortcut.WorkingDirectory) ? null : item.Shortcut.WorkingDirectory;
                    return Program(r, item.Shortcut.TargetPath, item.Shortcut.Arguments, wd);
                }
                return Shell(path, r.Mode == ExecutionMode.Administrator ? "runas" : null, workDir, Quoted(path));
            default:
                if (r.Mode == ExecutionMode.OtherUser)
                    return Program(r, "cmd.exe", "/c start \"\" " + Quoted(path), null);
                return Shell(path, r.Mode == ExecutionMode.Administrator ? "runas" : null, workDir, Quoted(path));
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

    private static LocalLaunchSpec Shell(string path, string? verb, string? workDir, string preview) => new()
    {
        FileName = path,
        UseShellExecute = true,
        Verb = verb,
        WorkingDirectory = workDir,
        Preview = preview
    };

    private static string Quoted(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;
}
