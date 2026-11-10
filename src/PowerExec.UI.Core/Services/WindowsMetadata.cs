using PowerExec.Core.Localization;
using System.Diagnostics;
using System.Text;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;

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
        item.Synopsis = string.IsNullOrEmpty(version) ? item.FileName : L.F("{0} · version {1}", item.FileName, version);
        if (!string.IsNullOrWhiteSpace(info.CompanyName))
            item.Description = info.CompanyName.Trim();
    }

    /// <summary>Zonen-Kennung aus dem Datenstrom „Zone.Identifier“ (nur NTFS/ReFS; sonst null).</summary>
    public static int? ReadZoneId(string path)
    {
        try
        {
            var stream = path + ZoneIdentifier.StreamName;
            if (!File.Exists(stream)) return null;
            return ZoneIdentifier.ParseZoneId(File.ReadAllText(stream));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Entfernt die Zonen-Kennung (wie „Entsperren“ in den Dateieigenschaften).</summary>
    public static bool Unblock(string path)
    {
        try
        {
            File.Delete(path + ZoneIdentifier.StreamName);
            return ReadZoneId(path) == null;
        }
        catch
        {
            return false;
        }
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
            item.Synopsis = desc.Length > 0 ? desc : (target.Length > 0 ? L.T("Target: ") + Path.GetFileName(target) : L.T("Shortcut"));
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
            if (OperatingSystem.IsWindows() && WNetGetConnection(drive, sb, ref len) == 0)
                return sb.ToString().TrimEnd('\\') + path[2..];
        }
        catch
        {
            // Kein Netzlaufwerk oder API nicht verfügbar.
        }
        return path;
    }

    public static bool IsNetworkPath(string path) => path.StartsWith(@"\\");

    [System.Runtime.InteropServices.DllImport("mpr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

    public static string ServerShare(string uncPath)
    {
        var parts = uncPath.TrimStart('\\').Split('\\');
        return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : uncPath;
    }
}
