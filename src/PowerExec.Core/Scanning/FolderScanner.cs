using PowerExec.Core.Localization;
using PowerExec.Core.Config;

namespace PowerExec.Core.Scanning;

public sealed record ExcludedFile(string FullPath, string RelativePath, string Pattern);

public sealed record ScanResult(FolderConfig Folder, IReadOnlyList<string> Files, IReadOnlyList<ExcludedFile> Excluded, string? Error)
{
    public int ExcludedCount => Excluded.Count;
}

/// <summary>Durchsucht einen konfigurierten Ordner nach passenden Dateien.</summary>
public static class FolderScanner
{
    public static ScanResult Scan(FolderConfig folder, AppSettings settings)
    {
        var root = folder.Path.Trim();
        if (root.Length == 0) return new ScanResult(folder, Array.Empty<string>(), Array.Empty<ExcludedFile>(), L.T("Kein Pfad angegeben."));

        try
        {
            if (!Directory.Exists(root))
                return new ScanResult(folder, Array.Empty<string>(), Array.Empty<ExcludedFile>(), L.T("Ordner nicht gefunden oder nicht erreichbar."));

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
            var excluded = new List<ExcludedFile>();
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                if (!extensions.Contains(Path.GetExtension(file))) continue;
                var rel = FolderVisibility.Normalize(Path.GetRelativePath(root, file));
                var pattern = matcher.Match(rel);
                if (pattern != null)
                {
                    excluded.Add(new ExcludedFile(file, rel, pattern));
                    continue;
                }
                files.Add(file);
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return new ScanResult(folder, files, excluded, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new ScanResult(folder, Array.Empty<string>(), Array.Empty<ExcludedFile>(), ex.Message);
        }
    }
}
