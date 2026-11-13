namespace PowerExec.UI.Infrastructure;

/// <summary>Anzeigetexte, die mehrere Oberflächen gleich formatieren.</summary>
public static class DisplayText
{
    /// <summary>Dauer kurz: „4,2 s“, „3:07 min“, „1:05 h“.</summary>
    public static string Duration(TimeSpan t) =>
        t.TotalSeconds < 60 ? (PowerExec.Core.Localization.L.IsEnglish ? $"{t.TotalSeconds:0.0} s" : $"{t.TotalSeconds:0.0} s".Replace('.', ','))
        : t.TotalHours < 1 ? $"{(int)t.TotalMinutes}:{t.Seconds:00} min"
        : $"{(int)t.TotalHours}:{t.Minutes:00} h";

    /// <summary>Tastenkürzel in der Oberflächensprache: deutsch „Ctrl+Alt+Space“ → „Strg+Alt+Leertaste“, englisch unverändert.</summary>
    public static string Hotkey(string hotkey) => PowerExec.Core.Localization.L.IsEnglish ? hotkey :
        string.Join("+", hotkey.Split('+').Select(p => p.Trim() switch
        {
            "Ctrl" => "Strg",
            "Shift" => "Umschalt",
            "Space" => "Leertaste",
            var x => x
        }));

    /// <summary>Unsichtbares Leerzeichen: hier darf eine Zeile umbrechen.</summary>
    public const char SoftBreak = '\u200B';

    /// <summary>
    /// Namen für schmale Flächen (Kacheln) umbrechbar machen: nach „-“, „_“ und „.“ darf die Zeile umbrechen
    /// („Get-DiskSpace“ → „Get-“ / „DiskSpace“ statt „Get-DiskS…“). Am Ende und vor einem weiteren Trennzeichen nicht.
    /// </summary>
    public static string WithSoftBreaks(string text)
    {
        if (text.Length < 2 || text.AsSpan().IndexOfAny('-', '_', '.') < 0) return text;
        var sb = new System.Text.StringBuilder(text.Length + 4);
        for (int i = 0; i < text.Length; i++)
        {
            sb.Append(text[i]);
            if (text[i] is '-' or '_' or '.' && i > 0 && i < text.Length - 1 && char.IsLetterOrDigit(text[i + 1])) sb.Append(SoftBreak);
        }
        return sb.ToString();
    }
}
