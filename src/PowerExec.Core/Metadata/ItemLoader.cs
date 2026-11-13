using PowerExec.Core.Localization;
using PowerExec.Core.Models;

namespace PowerExec.Core.Metadata;

/// <summary>Liefert Windows-spezifische Infos (EXE-Dateibeschreibung, Verknüpfungsziel).</summary>
public interface IPlatformMetadata
{
    void FillExecutable(LaunchItem item);
    void FillShortcut(LaunchItem item);
}

/// <summary>Erstellt einen <see cref="LaunchItem"/> samt Anzeigename und Info-Text.</summary>
public sealed class ItemLoader
{
    private readonly IPlatformMetadata? _platform;

    public ItemLoader(IPlatformMetadata? platform = null) => _platform = platform;

    public LaunchItem Load(string fullPath, string rootFolder)
    {
        var item = new LaunchItem
        {
            FullPath = fullPath,
            RootFolder = rootFolder,
            Type = LaunchItem.TypeFromExtension(fullPath)
        };
        try { item.LastWriteTimeUtc = File.GetLastWriteTimeUtc(fullPath); } catch { /* Datei inzwischen weg oder gesperrt: ohne Zeitstempel weiter */ }

        item.DisplayName = item.Type == ItemType.PowerShell || item.Type == ItemType.Batch
            ? item.FileName
            : Path.GetFileNameWithoutExtension(fullPath);

        try
        {
            switch (item.Type)
            {
                case ItemType.PowerShell:
                    ScriptInfoReader.Fill(item);
                    break;
                case ItemType.Batch:
                    BatchInfoReader.Fill(item);
                    break;
                case ItemType.Executable:
                    _platform?.FillExecutable(item);
                    break;
                case ItemType.Shortcut:
                    _platform?.FillShortcut(item);
                    break;
                case ItemType.ConsoleSnapIn:
                    item.Synopsis = L.F("Snap-in ({0})", item.FileName);
                    break;
            }
        }
        catch (Exception ex)
        {
            item.ParseError = ex.Message;
        }

        if (string.IsNullOrWhiteSpace(item.Synopsis) && item.Type == ItemType.PowerShell && item.ParseError != null)
            item.Synopsis = L.T("Script contains errors: ") + item.ParseError;
        return item;
    }
}
