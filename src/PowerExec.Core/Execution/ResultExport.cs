using PowerExec.Core.Localization;
using System.Text;

namespace PowerExec.Core.Execution;

/// <summary>CSV-Export und Logdatei für Ergebnisse einer (Massen-)Ausführung.</summary>
public static class ResultExport
{
    public static string StatusText(RemoteStatus s) => s switch
    {
        RemoteStatus.Success => L.T("Erfolgreich"),
        RemoteStatus.Error => L.T("Fehler"),
        RemoteStatus.Unreachable => L.T("Nicht erreichbar"),
        _ => L.T("Läuft")
    };

    /// <summary>CSV mit Semikolon (öffnet sich in deutschem Excel direkt korrekt).</summary>
    public static string ToCsv(IEnumerable<RemoteResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine(L.T("Rechner;Status;Dauer (s);Fehler;Hinweis;Ausgabe"));
        foreach (var r in results)
        {
            sb.Append(Field(r.Computer)).Append(';')
              .Append(Field(StatusText(r.Status))).Append(';')
              .Append(Field((r.DurationMs / 1000.0).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("de-DE")))).Append(';')
              .Append(Field(r.Error)).Append(';')
              .Append(Field(r.Note)).Append(';')
              .Append(Field(r.Output))
              .AppendLine();
        }
        return sb.ToString();
    }

    public static string ToLog(string scriptPath, string runAs, string engine, DateTime started, IEnumerable<RemoteResult> results)
    {
        var list = results.ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Power-Exec · {started:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(L.F("Script:   {0}", scriptPath));
        sb.AppendLine(L.F("Konto:    {0} (Anmeldedaten nicht gespeichert)", runAs));
        sb.AppendLine($"Engine:   {engine}");
        sb.AppendLine(L.F("Rechner:  {0}", list.Count));
        sb.AppendLine(new string('-', 72));
        foreach (var r in list)
        {
            sb.AppendLine($"{r.Computer,-20} {StatusText(r.Status),-17} {r.DurationMs / 1000.0,6:0.0} s  {r.Error}");
            if (r.Note.Length > 0) sb.AppendLine(L.T("    Hinweis: ") + r.Note);
            if (r.Output.Length > 0)
                foreach (var line in r.Output.Replace("\r", "").Split('\n'))
                    sb.AppendLine("    " + line);
        }
        sb.AppendLine(new string('-', 72));
        sb.AppendLine(L.F("Ende: {0} OK · ", list.Count(r => r.Status == RemoteStatus.Success)) +
                      L.F("{0} Fehler · ", list.Count(r => r.Status == RemoteStatus.Error)) +
                      L.F("{0} nicht erreichbar", list.Count(r => r.Status == RemoteStatus.Unreachable)));
        return sb.ToString();
    }

    private static string Field(string s)
    {
        s ??= string.Empty;
        // Formel-Injection in Excel verhindern.
        if (s.Length > 0 && "=+-@".Contains(s[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
