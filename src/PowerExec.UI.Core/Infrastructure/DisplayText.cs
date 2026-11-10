namespace PowerExec.Infrastructure;

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
}
