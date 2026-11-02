using PowerExec.Core.Config;

namespace PowerExec.Core.Scanning;

public sealed record ScanResult(FolderConfig Folder, IReadOnlyList<string> Files, int ExcludedCount, string? Error);

/// <summary>Durchsucht einen konfigurierten Ordner nach passenden Dateien.</summary>
public static class FolderScanner
{
    public static ScanResult Scan(FolderConfig folder, AppSettings settings)
    {
        var root = folder.Path.Trim();
        if (root.Length == 0) return new ScanResult(folder, Array.Empty<string>(), 0, "Kein Pfad angegeben.");

        try
        {
            if (!Directory.Exists(root))
                return new ScanResult(folder, Array.Empty<string>(), 0, "Ordner nicht gefunden oder nicht erreichbar.");

            var extensions = new HashSet<string>(
                settings.ExtensionsFor(folder).Select(e => e.StartsWith('.') ? e : "." + e),
                StringComparer.OrdinalIgnoreCase);
            var matcher = new ExclusionMatcher(folder.Excludes.Concat(settings.GlobalExcludes));

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = folder.Recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                MatchCasing = MatchCasing.CaseInsensitive
            };

            var files = new List<string>();
            int excluded = 0;
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                if (!extensions.Contains(Path.GetExtension(file))) continue;
                var rel = Path.GetRelativePath(root, file);
                if (matcher.IsExcluded(rel))
                {
                    excluded++;
                    continue;
                }
                files.Add(file);
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return new ScanResult(folder, files, excluded, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new ScanResult(folder, Array.Empty<string>(), 0, ex.Message);
        }
    }
}
