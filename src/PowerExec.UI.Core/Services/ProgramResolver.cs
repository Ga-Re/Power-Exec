namespace PowerExec.UI.Services;

/// <summary>Findet Programme wie die Windows-Shell: absoluter Pfad, System32, Windows, PATH, WindowsApps.</summary>
public static class ProgramResolver
{
    /// <summary>Programmnamen über Pfad, System32 und PATH auflösen.</summary>
    public static string? Resolve(string program)
    {
        program = program.Trim().Trim('"');
        if (program.Length == 0) return null;
        try
        {
            if (Path.IsPathRooted(program)) return File.Exists(program) ? program : null;
            var names = Path.HasExtension(program) ? new[] { program } : new[] { program + ".exe", program + ".cmd", program + ".bat", program + ".msc" };
            var dirs = new List<string> { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            dirs.Add(Path.Combine(local, "Microsoft", "WindowsApps"));
            foreach (var d in dirs)
                foreach (var n in names)
                {
                    var p = Path.Combine(d, n);
                    if (File.Exists(p)) return p;
                }
        }
        catch { }
        return null;
    }
}
