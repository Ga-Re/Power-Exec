using PowerExec.Core.Models;

namespace PowerExec.Core.Config;

public enum CustomEntryKind { Url, Folder, Rdp, Command, File, Snippet, Ssh, Tool, Workflow }

/// <summary>Ein Schritt eines Ablaufs: Eintrag (Pfad bzw. „custom:Id“) und Startprofil.</summary>
public sealed class WorkflowStep
{
    public string Path { get; set; } = string.Empty;
    /// <summary>Anzeigename beim Anlegen (falls der Eintrag später fehlt).</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Startprofil; leer = Standardprofil.</summary>
    public string ProfileName { get; set; } = string.Empty;

    public WorkflowStep Clone() => new() { Path = Path, Name = Name, ProfileName = ProfileName };
}

public enum SnippetLanguage { PowerShell, Text }

/// <summary>Was ein Klick auf ein Snippet macht.</summary>
public enum SnippetAction { Run, Copy }

public enum BrowserChoice { Default, Edge, Chrome, Firefox }

public enum SshClient { Terminal, Console, Putty }

/// <summary>Bildschirm einer RDP-Verbindung.</summary>
public enum RdpScreen { Default, Fullscreen, Window }

/// <summary>Womit ein Ordner geöffnet wird.</summary>
public enum FolderOpener { Explorer, NewWindow, Program, Terminal }

