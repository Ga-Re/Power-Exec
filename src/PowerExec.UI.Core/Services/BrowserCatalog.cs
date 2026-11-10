using PowerExec.Core.Localization;
using System.IO;
using Microsoft.Win32;
using PowerExec.Core.Config;

namespace PowerExec.Services;

/// <summary>Installierte Browser (App Paths bzw. bekannte Ordner) und ihre Profile (Local State / profiles.ini).</summary>
public static class BrowserCatalog
{
    private static IReadOnlyList<BrowserChoice>? _installed;
    private static readonly Dictionary<BrowserChoice, IReadOnlyList<BrowserProfileInfo>> _profiles = new();

    private static string Exe(BrowserChoice b) => b switch
    {
        BrowserChoice.Chrome => "chrome.exe",
        BrowserChoice.Firefox => "firefox.exe",
        _ => "msedge.exe"
    };

    /// <summary>Standardbrowser plus alle gefundenen Browser.</summary>
    public static IReadOnlyList<BrowserChoice> Installed => _installed ??= Detect();

    public static void Refresh()
    {
        _installed = null;
        lock (_profiles) _profiles.Clear();
    }

    private static IReadOnlyList<BrowserChoice> Detect()
    {
        var list = new List<BrowserChoice> { BrowserChoice.Default };
        foreach (var b in new[] { BrowserChoice.Edge, BrowserChoice.Chrome, BrowserChoice.Firefox })
            if (IsInstalled(b)) list.Add(b);
        return list;
    }

    public static bool IsInstalled(BrowserChoice b)
    {
        if (b == BrowserChoice.Default) return true;
        if (OperatingSystem.IsWindows())
        try
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + Exe(b));
                if (key?.GetValue(null) is string path && File.Exists(path.Trim('"'))) return true;
            }
        }
        catch { }
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates = b switch
        {
            BrowserChoice.Edge => new[] { Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe") },
            BrowserChoice.Chrome => new[] { Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"), Path.Combine(local, @"Google\Chrome\Application\chrome.exe") },
            _ => new[] { Path.Combine(pf, @"Mozilla Firefox\firefox.exe"), Path.Combine(pf86, @"Mozilla Firefox\firefox.exe") }
        };
        return candidates.Any(File.Exists);
    }

    public static string DisplayName(BrowserChoice b) => b switch
    {
        BrowserChoice.Edge => "Microsoft Edge",
        BrowserChoice.Chrome => "Google Chrome",
        BrowserChoice.Firefox => "Mozilla Firefox",
        _ => L.T("Standardbrowser")
    };

    /// <summary>Profile eines Browsers (leer beim Standardbrowser oder wenn nichts gefunden wird).</summary>
    public static IReadOnlyList<BrowserProfileInfo> ProfilesOf(BrowserChoice b)
    {
        lock (_profiles)
        {
            if (_profiles.TryGetValue(b, out var cached)) return cached;
            IReadOnlyList<BrowserProfileInfo> result = Array.Empty<BrowserProfileInfo>();
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string? file = b switch
                {
                    BrowserChoice.Edge => Path.Combine(local, @"Microsoft\Edge\User Data\Local State"),
                    BrowserChoice.Chrome => Path.Combine(local, @"Google\Chrome\User Data\Local State"),
                    BrowserChoice.Firefox => Path.Combine(roaming, @"Mozilla\Firefox\profiles.ini"),
                    _ => null
                };
                if (file != null && File.Exists(file))
                {
                    // Browser kann die Datei geöffnet haben: mit Freigabe lesen
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(fs);
                    var text = reader.ReadToEnd();
                    result = b == BrowserChoice.Firefox ? BrowserProfiles.ParseFirefoxProfilesIni(text) : BrowserProfiles.ParseChromiumLocalState(text);
                }
            }
            catch (Exception ex)
            {
                AppLog.Info(L.F("Browserprofile von {0} nicht lesbar: {1}", DisplayName(b), ex.Message));
            }
            _profiles[b] = result;
            return result;
        }
    }

    /// <summary>Anzeigename eines gespeicherten Profils (Ordnername → „Arbeit (name@firma.de)“).</summary>
    public static string ProfileDisplay(BrowserChoice b, string id) =>
        ProfilesOf(b).FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Display ?? id;
}
