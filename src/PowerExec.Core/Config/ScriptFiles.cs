using System.Text;

namespace PowerExec.Core.Config;

/// <summary>Script-Dateien aus dem Eintrags-Editor speichern – nie ohne Sicherung des alten Stands.</summary>
public static class ScriptFiles
{
    /// <summary>Ordner der Sicherungen (%LOCALAPPDATA%\Power-Exec\Backups).</summary>
    public static string BackupFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec", "Backups");

    /// <summary>
    /// Text in die Datei schreiben; vorher eine Kopie des bisherigen Stands unter <see cref="BackupFolder"/> ablegen
    /// („Name-JJJJMMTT-hhmmss.ps1“). Die Kodierung (BOM ja/nein) bleibt erhalten.
    /// </summary>
    /// <returns>Pfad der Sicherung.</returns>
    public static string SaveWithBackup(string path, string text, DateTime? now = null)
    {
        Directory.CreateDirectory(BackupFolder);
        var stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var backup = Path.Combine(BackupFolder, $"{Path.GetFileNameWithoutExtension(path)}-{stamp}{Path.GetExtension(path)}");
        for (int i = 2; File.Exists(backup); i++)
            backup = Path.Combine(BackupFolder, $"{Path.GetFileNameWithoutExtension(path)}-{stamp}-{i}{Path.GetExtension(path)}");
        File.Copy(path, backup);
        var bytes = File.ReadAllBytes(path);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        File.WriteAllText(path, text, new UTF8Encoding(bom));
        return backup;
    }
}
