namespace PowerExec.Core.Metadata;

/// <summary>Prüft, ob PowerShell-Module lokal vorhanden sind (Suche in den Modulpfaden).</summary>
public static class ModuleLocator
{
    public static IReadOnlyList<string> DefaultSearchPaths()
    {
        var paths = new List<string>();
        var env = Environment.GetEnvironmentVariable("PSModulePath");
        if (!string.IsNullOrEmpty(env)) paths.AddRange(env.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        if (OperatingSystem.IsWindows())
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            paths.Add(Path.Combine(docs, "WindowsPowerShell", "Modules"));
            paths.Add(Path.Combine(docs, "PowerShell", "Modules"));
            paths.Add(Path.Combine(pf, "WindowsPowerShell", "Modules"));
            paths.Add(Path.Combine(pf, "PowerShell", "Modules"));
            paths.Add(Path.Combine(pf, "PowerShell", "7", "Modules"));
            paths.Add(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "Modules"));
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Module, die in keinem Modulpfad (und nicht im Scriptordner) gefunden wurden.</summary>
    public static IReadOnlyList<string> FindMissing(IEnumerable<string> modules, IEnumerable<string> searchPaths, string? scriptDirectory = null)
    {
        var roots = searchPaths.ToList();
        if (scriptDirectory != null)
        {
            roots.Add(scriptDirectory);
            roots.Add(Path.Combine(scriptDirectory, "Modules"));
        }
        var missing = new List<string>();
        foreach (var m in modules)
        {
            bool found = roots.Any(r =>
            {
                try { return Directory.Exists(Path.Combine(r, m)); } catch { return false; }
            });
            if (!found) missing.Add(m);
        }
        return missing;
    }
}
