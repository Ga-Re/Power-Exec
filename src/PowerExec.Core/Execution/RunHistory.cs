using System.Text.Json;
using System.Text.Json.Serialization;
using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Execution;

public enum HistoryResult { Started, Running, Success, Warning, Error, Cancelled }

/// <summary>Ein Start im Verlauf: was, wie, wohin, mit welchen Werten und mit welchem Ergebnis (nie mit Kennwörtern).</summary>
public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public ExecutionMode Mode { get; set; }
    public PsEngine Engine { get; set; } = PsEngine.WindowsPowerShell;
    public List<string> Targets { get; set; } = new();
    public List<string> FailedTargets { get; set; } = new();
    /// <summary>Parameterwerte als Text (wie im Dialog eingegeben); Kennwörter fehlen.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public HistoryResult Result { get; set; } = HistoryResult.Started;
    public int? ExitCode { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? LogFile { get; set; }
    /// <summary>Teil eines Ablaufs (C3) oder eines Zeitplans (D4), sonst leer.</summary>
    public string Origin { get; set; } = string.Empty;

    [JsonIgnore] public bool IsRemote => Targets.Count > 0;
    [JsonIgnore] public TimeSpan? Duration => EndUtc is { } e ? e - StartUtc : null;
}

/// <summary>Verlauf aller Starts (eigene Datei neben dem Protokoll, höchstens <see cref="MaxEntries"/> Einträge).</summary>
public sealed class RunHistory
{
    public const int MaxEntries = 500;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false, Converters = { new JsonStringEnumConverter() } };

    public List<HistoryEntry> Entries { get; set; } = new();

    public static RunHistory Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new RunHistory();
            return JsonSerializer.Deserialize<RunHistory>(File.ReadAllText(path), Options) ?? new RunHistory();
        }
        catch
        {
            return new RunHistory(); // beschädigte Datei: neu beginnen
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, overwrite: true);
    }

    public HistoryEntry Add(HistoryEntry entry)
    {
        Entries.Insert(0, entry);
        if (Entries.Count > MaxEntries) Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);
        return entry;
    }

    /// <summary>Neuer Eintrag; Kennwort-, PSCredential- und SecureString-Parameter werden nicht übernommen.</summary>
    public static HistoryEntry Create(LaunchItem item, string name, string profileName, ExecutionMode mode, PsEngine engine,
        IEnumerable<string>? targets, IReadOnlyDictionary<string, string?>? raw, DateTime? nowUtc = null)
    {
        var e = new HistoryEntry
        {
            Path = item.FullPath, Name = name, ProfileName = profileName, Mode = mode, Engine = engine,
            Targets = targets?.ToList() ?? new List<string>(), StartUtc = nowUtc ?? DateTime.UtcNow
        };
        if (raw != null)
            foreach (var p in item.Parameters)
            {
                if (p.IsSecure || p.Kind == ParameterKind.Secure) continue;
                if (raw.TryGetValue(p.Name, out var v) && v != null) e.Parameters[p.Name] = v;
            }
        return e;
    }

    /// <summary>Typisierte Werte (z. B. nach „Fehlgeschlagene wiederholen“) zurück in Text wandeln.</summary>
    public static Dictionary<string, string?> RawFrom(IReadOnlyDictionary<string, object> values) =>
        values.ToDictionary(kv => kv.Key, kv => kv.Value switch
        {
            null => null,
            string s => s,
            bool b => b ? "true" : "false",
            System.Management.Automation.SwitchParameter sw => sw.IsPresent ? "true" : "false",
            System.Collections.IEnumerable list => string.Join(", ", list.Cast<object>()),
            var o => Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture)
        }, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Startprofil, das den Lauf genau wiederholt: alle gespeicherten Werte fest, gleiche Ausführungsart und Ziele.
    /// <paramref name="onlyFailed"/>: nur die Rechner, auf denen es nicht geklappt hat.
    /// </summary>
    public static LaunchProfile ReplayProfile(HistoryEntry e, bool onlyFailed = false)
    {
        var targets = onlyFailed ? e.FailedTargets : e.Targets;
        var p = new LaunchProfile
        {
            Name = "Verlauf",
            RunAsAdmin = e.Mode == ExecutionMode.Administrator,
            RunAsOtherUser = e.Mode == ExecutionMode.OtherUser,
            Engine = e.Engine,
            Target = targets.Count > 0 ? ProfileTarget.Fixed : ProfileTarget.Local,
            Machines = targets.ToList(),
            Dialog = DialogMode.WhenNeeded
        };
        foreach (var (name, value) in e.Parameters) p.Parameters[name] = new ParameterSetting { Mode = ParameterMode.Fixed, Value = value };
        return p;
    }

    /// <summary>Filter für die Verlaufsansicht: „all“, „today“, „errors“, „remote“ und Suchtext.</summary>
    public IEnumerable<HistoryEntry> Filter(string filter, string? search, DateTime? nowLocal = null)
    {
        var today = (nowLocal ?? DateTime.Now).Date;
        IEnumerable<HistoryEntry> q = Entries;
        q = filter switch
        {
            "today" => q.Where(e => e.StartUtc.ToLocalTime().Date == today),
            "errors" => q.Where(e => e.Result is HistoryResult.Error or HistoryResult.Warning),
            "remote" => q.Where(e => e.IsRemote),
            _ => q
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            q = q.Where(e => terms.All(t => (e.Name + " " + e.ProfileName + " " + string.Join(' ', e.Targets) + " " + e.Summary + " " + string.Join(' ', e.Parameters.Values))
                .Contains(t, StringComparison.CurrentCultureIgnoreCase)));
        }
        return q;
    }

    /// <summary>Beim Umbenennen/Verschieben einer Datei mitwandern.</summary>
    public void Rename(string oldPath, string newPath)
    {
        foreach (var e in Entries.Where(e => e.Path.Equals(oldPath, StringComparison.OrdinalIgnoreCase))) e.Path = newPath;
    }
}
