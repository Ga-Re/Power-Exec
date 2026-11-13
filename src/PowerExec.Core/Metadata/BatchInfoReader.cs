using System.Text;
using PowerExec.Core.Models;

namespace PowerExec.Core.Metadata;

/// <summary>Liest die erste Kommentarzeile (REM oder ::) einer .cmd/.bat-Datei als Synopsis.</summary>
public static class BatchInfoReader
{
    public static void Fill(LaunchItem item)
    {
        try
        {
            using var reader = new StreamReader(item.FullPath, Encoding.Default, detectEncodingFromByteOrderMarks: true);
            item.Synopsis = ReadSynopsis(reader);
        }
        catch
        {
            // Datei nicht lesbar: keine Synopsis.
        }
    }

    public static string ReadSynopsis(TextReader reader)
    {
        for (int i = 0; i < 30; i++)
        {
            var line = reader.ReadLine();
            if (line == null) break;
            var t = line.Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith('@')) t = t[1..].TrimStart();
            if (t.StartsWith("echo off", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("setlocal", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("chcp", StringComparison.OrdinalIgnoreCase))
                continue;

            string? comment = null;
            if (t.StartsWith("::", StringComparison.Ordinal)) comment = t[2..];
            else if (t.Equals("rem", StringComparison.OrdinalIgnoreCase)) continue;
            else if (t.StartsWith("rem ", StringComparison.OrdinalIgnoreCase)) comment = t[4..];

            if (comment == null) break; // erste echte Anweisung: Suche beenden
            comment = comment.Trim().TrimStart('-', '=', '*', '#').Trim();
            if (comment.Length > 0) return comment;
        }
        return string.Empty;
    }
}
