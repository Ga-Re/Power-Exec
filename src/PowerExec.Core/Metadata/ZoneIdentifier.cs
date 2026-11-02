namespace PowerExec.Core.Metadata;

/// <summary>Auswertung der Zonen-Kennung (Datenstrom „Zone.Identifier“) heruntergeladener Dateien.</summary>
public static class ZoneIdentifier
{
    public const string StreamName = ":Zone.Identifier";

    /// <summary>Liest „ZoneId=3“ aus dem Inhalt des Datenstroms.</summary>
    public static int? ParseZoneId(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase) && int.TryParse(line[7..], out var zone))
                return zone;
        }
        return null;
    }

    /// <summary>Zone 3 = Internet, 4 = eingeschränkte Sites.</summary>
    public static bool IsBlocked(int? zoneId) => zoneId >= 3;

    public static string Describe(int? zoneId) => zoneId switch
    {
        3 => "aus dem Internet heruntergeladen",
        4 => "aus einer eingeschränkten Zone",
        _ => "aus einer nicht vertrauenswürdigen Quelle"
    };
}
