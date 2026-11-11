using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Diagnostics;

/// <summary>Eine Datei des Testbestands (relativ zum Ordner).</summary>
public sealed record PerfFile(string RelativePath, ItemType Type, string Synopsis, string Description);

/// <summary>
/// Reproduzierbarer Testbestand für Leistungsmessungen (Benchmarks und Selbsttest):
/// ein Ordner mit Unterordnern als smarte Gruppen, eigene Gruppen mit zugeordneten Dateien,
/// eigene Einträge, Schlagworte, Favoriten und Startstatistik. Standard: 500 Einträge in ~20 Gruppen.
/// </summary>
public sealed class PerfDataset
{
    /// <summary>Tippen und Löschen von „disk“ Zeichen für Zeichen – das Szenario der Messung.</summary>
    public static readonly IReadOnlyList<string> TypingSequence = ["d", "di", "dis", "disk", "dis", "di", "d", ""];

    private static readonly string[] Verbs =
        ["Get", "Set", "Reset", "New", "Remove", "Test", "Start", "Stop", "Restart", "Export", "Import", "Sync", "Clear", "Update", "Invoke"];

    private static readonly string[] Nouns =
    [
        "DiskSpace", "ADUser", "ADUserPassword", "ADGroup", "Mailbox", "Service", "EventLog", "Printer", "Share", "Certificate",
        "DnsRecord", "DhcpLease", "VMSnapshot", "GPO", "Backup", "Patch", "FirewallRule", "Session", "UserProfile", "License",
        "TempFiles", "SqlJob", "IISAppPool", "ScheduledTask", "BitLocker", "Defender", "Inventory", "NetAdapter", "Volume", "Quota"
    ];

    private static readonly string[] Subfolders =
        ["AD", "Exchange", "Server", "Client", "Netzwerk", "Backup", "Sicherheit", "SQL", "Web", "Drucker", "Lizenzen", "Wartung"];

    private static readonly string[] OwnGroups =
        ["Kunde A", "Kunde A/AD", "Kunde B", "Kunde B/Server", "Täglich", "Notfall", "Onboarding", "Reports"];

    private static readonly string[] TagPool =
        ["ad", "exchange", "server", "client", "netz", "backup", "security", "sql", "web", "druck", "lizenz", "wartung", "kunde-a", "kunde-b", "täglich"];

    public string Root { get; }
    public string FolderGroup => "Scripte";
    public IReadOnlyList<PerfFile> Files { get; }
    public IReadOnlyList<CustomEntry> CustomEntries { get; }

    private readonly int _seed;

    private PerfDataset(string root, int seed, List<PerfFile> files, List<CustomEntry> custom)
    {
        Root = root;
        _seed = seed;
        Files = files;
        CustomEntries = custom;
    }

    /// <param name="root">Ordner, in dem die Dateien liegen (bzw. liegen würden).</param>
    /// <param name="fileCount">Dateien im Ordner; dazu kommen <paramref name="customCount"/> eigene Einträge.</param>
    public static PerfDataset Create(string root, int fileCount = 440, int customCount = 60, int seed = 17)
    {
        var rnd = new Random(seed);
        var files = new List<PerfFile>(fileCount);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < fileCount; i++)
        {
            var sub = Subfolders[i % Subfolders.Length];
            string name;
            do name = Verbs[rnd.Next(Verbs.Length)] + "-" + Nouns[rnd.Next(Nouns.Length)] + (rnd.Next(4) == 0 ? rnd.Next(2, 9).ToString() : string.Empty);
            while (!used.Add(sub + "/" + name));
            var roll = rnd.Next(100);
            var type = roll < 80 ? ItemType.PowerShell : roll < 92 ? ItemType.Batch : ItemType.Executable;
            var ext = type switch { ItemType.PowerShell => ".ps1", ItemType.Batch => ".cmd", _ => ".exe" };
            var synopsis = $"{VerbText(name)} für {sub} ({NounOf(name)})";
            // Jede zehnte Datei mit langer Beschreibung (> 300 Zeichen: in der Suche nur zusammenhängend)
            var description = i % 10 == 0
                ? string.Join(' ', Enumerable.Repeat($"Prüft {NounOf(name)} auf allen Rechnern der Gruppe {sub} und schreibt ein Protokoll.", 5))
                : string.Empty;
            files.Add(new PerfFile(Path.Combine(sub, name + ext), type, synopsis, description));
        }