/// <summary>Fenster beim Start eines Programms.</summary>
public enum LaunchWindowState { Normal, Maximized, Minimized }

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

    // SSH (zusätzlich)
    /// <summary>Befehl, der nach der Anmeldung ausgeführt wird (ssh -t … "Befehl").</summary>
    public string RemoteCommand { get; set; } = string.Empty;
    /// <summary>Sprungserver (ssh -J), z. B. „admin@bastion“.</summary>
    public string JumpHost { get; set; } = string.Empty;
    /// <summary>Lokale Weiterleitungen (ssh -L), z. B. „8080:localhost:80“; mehrere mit ; getrennt.</summary>
    public string PortForward { get; set; } = string.Empty;
    /// <summary>Profil in Windows Terminal (wt -p).</summary>
    public string TerminalProfile { get; set; } = string.Empty;

    // RDP (zusätzlich)
    /// <summary>Vorhandene .rdp-Datei; gesetzt = Einzelfelder (außer Admin-Sitzung) werden nicht verwendet.</summary>
    public string RdpFile { get; set; } = string.Empty;
    public RdpScreen Screen { get; set; } = RdpScreen.Default;
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public bool AllMonitors { get; set; }
    public bool AdminSession { get; set; }
    public string Gateway { get; set; } = string.Empty;

    // Webseite
    public BrowserChoice Browser { get; set; } = BrowserChoice.Default;
    public bool Private { get; set; }
    public string BrowserProfile { get; set; } = string.Empty;
    /// <summary>Als App-Fenster ohne Tabs und Adressleiste (Edge/Chrome: --app).</summary>
    public bool AppWindow { get; set; }

    // Text-Snippet
    /// <summary>Nach dem Kopieren in das vorher aktive Fenster einfügen (Strg+V).</summary>
    public bool PasteAfterCopy { get; set; }
    /// <summary>Code ist HTML und wird formatiert (plus als reiner Text) kopiert.</summary>
    public bool CopyAsHtml { get; set; }

    // Ordner
    public FolderOpener OpenWith { get; set; } = FolderOpener.Explorer;
    /// <summary>Programm für „Öffnen mit“ (z. B. Total Commander).</summary>
    public string OpenWithProgram { get; set; } = string.Empty;

    // Befehl, Datei, Werkzeug
    public string WorkingDirectory { get; set; } = string.Empty;
    public LaunchWindowState WindowState { get; set; } = LaunchWindowState.Normal;

    /// <summary>Eigenes Symbol (siehe <see cref="IconRef"/>), leer = automatisch (Favicon, Programmsymbol …).</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Ein Klick im Panel startet sofort (statt den Eintrag aufzuklappen).</summary>
    public bool SingleClick { get; set; }

    /// <summary>Aus einem Team-Katalog (Kennung des Abos), leer = eigener Eintrag. Katalogeinträge sind schreibgeschützt.</summary>
    public string CatalogId { get; set; } = string.Empty;

    /// <summary>Stand beim letzten Übernehmen (für „geändert“).</summary>
    public string CatalogFingerprint { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFromCatalog => CatalogId.Length > 0;

    /// <summary>Ablauf: Schritte, die nacheinander starten.</summary>
    public List<WorkflowStep> Steps { get; set; } = new();

    /// <summary>Ablauf: bei einem Fehler (Exit-Code ≠ 0, Remote-Fehler, abgebrochen) die restlichen Schritte nicht starten.</summary>
    public bool StopOnError { get; set; } = true;

    public CustomEntry Clone()
    {
        var c = (CustomEntry)MemberwiseClone();
        c.Steps = Steps.Select(s => s.Clone()).ToList();
        return c;
    }

    /// <summary>Kopie mit den Startoptionen eines Profils (null = unverändert).</summary>
    public CustomEntry WithProfile(LaunchProfile? profile)
    {
        var c = Clone();
        profile?.Options?.ApplyTo(c);
        return c;
    }

    /// <summary>Kopie mit neuer Kennung (Duplizieren).</summary>
    public CustomEntry Duplicate()
    {
        var c = Clone();
        c.Id = Guid.NewGuid().ToString("N");
        c.Name = (string.IsNullOrWhiteSpace(Name) ? Target : Name) + " (Kopie)";
        c.CatalogId = string.Empty; // Kopie gehört dem Benutzer
        c.CatalogFingerprint = string.Empty;
        return c;
    }

    /// <summary>Schlüssel für Favoriten, Profile, Verlauf: bei Dateien der Dateipfad, sonst „custom:Id“.</summary>
    public string Key => Kind == CustomEntryKind.File ? Target.Trim() : PanelConfig.CustomEntriesKey + Id;

    /// <summary>
    /// Vorlage zu einem Art-Schlüssel aus Menüs („Url“, „Snippet“ = PowerShell, „TextSnippet“ …); null = Art erst im Dialog wählen.
    /// </summary>
    public static CustomEntry? FromKindKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (key.Equals("TextSnippet", StringComparison.OrdinalIgnoreCase))
            return new CustomEntry { Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text };
        return Enum.TryParse<CustomEntryKind>(key, true, out var k) ? new CustomEntry { Kind = k } : null;
    }

    /// <summary>Schlüssel für Fenstergröße, Kacheln usw. (PowerShell- und Text-Snippets getrennt).</summary>
    public string KindKey => Kind == CustomEntryKind.Snippet && Language == SnippetLanguage.Text ? "TextSnippet" : Kind.ToString();

    /// <summary>Nach einem Artwechsel: Werte, die zur neuen Art nicht passen, verwerfen (Name, Symbol, Ziel, Beschreibung bleiben).</summary>
    public CustomEntry CleanedForKind()
    {
        var c = Clone();
        var blank = new CustomEntry();
        if (Kind != CustomEntryKind.Snippet) { c.Code = string.Empty; c.Language = blank.Language; c.Action = blank.Action; c.PasteAfterCopy = false; c.CopyAsHtml = false; }
        if (Kind is not (CustomEntryKind.Ssh or CustomEntryKind.Rdp)) { c.User = string.Empty; c.Port = 0; }
        if (Kind != CustomEntryKind.Ssh) { c.KeyFile = string.Empty; c.Client = blank.Client; c.RemoteCommand = c.JumpHost = c.PortForward = c.TerminalProfile = string.Empty; }
        if (Kind != CustomEntryKind.Rdp) { c.RdpFile = c.Gateway = string.Empty; c.Screen = blank.Screen; c.ScreenWidth = c.ScreenHeight = 0; c.AllMonitors = c.AdminSession = false; }
        if (Kind != CustomEntryKind.Url) { c.Browser = blank.Browser; c.Private = c.AppWindow = false; c.BrowserProfile = string.Empty; }
        if (Kind != CustomEntryKind.Folder) { c.OpenWith = blank.OpenWith; c.OpenWithProgram = string.Empty; }
        if (Kind is not (CustomEntryKind.Command or CustomEntryKind.Tool or CustomEntryKind.File)) { c.WorkingDirectory = string.Empty; c.WindowState = blank.WindowState; }
        if (Kind is not (CustomEntryKind.Command or CustomEntryKind.Tool or CustomEntryKind.Rdp or CustomEntryKind.Ssh)) c.Arguments = string.Empty;
        if (Kind == CustomEntryKind.Snippet) c.Target = string.Empty;
        return c;
    }

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
    public IReadOnlyList<Placeholder> Placeholders => Config.Placeholders.Find(Target, Arguments, Code, User, RemoteCommand, WorkingDirectory);

    /// <summary>Enthält Platzhalter oder eingebaute Variablen ({{$Datum}} …), die vor dem Start eingesetzt werden müssen.</summary>
    public bool NeedsResolving => Placeholders.Count > 0 || Config.Placeholders.HasBuiltins(Target, Arguments, Code, User, RemoteCommand, WorkingDirectory);

    /// <summary>Kopie mit eingesetzten Platzhalter-Werten (bei Webseiten URL-codiert).</summary>
    public CustomEntry WithValues(IReadOnlyDictionary<string, string> values)
    {
        var c = Clone();
        c.Target = Config.Placeholders.Apply(Target, values, urlEncode: Kind == CustomEntryKind.Url);
        c.Arguments = Config.Placeholders.Apply(Arguments, values);
        c.Code = Config.Placeholders.Apply(Code, values, powerShell: Kind == CustomEntryKind.Snippet && Language == SnippetLanguage.PowerShell);
        c.User = Config.Placeholders.Apply(User, values);
        c.RemoteCommand = Config.Placeholders.Apply(RemoteCommand, values);
        c.WorkingDirectory = Config.Placeholders.Apply(WorkingDirectory, values);
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
                WorkingDirectory = WorkingDirectory.Trim().Length > 0 ? WorkingDirectory.Trim() : null,
                WindowState = WindowState,
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

        if (Kind == CustomEntryKind.Workflow)
        {
            return new LaunchItem
            {
                FullPath = Key,
                RootFolder = PanelConfig.CustomEntriesKey,
                Type = ItemType.Other,
                DisplayName = string.IsNullOrWhiteSpace(Name) ? "Ablauf" : Name.Trim(),
                Synopsis = Description.Trim().Length > 0 ? Description.Trim() : SummaryText(),
                CustomKind = Kind,
                Source = this
            };
        }

        var (type, launch, args) = Kind switch
        {
            CustomEntryKind.Rdp => (ItemType.Executable, "mstsc.exe", RdpArguments()),
            CustomEntryKind.Command => (ItemType.Executable, target, Arguments.Trim()),
            CustomEntryKind.Ssh => SshCommand(),
            CustomEntryKind.Tool => ToolCommand(target),
            CustomEntryKind.Url when Browser != BrowserChoice.Default || AppWindow => (ItemType.Executable, BrowserExe(), BrowserArguments(target)),
            CustomEntryKind.Folder => FolderCommand(target),
            _ => (ItemType.Other, target, string.Empty)
        };
        bool programLike = Kind is CustomEntryKind.Command or CustomEntryKind.Tool;
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
            WorkingDirectory = programLike && WorkingDirectory.Trim().Length > 0 ? WorkingDirectory.Trim() : null,
            WindowState = programLike ? WindowState : LaunchWindowState.Normal,
            Source = this
        };
    }

    /// <summary>Kurzinfo ohne Wiederholungen: die Adresse, der Pfad bzw. die Verbindung.</summary>
    public string SummaryText() => Kind switch
    {
        CustomEntryKind.Rdp => "Remotedesktop · " + (UsesRdpFile ? LeafOf(RdpFile) : RdpHost()),
        CustomEntryKind.Ssh => "SSH · " + SshTarget() + (Port > 0 && Port != 22 ? ":" + Port : string.Empty),
        CustomEntryKind.Command => (Target.Trim() + " " + Arguments.Trim()).Trim(),
        CustomEntryKind.Tool => (Target.Trim() + " " + Arguments.Trim()).Trim(),
        CustomEntryKind.Workflow => $"Ablauf · {Steps.Count} {(Steps.Count == 1 ? "Schritt" : "Schritte")}: " + string.Join(" → ", Steps.Select(s => s.Name.Length > 0 ? s.Name : System.IO.Path.GetFileName(s.Path))),
        _ => Target.Trim()
    };

    private string DefaultName() => Kind switch
    {
        CustomEntryKind.Url when Uri.TryCreate(Target.Trim(), UriKind.Absolute, out var u) && u.Host.Length > 0 => u.Host,
        CustomEntryKind.Folder => Path.GetFileName(Target.Trim().TrimEnd('\\', '/')) is { Length: > 0 } f ? f : Target.Trim(),
        CustomEntryKind.Snippet => "Snippet",
        CustomEntryKind.Ssh => SshTarget(),
        CustomEntryKind.Rdp when UsesRdpFile && Target.Trim().Length == 0 => Path.GetFileNameWithoutExtension(LeafOf(RdpFile)),
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

    private IEnumerable<string> Forwards() =>
        PortForward.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private (ItemType, string, string) SshCommand()
    {
        var parts = new List<string>();
        if (Client == SshClient.Putty)
        {
            parts.Add("-ssh");
            parts.Add(SshTarget());
            if (Port > 0) { parts.Add("-P"); parts.Add(Port.ToString()); }
            if (KeyFile.Trim().Length > 0) { parts.Add("-i"); parts.Add(Quote(KeyFile.Trim())); }
            foreach (var f in Forwards()) { parts.Add("-L"); parts.Add(f); }
            if (Arguments.Trim().Length > 0) parts.Add(Arguments.Trim());
            return (ItemType.Executable, "putty.exe", string.Join(' ', parts));
        }
        parts.Add(SshTarget());
        if (Port > 0 && Port != 22) { parts.Add("-p"); parts.Add(Port.ToString()); }
        if (KeyFile.Trim().Length > 0) { parts.Add("-i"); parts.Add(Quote(KeyFile.Trim())); }
        if (JumpHost.Trim().Length > 0) { parts.Add("-J"); parts.Add(JumpHost.Trim()); }
        foreach (var f in Forwards()) { parts.Add("-L"); parts.Add(f); }
        if (Arguments.Trim().Length > 0) parts.Add(Arguments.Trim());
        if (RemoteCommand.Trim().Length > 0)
        {
            parts.Add("-t");
            parts.Add("\"" + RemoteCommand.Trim().Replace("\"", "\\\"") + "\"");
        }
        var ssh = string.Join(' ', parts);
        if (Client != SshClient.Terminal) return (ItemType.Executable, "ssh.exe", ssh);
        // Windows Terminal trennt Befehle an „;“ – im SSH-Aufruf maskieren
        var wt = (TerminalProfile.Trim().Length > 0 ? $"-p {Quote(TerminalProfile.Trim(), always: true)} " : string.Empty) + "ssh " + ssh.Replace(";", "\\;");
        return (ItemType.Executable, "wt.exe", wt);
    }

    /// <summary>Hinweise zu Optionen, die der gewählte SSH-Client nicht kennt.</summary>
    public IReadOnlyList<string> SshWarnings()
    {
        var w = new List<string>();
        if (Kind != CustomEntryKind.Ssh || Client != SshClient.Putty) return w;
        if (JumpHost.Trim().Length > 0) w.Add("PuTTY unterstützt den Sprungserver hier nicht – nimm Windows Terminal oder Konsole.");
        if (RemoteCommand.Trim().Length > 0) w.Add("PuTTY führt den Befehl nach dem Login hier nicht aus – nimm Windows Terminal oder Konsole.");
        if (TerminalProfile.Trim().Length > 0) w.Add("Das Terminal-Profil gilt nur für Windows Terminal.");
        return w;
    }

    // ---- Remotedesktop ---------------------------------------------------------

    /// <summary>Ordner für erzeugte .rdp-Dateien (setzt die App).</summary>
    public static string RdpFolder { get; set; } = Path.Combine(Path.GetTempPath(), "Power-Exec", "Rdp");

    public bool UsesRdpFile => Kind == CustomEntryKind.Rdp && RdpFile.Trim().Length > 0;

    /// <summary>Benutzer oder Gateway lassen sich nicht über die Befehlszeile übergeben: dann erzeugt Power-Exec eine .rdp-Datei (ohne Kennwort).</summary>
    public bool NeedsGeneratedRdpFile => Kind == CustomEntryKind.Rdp && !UsesRdpFile && (User.Trim().Length > 0 || Gateway.Trim().Length > 0);

    public string GeneratedRdpPath => Path.Combine(RdpFolder, Id + ".rdp");

    private string RdpArguments()
    {
        var parts = new List<string>();
        if (UsesRdpFile) parts.Add(Quote(RdpFile.Trim(), always: true));
        else if (NeedsGeneratedRdpFile) parts.Add(Quote(GeneratedRdpPath, always: true));
        else parts.Add("/v:" + RdpHost());
        if (!UsesRdpFile)
        {
            switch (Screen)
            {
                case RdpScreen.Fullscreen: parts.Add("/f"); break;
                case RdpScreen.Window when ScreenWidth > 0 && ScreenHeight > 0: parts.Add($"/w:{ScreenWidth}"); parts.Add($"/h:{ScreenHeight}"); break;
            }
            if (AllMonitors) parts.Add("/multimon");
        }
        if (AdminSession) parts.Add("/admin");
        if (Arguments.Trim().Length > 0) parts.Add(Arguments.Trim());
        return string.Join(' ', parts);
    }

    /// <summary>Inhalt der erzeugten .rdp-Datei – nie mit Kennwort.</summary>
    public string RdpFileContent()
    {
        var lines = new List<string> { "full address:s:" + RdpHost() };
        if (User.Trim().Length > 0) lines.Add("username:s:" + User.Trim());
        lines.Add("prompt for credentials:i:1");
        if (Screen == RdpScreen.Fullscreen) lines.Add("screen mode id:i:2");
        else if (Screen == RdpScreen.Window)
        {
            lines.Add("screen mode id:i:1");
            if (ScreenWidth > 0 && ScreenHeight > 0) { lines.Add("desktopwidth:i:" + ScreenWidth); lines.Add("desktopheight:i:" + ScreenHeight); }
        }
        if (AllMonitors) lines.Add("use multimon:i:1");
        if (Gateway.Trim().Length > 0)
        {
            lines.Add("gatewayhostname:s:" + Gateway.Trim());
            lines.Add("gatewayusagemethod:i:1");
            lines.Add("gatewayprofileusagemethod:i:1");
            lines.Add("gatewaycredentialssource:i:0");
        }
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>Vor dem Start: ggf. die .rdp-Datei schreiben.</summary>
    public void PrepareLaunch()
    {
        if (!NeedsGeneratedRdpFile) return;
        Directory.CreateDirectory(RdpFolder);
        File.WriteAllText(GeneratedRdpPath, RdpFileContent(), System.Text.Encoding.Unicode);
    }

    // ---- Ordner ----------------------------------------------------------------

    /// <summary>Ist Windows Terminal installiert? (setzt die App; sonst PowerShell-Konsole)</summary>
    public static Func<bool> TerminalAvailable { get; set; } = () => true;

    /// <summary>
    /// Pfad für Befehlszeilen: ohne abschließenden Backslash (sonst maskiert er das Anführungszeichen),
    /// Laufwerkswurzel als „C:\.“.
    /// </summary>
    public static string CommandLinePath(string path)
    {
        var p = path.Trim().Trim('"');
        if (p.Length > 3 || p.StartsWith(@"\\")) p = p.TrimEnd('\\', '/');
        if (p.Length == 2 && p[1] == ':') p += @"\.";
        else if (p.Length == 3 && p[1] == ':' && p[2] is '\\' or '/') p = p[..2] + @"\.";
        return p;
    }

    /// <summary>Terminal in diesem Ordner: Windows Terminal (-d), sonst eine PowerShell-Konsole mit Set-Location.</summary>
    private static (ItemType, string, string) TerminalCommand(string target)
    {
        var p = CommandLinePath(target);
        if (TerminalAvailable()) return (ItemType.Executable, "wt.exe", "-d \"" + p + "\"");
        return (ItemType.Executable, "powershell.exe", "-NoExit -NoLogo -Command \"Set-Location -LiteralPath '" + p.Replace("'", "''") + "'\"");
    }

    private (ItemType, string, string) FolderCommand(string target) => OpenWith switch
    {
        FolderOpener.NewWindow => (ItemType.Executable, "explorer.exe", "/n,\"" + CommandLinePath(target) + "\""),
        FolderOpener.Terminal => TerminalCommand(target),
        FolderOpener.Program when OpenWithProgram.Trim().Length > 0 => (ItemType.Executable, OpenWithProgram.Trim(), "\"" + CommandLinePath(target) + "\""),
        _ => (ItemType.Other, target, string.Empty)
    };

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
        _ => "msedge.exe" // auch für App-Fenster mit Standardbrowser
    };

    private string BrowserArguments(string url)
    {
        var parts = new List<string>();
        var profile = BrowserProfile.Trim();
        switch (Browser)
        {
            case BrowserChoice.Firefox:
                if (profile.Length > 0) { parts.Add("-P"); parts.Add(Quote(profile)); }
                // Firefox kennt kein App-Fenster: dann wenigstens ein eigenes Fenster
                parts.Add(Private ? "-private-window" : AppWindow ? "-new-window" : "-new-tab");
                break;
            default:
                if (profile.Length > 0) parts.Add($"--profile-directory={Quote(profile)}");
                if (Private) parts.Add(Browser == BrowserChoice.Chrome ? "--incognito" : "--inprivate");
                if (AppWindow) { parts.Add("--app=" + Quote(url)); return string.Join(' ', parts); }
                break;
        }
        parts.Add(Quote(url));
        return string.Join(' ', parts);
    }

    /// <summary>Dateiname auch bei Windows-Pfaden auf anderen Systemen.</summary>
    private static string LeafOf(string path) => path.Trim().Split('\\', '/').LastOrDefault() ?? path;

    private static string Quote(string s, bool always = false) => always || s.Contains(' ') ? "\"" + s + "\"" : s;
}
