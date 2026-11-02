using System.Diagnostics;
using System.Text;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Interop;

namespace PowerExec.Services;

/// <summary>Liest Dateibeschreibung von EXE-Dateien und Ziel/Kommentar von Verknüpfungen.</summary>
public sealed class WindowsMetadata : IPlatformMetadata
{
    public void FillExecutable(LaunchItem item)
    {
        var info = FileVersionInfo.GetVersionInfo(item.FullPath);
        var description = info.FileDescription?.Trim();
        if (!string.IsNullOrEmpty(description))
            item.DisplayName = description;
        var version = info.ProductVersion?.Trim();
        item.Synopsis = string.IsNullOrEmpty(version) ? item.FileName : $"{item.FileName} · Version {version}";
        if (!string.IsNullOrWhiteSpace(info.CompanyName))
            item.Description = info.CompanyName.Trim();
    }

    public void FillShortcut(LaunchItem item)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        dynamic? shell = null;
        dynamic? link = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            link = shell!.CreateShortcut(item.FullPath);
            string target = link.TargetPath ?? string.Empty;
            string args = link.Arguments ?? string.Empty;
            string desc = link.Description ?? string.Empty;
            string workDir = link.WorkingDirectory ?? string.Empty;
            item.Shortcut = new ShortcutTarget(target, args, workDir);
            item.Synopsis = desc.Length > 0 ? desc : (target.Length > 0 ? "Ziel: " + Path.GetFileName(target) : "Verknüpfung");
            if (target.Length > 0) item.Description = (target + " " + args).Trim();
        }
        finally
        {
            if (link != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            if (shell != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}

/// <summary>Wandelt Pfade auf verbundenen Netzlaufwerken in UNC-Pfade um.</summary>
public static class PathResolver
{
    public static string ToUnc(string path)
    {
        try
        {
            if (path.Length < 3 || path[1] != ':' || path.StartsWith(@"\\")) return path;
            var drive = path[..2];
            var sb = new StringBuilder(512);
            int len = sb.Capacity;
            if (NativeMethods.WNetGetConnection(drive, sb, ref len) == 0)
                return sb.ToString().TrimEnd('\\') + path[2..];
        }
        catch
        {
            // Kein Netzlaufwerk oder API nicht verfügbar.
        }
        return path;
    }

    public static bool IsNetworkPath(string path) => path.StartsWith(@"\\");

    public static string ServerShare(string uncPath)
    {
        var parts = uncPath.TrimStart('\\').Split('\\');
        return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : uncPath;
    }
}
