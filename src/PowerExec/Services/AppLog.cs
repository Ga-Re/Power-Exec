using System.Collections.Concurrent;
using System.Text;

namespace PowerExec.Services;

/// <summary>
/// Einfaches App-Protokoll: hält die letzten Einträge im Speicher (für die Diagnose-Ansicht)
/// und schreibt sie nach %LOCALAPPDATA%\Power-Exec\app.log (max. ~1 MB, danach app.log.1).
/// </summary>
public static class AppLog
{
    private const int MaxLines = 1000;
    private const long MaxFileBytes = 1024 * 1024;
    private static readonly ConcurrentQueue<string> Lines = new();
    private static readonly object FileLock = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power-Exec", "app.log");

    public static event Action? Changed;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        Lines.Enqueue(line);
        while (Lines.Count > MaxLines && Lines.TryDequeue(out _)) { }
        try
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxFileBytes)
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Protokoll darf die App nie stören
        }
        Changed?.Invoke();
    }

    public static string Recent(int max = 500)
    {
        var all = Lines.ToArray();
        return string.Join(Environment.NewLine, all.Skip(Math.Max(0, all.Length - max)));
    }
}
