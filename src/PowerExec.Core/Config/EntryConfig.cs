namespace PowerExec.Core.Config;

public enum ProfileTarget { Local, Fixed, Ask }

public enum DialogMode { Always, WhenNeeded, Never }

public enum ParameterMode { Fixed, Preset, Ask }

public sealed class ParameterSetting
{
    public ParameterMode Mode { get; set; } = ParameterMode.Ask;
    public string Value { get; set; } = string.Empty;

    public ParameterSetting Clone() => new() { Mode = Mode, Value = Value };
}

/// <summary>Eine gespeicherte Startkonfiguration eines Eintrags.</summary>
public sealed class LaunchProfile
{
    public string Name { get; set; } = "Standard";
    public bool IsDefault { get; set; }
    public bool RunAsAdmin { get; set; }
    /// <summary>Als anderer Benutzer starten (Anmeldung wird beim Start abgefragt, nie gespeichert).</summary>
    public bool RunAsOtherUser { get; set; }
    public PsEngine Engine { get; set; } = PsEngine.PowerShell7;
    public bool NoExit { get; set; } = true;
    public bool NoProfile { get; set; } = true;
    public ProfileTarget Target { get; set; } = ProfileTarget.Local;
    public List<string> Machines { get; set; } = new();
    public DialogMode Dialog { get; set; } = DialogMode.WhenNeeded;

    /// <summary>Parameter-Einstellungen (nur PowerShell). Schlüssel: Parametername.</summary>
    public Dictionary<string, ParameterSetting> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Zusätzliche Argumente für EXE, Verknüpfungen, Batch und Snap-ins.</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>Vor dem Start eine Sicherheitsabfrage zeigen.</summary>
    public bool ConfirmBeforeRun { get; set; }

    /// <summary>true = automatisch erzeugtes Profil ohne gespeicherte Konfiguration.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsImplicit { get; set; }

    public LaunchProfile Clone() => new()
    {
        Name = Name,
        IsDefault = IsDefault,
        RunAsAdmin = RunAsAdmin,
        RunAsOtherUser = RunAsOtherUser,
        Engine = Engine,
        NoExit = NoExit,
        NoProfile = NoProfile,
        Target = Target,
        Machines = new List<string>(Machines),
        Dialog = Dialog,
        Parameters = Parameters.ToDictionary(kv => kv.Key, kv => kv.Value.Clone(), StringComparer.OrdinalIgnoreCase),
        Arguments = Arguments,
        ConfirmBeforeRun = ConfirmBeforeRun,
        IsImplicit = IsImplicit
    };
}

/// <summary>Einstellungen eines einzelnen Eintrags (Script oder Programm).</summary>
public sealed class EntryConfig
{
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Freitext-Notiz (z. B. Hinweise oder Link zur Doku).</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Eigene Gruppe im Panel als Pfad („Server/Exchange“), leer = keine.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Schlagworte für Suche („#tag“) und Filter.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Ein Klick im Panel startet sofort das Standardprofil (statt aufzuklappen).</summary>
    public bool SingleClick { get; set; }

    public List<LaunchProfile> Profiles { get; set; } = new();

    /// <summary>Nichts eingestellt – die Konfiguration muss nicht aufbewahrt werden.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => string.IsNullOrEmpty(Category) && Profiles.Count == 0 && Tags.Count == 0 && !SingleClick &&
                           string.IsNullOrWhiteSpace(DisplayName) && string.IsNullOrWhiteSpace(Description) && string.IsNullOrWhiteSpace(Notes);

    public EntryConfig Clone() => new()
    {
        Path = Path,
        DisplayName = DisplayName,
        Description = Description,
        Category = Category,
        Notes = Notes,
        Tags = new List<string>(Tags),
        SingleClick = SingleClick,
        Profiles = Profiles.Select(p => p.Clone()).ToList()
    };
}

/// <summary>Ein Eintrag in „Zuletzt ausgeführt“.</summary>
public sealed class RecentRun
{
    public string Path { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public DateTime TimeUtc { get; set; }
}
