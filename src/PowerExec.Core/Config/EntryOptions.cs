using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerExec.Core.Config;

/// <summary>
/// Startoptionen eines eigenen Eintrags, die je Startprofil verschieden sein dürfen
/// (Browser und Browserprofil, RDP-Anzeige, SSH-Optionen, „Öffnen mit“, Arbeitsordner …).
/// Ziel, Adresse und Code gehören dagegen zum Eintrag selbst.
/// </summary>
public sealed class EntryOptions
{
    // Webseite
    public BrowserChoice Browser { get; set; }
    public string BrowserProfile { get; set; } = string.Empty;
    public bool Private { get; set; }
    public bool AppWindow { get; set; }
    // RDP / SSH
    public string User { get; set; } = string.Empty;
    public RdpScreen Screen { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public bool AllMonitors { get; set; }
    public bool AdminSession { get; set; }
    public string Gateway { get; set; } = string.Empty;
    public SshClient Client { get; set; }
    public string RemoteCommand { get; set; } = string.Empty;
    public string JumpHost { get; set; } = string.Empty;
    public string PortForward { get; set; } = string.Empty;
    public string TerminalProfile { get; set; } = string.Empty;
    // Ordner
    public FolderOpener OpenWith { get; set; }
    public string OpenWithProgram { get; set; } = string.Empty;
    // Programme
    public string WorkingDirectory { get; set; } = string.Empty;
    public LaunchWindowState WindowState { get; set; }
    // Text-Snippet
    public bool PasteAfterCopy { get; set; }
    public bool CopyAsHtml { get; set; }

    public static EntryOptions From(CustomEntry e) => new()
    {
        Browser = e.Browser, BrowserProfile = e.BrowserProfile, Private = e.Private, AppWindow = e.AppWindow,
        User = e.User, Screen = e.Screen, ScreenWidth = e.ScreenWidth, ScreenHeight = e.ScreenHeight, AllMonitors = e.AllMonitors,
        AdminSession = e.AdminSession, Gateway = e.Gateway, Client = e.Client, RemoteCommand = e.RemoteCommand, JumpHost = e.JumpHost,
        PortForward = e.PortForward, TerminalProfile = e.TerminalProfile, OpenWith = e.OpenWith, OpenWithProgram = e.OpenWithProgram,
        WorkingDirectory = e.WorkingDirectory, WindowState = e.WindowState, PasteAfterCopy = e.PasteAfterCopy, CopyAsHtml = e.CopyAsHtml
    };

    public void ApplyTo(CustomEntry e)
    {
        e.Browser = Browser; e.BrowserProfile = BrowserProfile; e.Private = Private; e.AppWindow = AppWindow;
        e.User = User; e.Screen = Screen; e.ScreenWidth = ScreenWidth; e.ScreenHeight = ScreenHeight; e.AllMonitors = AllMonitors;
        e.AdminSession = AdminSession; e.Gateway = Gateway; e.Client = Client; e.RemoteCommand = RemoteCommand; e.JumpHost = JumpHost;
        e.PortForward = PortForward; e.TerminalProfile = TerminalProfile; e.OpenWith = OpenWith; e.OpenWithProgram = OpenWithProgram;
        e.WorkingDirectory = WorkingDirectory; e.WindowState = WindowState; e.PasteAfterCopy = PasteAfterCopy; e.CopyAsHtml = CopyAsHtml;
    }

    public EntryOptions Clone() => (EntryOptions)MemberwiseClone();

    /// <summary>Kurzbeschreibung für die Profilliste, z. B. „Edge · Arbeit“.</summary>
    public string Summary(CustomEntryKind kind, Func<BrowserChoice, string, string>? profileName = null)
    {
        var parts = new List<string>();
        switch (kind)
        {
            case CustomEntryKind.Url:
                parts.Add(Browser switch { BrowserChoice.Edge => "Edge", BrowserChoice.Chrome => "Chrome", BrowserChoice.Firefox => "Firefox", _ => "Standardbrowser" });
                if (BrowserProfile.Length > 0) parts.Add(profileName?.Invoke(Browser, BrowserProfile) ?? BrowserProfile);
                if (Private) parts.Add("privat");
                if (AppWindow) parts.Add("App-Fenster");
                break;
            case CustomEntryKind.Rdp:
                if (User.Length > 0) parts.Add(User);
                parts.Add(Screen switch { RdpScreen.Fullscreen => "Vollbild", RdpScreen.Window => ScreenWidth > 0 ? $"{ScreenWidth}×{ScreenHeight}" : "Fenster", _ => "Standardanzeige" });
                if (AllMonitors) parts.Add("alle Monitore");
                if (AdminSession) parts.Add("Admin-Sitzung");
                if (Gateway.Length > 0) parts.Add("über " + Gateway);
                break;
            case CustomEntryKind.Ssh:
                if (User.Length > 0) parts.Add(User);
                parts.Add(Client switch { SshClient.Putty => "PuTTY", SshClient.Console => "Konsole", _ => "Terminal" });
                if (JumpHost.Length > 0) parts.Add("über " + JumpHost);
                if (RemoteCommand.Length > 0) parts.Add(RemoteCommand);
                break;
            case CustomEntryKind.Folder:
                parts.Add(OpenWith switch { FolderOpener.NewWindow => "neues Fenster", FolderOpener.Program => Path.GetFileNameWithoutExtension(OpenWithProgram), FolderOpener.Terminal => "Terminal", _ => "Explorer" });
                break;
            case CustomEntryKind.Command or CustomEntryKind.Tool or CustomEntryKind.File:
                if (WorkingDirectory.Length > 0) parts.Add("in " + WorkingDirectory);
                if (WindowState != LaunchWindowState.Normal) parts.Add(WindowState == LaunchWindowState.Maximized ? "maximiert" : "minimiert");
                break;
            case CustomEntryKind.Snippet:
                if (CopyAsHtml) parts.Add("formatiert");
                if (PasteAfterCopy) parts.Add("direkt einfügen");
                break;
        }
        return string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}

/// <summary>Gefundenes Browserprofil (Ordnername für --profile-directory bzw. Name für Firefox -P).</summary>
public sealed record BrowserProfileInfo(string Id, string Name, string Account)
{
    public string Display => Account.Length > 0 && !Name.Contains(Account, StringComparison.OrdinalIgnoreCase) ? $"{Name} ({Account})" : Name;
}

/// <summary>Liest Browserprofile aus den Konfigurationsdateien (ohne Zugriff auf Browserdaten wie Verlauf oder Kennwörter).</summary>
public static class BrowserProfiles
{
    /// <summary>Chromium (Edge, Chrome): „Local State“ → profile.info_cache.</summary>
    public static IReadOnlyList<BrowserProfileInfo> ParseChromiumLocalState(string json)
    {
        var list = new List<BrowserProfileInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("profile", out var profile) || !profile.TryGetProperty("info_cache", out var cache)) return list;
            foreach (var p in cache.EnumerateObject())
            {
                var name = p.Value.TryGetProperty("name", out var n) ? n.GetString() ?? p.Name : p.Name;
                var account = p.Value.TryGetProperty("user_name", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                list.Add(new BrowserProfileInfo(p.Name, name, account));
            }
        }
        catch (JsonException) { }
        return list.OrderBy(p => p.Id == "Default" ? 0 : 1).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Firefox: profiles.ini → [ProfileN] Name=…</summary>
    public static IReadOnlyList<BrowserProfileInfo> ParseFirefoxProfilesIni(string ini)
    {
        var list = new List<BrowserProfileInfo>();
        bool inProfile = false;
        foreach (var raw in ini.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inProfile = Regex.IsMatch(line, @"^\[Profile\d+\]$", RegexOptions.IgnoreCase); continue; }
            if (inProfile && line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
            {
                var name = line[5..].Trim();
                if (name.Length > 0) list.Add(new BrowserProfileInfo(name, name, string.Empty));
            }
        }
        return list;
    }
}