        var custom = new List<CustomEntry>(customCount);
        for (int i = 0; i < customCount; i++)
        {
            var noun = Nouns[rnd.Next(Nouns.Length)];
            var entry = (i % 6) switch
            {
                0 => new CustomEntry { Kind = CustomEntryKind.Url, Name = $"Portal {noun}", Target = $"https://intranet.example/{noun.ToLowerInvariant()}" },
                1 => new CustomEntry { Kind = CustomEntryKind.Rdp, Name = $"SRV-{noun.ToUpperInvariant()}-{i:00}", Target = $"srv-{i:00}.example" },
                2 => new CustomEntry { Kind = CustomEntryKind.Ssh, Name = $"Linux {noun}", Target = $"lx-{i:00}.example", User = "admin" },
                3 => new CustomEntry { Kind = CustomEntryKind.Folder, Name = $"Freigabe {noun}", Target = $@"\\fs01\{noun}" },
                4 => new CustomEntry { Kind = CustomEntryKind.Command, Name = $"Konsole {noun}", Target = "cmd.exe" },
                _ => new CustomEntry { Kind = CustomEntryKind.Snippet, Name = $"Snippet {noun}", Code = $"param([string]$Ziel = 'localhost')\nGet-{noun} -ComputerName $Ziel" }
            };
            entry.Id = $"perf{i:000}";
            entry.Description = $"Eigener Eintrag für {noun}";
            custom.Add(entry);
        }
        return new PerfDataset(root, seed, files, custom);
    }

    public string FullPathOf(PerfFile f) => Path.Combine(Root, f.RelativePath);

    /// <summary>Ordner, Gruppen, Zuordnungen, Schlagworte, Favoriten, Startstatistik und eigene Einträge übernehmen.</summary>
    public void ApplyTo(AppSettings s, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var rnd = new Random(_seed + 1);
        // Die Ordnergruppe entsteht über den Ordner selbst (GroupManager.Create würde „Scripte 2“ anlegen)
        s.Folders.Add(new FolderConfig { Path = Root, Recursive = true, Group = FolderGroup, SubfoldersAsGroups = true });
        if (!s.CategoryOrder.Contains(FolderGroup, StringComparer.CurrentCultureIgnoreCase)) s.CategoryOrder.Add(FolderGroup);
        foreach (var g in OwnGroups)
        {
            var parent = GroupManager.ParentOf(g);
            GroupManager.Create(s, GroupManager.LeafOf(g), parent);
        }

        var paths = Files.Select(FullPathOf).ToList();
        // Jede fünfte Datei in eine eigene Gruppe (≈ 88 Einträge)
        for (int i = 0; i < paths.Count; i += 5) GroupManager.Assign(s, paths[i], OwnGroups[(i / 5) % OwnGroups.Length]);
        // Schlagworte, Notizen und eigene Anzeigenamen für rund 40 % der Dateien
        for (int i = 0; i < paths.Count; i++)
        {
            if (rnd.Next(100) >= 40) continue;
            var entry = s.FindEntry(paths[i]);
            if (entry == null) { entry = new EntryConfig { Path = paths[i] }; s.Entries.Add(entry); }
            int count = 1 + rnd.Next(3);
            for (int t = 0; t < count; t++)
            {
                var tag = TagPool[rnd.Next(TagPool.Length)];
                if (!entry.Tags.Contains(tag)) entry.Tags.Add(tag);
            }
            if (rnd.Next(4) == 0) entry.Notes = "Nur nach Rücksprache mit dem Team ausführen";
        }

        s.CustomEntries.AddRange(CustomEntries.Select(c => c.Clone()));
        for (int i = 0; i < CustomEntries.Count; i += 4)
            GroupManager.Assign(s, CustomEntries[i].Key, OwnGroups[(i / 4) % OwnGroups.Length]);

        var keys = paths.Concat(CustomEntries.Select(c => c.Key)).ToList();
        for (int i = 0; i < 25; i++) s.Favorites.Add(keys[(i * 37) % keys.Count]);
        for (int i = 0; i < 120; i++)
        {
            var key = keys[(i * 53 + 7) % keys.Count];
            s.RunStats[key] = new RunStat { Score = 1 + rnd.Next(20), Count = 1 + rnd.Next(40), LastUtc = now.AddDays(-rnd.Next(60)) };
        }
        WorkspaceManager.EnsureInitialized(s);
    }

    /// <summary>Einträge, wie sie der Scan liefern würde (ohne Datenträger).</summary>
    public List<LaunchItem> CreateItems() => Files.Select(f => new LaunchItem
    {
        FullPath = FullPathOf(f),
        RootFolder = Root,
        Type = f.Type,
        DisplayName = Path.GetFileNameWithoutExtension(f.RelativePath),
        Synopsis = f.Synopsis,
        Description = f.Description,
        LastWriteTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    }).ToList();

    /// <summary>Dateien auf den Datenträger schreiben (Selbsttest: echter Scan).</summary>
    public void WriteFiles()
    {
        foreach (var f in Files)
        {
            var path = FullPathOf(f);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var content = f.Type switch
            {
                ItemType.PowerShell => $"<#\n.SYNOPSIS\n{f.Synopsis}\n" + (f.Description.Length > 0 ? $".DESCRIPTION\n{f.Description}\n" : string.Empty) +
                                       "#>\nparam([string]$ComputerName = 'localhost')\nWrite-Output $ComputerName\n",
                ItemType.Batch => $"@echo off\r\nrem {f.Synopsis}\r\necho ok\r\n",
                _ => string.Empty
            };
            File.WriteAllText(path, content);
        }
    }

    private static string NounOf(string name) => name[(name.IndexOf('-') + 1)..].TrimEnd('2', '3', '4', '5', '6', '7', '8');

    private static string VerbText(string name) => name[..name.IndexOf('-')] switch
    {
        "Get" => "Zeigt",
        "Set" => "Setzt",
        "Reset" => "Setzt zurück",
        "New" => "Legt an",
        "Remove" => "Entfernt",
        "Test" => "Prüft",
        "Start" => "Startet",
        "Stop" => "Stoppt",
        "Restart" => "Startet neu",
        "Export" => "Exportiert",
        "Import" => "Importiert",
        "Sync" => "Gleicht ab",
        "Clear" => "Leert",
        "Update" => "Aktualisiert",
        _ => "Führt aus"
    };
}
