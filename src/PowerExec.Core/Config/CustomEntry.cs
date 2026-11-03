using PowerExec.Core.Models;

namespace PowerExec.Core.Config;

public enum CustomEntryKind { Url, Folder, Rdp, Command, File, Snippet, Ssh, Tool }

public enum SnippetLanguage { PowerShell, Text }

/// <summary>Was ein Klick auf ein Snippet macht.</summary>
public enum SnippetAction { Run, Copy }

public enum BrowserChoice { Default, Edge, Chrome, Firefox }

public enum SshClient { Terminal, Console, Putty }

/// <summary>
/// Eigener Eintrag: Webseite, Ordner/Freigabe, RDP-Verbindung, Befehl, einzelne Datei,
/// Snippet (PowerShell-Code oder Text), SSH-Verbindung oder Windows-Werkzeug.
/// Ziel, Argumente und Code dürfen Platzhalter wie <c>{{Ticket}}</c> enthalten, die beim Start abgefragt werden.
/// </summary>
public sealed class CustomEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public CustomEntryKind Kind { get; set; } = CustomEntryKind.Url;

    /// <summary>URL, Ordnerpfad, Rechnername (RDP/SSH), Programm (Befehl), Dateipfad bzw. Werkzeug („dsa.msc“, „ms-settings:display“).</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Argumente (Befehl, Werkzeug; bei RDP/SSH zusätzliche Optionen).</summary>
    public string Arguments { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    // Snippet
    public string Code { get; set; } = string.Empty;
    public SnippetLanguage Language { get; set; } = SnippetLanguage.PowerShell;
    public SnippetAction Action { get; set; } = SnippetAction.Run;

    // SSH / RDP
    public string User { get; set; } = string.Empty;
    public int Port { get; set; }
    public string KeyFile { get; set; } = string.Empty;
    public SshClient Client { get; set; } = SshClient.Terminal;

    // Webseite
    public BrowserChoice Browser { get; set; } = BrowserChoice.Default;
    public bool Private { get; set; }
    public string BrowserProfile { get; set; } = string.Empty;

    /// <summary>Eigenes Symbol (siehe <see cref="IconRef"/>), leer = automatisch (Favicon, Programmsymbol …).</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Ein Klick im Panel startet sofort (statt den Eintrag aufzuklappen).</summary>
    public bool SingleClick { get; set; }

    public CustomEntry Clone() => (CustomEntry)MemberwiseClone();

    /// <summary>Kopie mit neuer Kennung (Duplizieren).</summary>
    public CustomEntry Duplicate()
    {
        var c = Clone();
        c.Id = Guid.NewGuid().ToString("N");
        c.Name = (string.IsNullOrWhiteSpace(Name) ? Target : Name) + " (Kopie)";
        return c;
    }

    /// <summary>Schlüssel für Favoriten, Profile, Verlauf: bei Dateien der Dateipfad, sonst „custom:Id“.</summary>
    public string Key => Kind == CustomEntryKind.File ? Target.Trim() : PanelConfig.CustomEntriesKey + Id;

    public static bool IsCustomKey(string path) => path.StartsWith(PanelConfig.CustomEntriesKey, StringComparison.OrdinalIgnoreCase);

    public static string KindLabel(CustomEntryKind kind) => kind switch
    {
        CustomEntryKind.Url => "Webseite",
        CustomEntryKind.Folder => "Ordner",
        CustomEntryKind.Rdp => "Remotedesktop",
        CustomEntryKind.File => "Datei",
        CustomEntryKind.Snippet => "Snippet",
        CustomEntryKind.Ssh => "SSH",
        CustomEntryKind.Tool => "Windows-Werkzeug",
        _ => "Befehl"
    };

    /// <summary>Ordner, in dem PowerShell-Snippets als .ps1 abgelegt werden (setzt die App).</summary>
    public static string SnippetFolder { get; set; } = Path.Combine(Path.GetTempPath(), "Power-Exec", "Snippets");

    public string SnippetPath => Path.Combine(SnippetFolder, Id + ".ps1");

    /// <summary>Snippet, das beim Klick nur in die Zwischenablage kopiert wird.</summary>
    public bool CopiesToClipboard => Kind == CustomEntryKind.Snippet && (Language == SnippetLanguage.Text || Action == SnippetAction.Copy);

    /// <summary>Platzhalter in Ziel, Argumenten und Code.</summary>
    public IReadOnlyList<Placeholder> Placeholders => Config.Placeholders.Find(Target, Arguments, Code, User);

    /// <summary>Kopie mit eingesetzten Platzhalter-Werten (bei Webseiten URL-codiert).</summary>
    public CustomEntry WithValues(IReadOnlyDictionary<string, string> values)
    {
        var c = Clone();
        c.Target = Config.Placeholders.Apply(Target, values, urlEncode: Kind == CustomEntryKind.Url);
        c.Arguments = Config.Placeholders.Apply(Arguments, values);
        c.Code = Config.Placeholders.Apply(Code, values);
        c.User = Config.Placeholders.Apply(User, values);
        return c;
    }

    /// <summary>Wandelt den Eintrag in einen startbaren <see cref="LaunchItem"/> um.</summary>
    public LaunchItem ToLaunchItem()
    {
        var target = Target.Trim();
        var name = string.IsNullOrWhiteSpace(Name) ? DefaultName() : Name.Trim();
        if (Kind == CustomEntryKind.File)
        {
            // Einzelne Datei: verhält sich wie eine gefundene Datei (Script-Dialog, Remote, Profile über den Dateipfad)
            return new LaunchItem
            {
                FullPath = target,
                RootFolder = PanelConfig.CustomEntriesKey,
                Type = LaunchItem.TypeFromExtension(target),
                DisplayName = string.IsNullOrWhiteSpace(Name) ? Path.GetFileName(target) : Name.Trim(),
                Synopsis = Description.Trim(),
                CustomKind = Kind,
                Source = this
            };
        }

        if (Kind == CustomEntryKind.Snippet)
        {
            bool ps = Language == SnippetLanguage.PowerShell;
            return new LaunchItem
            {
                FullPath = Key,
                RootFolder = PanelConfig.CustomEntriesKey,
                Type = ps ? ItemType.PowerShell : ItemType.Other,
                DisplayName = name,
                Synopsis = Description.Trim().Length > 0 ? Description.Trim() : FirstLine(Code),
                LaunchTarget = ps ? SnippetPath : null,
                CustomKind = Kind,
                Source = this
            };
        }

        var (type, launch, args) = Kind switch
        {
            CustomEntryKind.Rdp => (ItemType.Executable, "mstsc.exe", ($"/v:{RdpHost()} " + Arguments).Trim()),
            CustomEntryKind.Command => (ItemType.Executable, target, Arguments.Trim()),
            CustomEntryKind.Ssh => SshCommand(),
            CustomEntryKind.Tool => ToolCommand(target),
            CustomEntryKind.Url when Browser != BrowserChoice.Default => (ItemType.Executable, BrowserExe(), BrowserArguments(target)),
            _ => (ItemType.Other, target, string.Empty)
        };
        return new LaunchItem
        {
            FullPath = Key,
            RootFolder = PanelConfig.CustomEntriesKey,
            Type = type,
            DisplayName = name,
            Synopsis = Description.Trim().Length > 0 ? Description.Trim() : SummaryText(),
            LaunchTarget = launch,
            LaunchArguments = args,
            CustomKind = Kind,
            Source = this
        };
    }

    /// <summary>Kurzinfo ohne Wiederholungen: die Adresse, der Pfad bzw. die Verbindung.</summary>
    public string SummaryText() => Kind switch
    {
        CustomEntryKind.Rdp => "Remotedesktop · " + RdpHost(),
        CustomEntryKind.Ssh => "SSH · " + SshTarget() + (Port > 0 && Port != 22 ? ":" + Port : string.Empty),
        CustomEntryKind.Command => (Target.Trim() + " " + Arguments.Trim()).Trim(),
        CustomEntryKind.Tool => (Target.Trim() + " " + Arguments.Trim()).Trim(),
        _ => Target.Trim()
    };

    private string DefaultName() => Kind switch
    {
        CustomEntryKind.Url when Uri.TryCreate(Target.Trim(), UriKind.Absolute, out var u) && u.Host.Length > 0 => u.Host,
        CustomEntryKind.Folder => Path.GetFileName(Target.Trim().TrimEnd('\\', '/')) is { Length: > 0 } f ? f : Target.Trim(),
        CustomEntryKind.Snippet => "Snippet",
        CustomEntryKind.Ssh => SshTarget(),
        _ => Target.Trim()
    };

    private static string FirstLine(string code) =>
        code.Replace("\r", "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#')) ?? string.Empty;

    private string RdpHost()
    {
        var host = Target.Trim();
        return Port > 0 && Port != 3389 && !host.Contains(':') ? $"{host}:{Port}" : host;
    }

    private string SshTarget() => (User.Trim().Length > 0 ? User.Trim() + "@" : string.Empty) + Target.Trim();

    private (ItemType, string, string) SshCommand()
    {
        var parts = new List<string>();
        if (Client == SshClient.Putty)
        {
            parts.Add("-ssh");
            parts.Add(SshTarget());
            if (Port > 0) { parts.Add("-P"); parts.Add(Port.ToString()); }
            if (KeyFile.Trim().Length > 0) { parts.Add("-i"); parts.Add(Quote(KeyFile.Trim())); }
            if (Arguments.Trim().Length > 0) parts.Add(Arguments.Trim());
            return (ItemType.Executable, "putty.exe", string.Join(' ', parts));
        }
        parts.Add(SshTarget());
        if (Port > 0 && Port != 22) { parts.Add("-p"); parts.Add(Port.ToString()); }
        if (KeyFile.Trim().Length > 0) { parts.Add("-i"); parts.Add(Quote(KeyFile.Trim())); }
        if (Arguments.Trim().Length > 0) parts.Add(Arguments.Trim());
        var ssh = string.Join(' ', parts);
        return Client == SshClient.Terminal
            ? (ItemType.Executable, "wt.exe", "ssh " + ssh)
            : (ItemType.Executable, "ssh.exe", ssh);
    }

    /// <summary>Werkzeug: Snap-in (.msc), Einstellungsseite (ms-settings:) oder Programm.</summary>
    private (ItemType, string, string) ToolCommand(string target)
    {
        if (target.EndsWith(".msc", StringComparison.OrdinalIgnoreCase)) return (ItemType.ConsoleSnapIn, target, Arguments.Trim());
        if (target.Contains(':') && !Path.IsPathRooted(target)) return (ItemType.Other, target, string.Empty); // ms-settings:… u. ä.
        return (ItemType.Executable, target, Arguments.Trim());
    }

    private string BrowserExe() => Browser switch
    {
        BrowserChoice.Chrome => "chrome.exe",
        BrowserChoice.Firefox => "firefox.exe",
        _ => "msedge.exe"
    };

    private string BrowserArguments(string url)
    {
        var parts = new List<string>();
        var profile = BrowserProfile.Trim();
        switch (Browser)
        {
            case BrowserChoice.Firefox:
                if (profile.Length > 0) { parts.Add("-P"); parts.Add(Quote(profile)); }
                parts.Add(Private ? "-private-window" : "-new-tab");
                break;
            default:
                if (profile.Length > 0) parts.Add($"--profile-directory={Quote(profile)}");
                if (Private) parts.Add(Browser == BrowserChoice.Chrome ? "--incognito" : "--inprivate");
                break;
        }
        parts.Add(Quote(url));
        return string.Join(' ', parts);
    }

    private static string Quote(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;
}
