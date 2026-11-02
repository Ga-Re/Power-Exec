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

    /// <summary>true = automatisch erzeugtes Profil ohne gespeicherte Konfiguration.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsImplicit { get; set; }

    public LaunchProfile Clone() => new()
    {
        Name = Name,
        IsDefault = IsDefault,
        RunAsAdmin = RunAsAdmin,
        Engine = Engine,
        NoExit = NoExit,
        NoProfile = NoProfile,
        Target = Target,
        Machines = new List<string>(Machines),
        Dialog = Dialog,
        Parameters = Parameters.ToDictionary(kv => kv.Key, kv => kv.Value.Clone(), StringComparer.OrdinalIgnoreCase),
        Arguments = Arguments,
        IsImplicit = IsImplicit
    };
}

/// <summary>Einstellungen eines einzelnen Eintrags (Script oder Programm).</summary>
public sealed class EntryConfig
{
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<LaunchProfile> Profiles { get; set; } = new();

    public EntryConfig Clone() => new()
    {
        Path = Path,
        DisplayName = DisplayName,
        Description = Description,
        Profiles = Profiles.Select(p => p.Clone()).ToList()
    };
}
