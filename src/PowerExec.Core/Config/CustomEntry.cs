using PowerExec.Core.Models;

namespace PowerExec.Core.Config;

public enum CustomEntryKind { Url, Folder, Rdp, Command }

/// <summary>Eintrag ohne Datei: Webseite, Ordner/Freigabe, RDP-Verbindung oder Befehl.</summary>
public sealed class CustomEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public CustomEntryKind Kind { get; set; } = CustomEntryKind.Url;

    /// <summary>URL, Ordnerpfad, Rechnername (RDP) bzw. Programm (Befehl).</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Argumente (nur bei Befehl; bei RDP zusätzliche mstsc-Optionen).</summary>
    public string Arguments { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public CustomEntry Clone() => new() { Id = Id, Name = Name, Kind = Kind, Target = Target, Arguments = Arguments, Description = Description };

    public string Key => PanelConfig.CustomEntriesKey + Id;

    public static bool IsCustomKey(string path) => path.StartsWith(PanelConfig.CustomEntriesKey, StringComparison.OrdinalIgnoreCase);

    public static string KindLabel(CustomEntryKind kind) => kind switch
    {
        CustomEntryKind.Url => "Webseite",
        CustomEntryKind.Folder => "Ordner",
        CustomEntryKind.Rdp => "Remotedesktop",
        _ => "Befehl"
    };

    /// <summary>Wandelt den Eintrag in einen startbaren <see cref="LaunchItem"/> um.</summary>
    public LaunchItem ToLaunchItem()
    {
        var target = Target.Trim();
        var (type, launch, args) = Kind switch
        {
            CustomEntryKind.Rdp => (ItemType.Executable, "mstsc.exe", ($"/v:{target} " + Arguments).Trim()),
            CustomEntryKind.Command => (ItemType.Executable, target, Arguments.Trim()),
            _ => (ItemType.Other, target, string.Empty)
        };
        return new LaunchItem
        {
            FullPath = Key,
            RootFolder = PanelConfig.CustomEntriesKey,
            Type = type,
            DisplayName = string.IsNullOrWhiteSpace(Name) ? target : Name.Trim(),
            Synopsis = string.IsNullOrWhiteSpace(Description) ? $"{KindLabel(Kind)} · {target}" : Description.Trim(),
            Description = $"{KindLabel(Kind)}: {target} {Arguments}".Trim(),
            LaunchTarget = launch,
            LaunchArguments = args,
            CustomKind = Kind
        };
    }
}
