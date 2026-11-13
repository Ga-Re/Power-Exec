using PowerExec.Core.Localization;
using System.Management.Automation.Language;

namespace PowerExec.Core.Config;

/// <summary>Feld eines eigenen Eintrags, zu dem ein Hinweis gehört.</summary>
public enum EntryField { Target, Code, Port, User, KeyFile, RdpFile, Gateway, ScreenSize, PortForward, OpenWithProgram, WorkingDirectory, General }

/// <summary>Ein Hinweis zur Eingabe; <see cref="IsError"/> = Speichern nicht möglich.</summary>
public sealed record EntryIssue(EntryField Field, string Message, bool IsError = true);

/// <summary>Syntaxfehler in einem PowerShell-Snippet (Zeile ab 1).</summary>
public sealed record SyntaxIssue(int Line, int Column, string Message);

/// <summary>Live-Prüfung eigener Einträge (Pflichtfelder, Formate) – ohne Netzwerk- oder Dateizugriff.</summary>
public static class EntryValidation
{
    public static IReadOnlyList<EntryIssue> Check(CustomEntry e)
    {
        var list = new List<EntryIssue>();
        var target = e.Target.Trim();
        switch (e.Kind)
        {
            case CustomEntryKind.Url:
                if (target.Length == 0) list.Add(new(EntryField.Target, L.T("Please enter an address.")));
                else if (!LooksLikeUrl(target)) list.Add(new(EntryField.Target, L.T("Not a valid address – e.g. https://intranet.company.local")));
                break;
            case CustomEntryKind.Folder:
                if (target.Length == 0) list.Add(new(EntryField.Target, L.T("Please enter a folder.")));
                if (e.OpenWith == FolderOpener.Program && e.OpenWithProgram.Trim().Length == 0)
                    list.Add(new(EntryField.OpenWithProgram, L.T("Please enter the program that opens the folder.")));
                break;
            case CustomEntryKind.Rdp:
                if (e.UsesRdpFile)
                {
                    if (!e.RdpFile.Trim().EndsWith(".rdp", StringComparison.OrdinalIgnoreCase))
                        list.Add(new(EntryField.RdpFile, L.T("Please choose an .rdp file.")));
                }
                else if (target.Length == 0) list.Add(new(EntryField.Target, L.T("Please enter a computer – or choose an .rdp file.")));
                else if (target.Contains(' ')) list.Add(new(EntryField.Target, L.T("Computer names don't contain spaces.")));
                if (e.Screen == RdpScreen.Window && (e.ScreenWidth is > 0 and < 640 || e.ScreenHeight is > 0 and < 480))
                    list.Add(new(EntryField.ScreenSize, L.T("At least 640 × 480.")));
                if (e.UsesRdpFile && (e.User.Trim().Length > 0 || e.Gateway.Trim().Length > 0))
                    list.Add(new(EntryField.RdpFile, L.T("With an .rdp file, user and gateway come from the file."), IsError: false));
                break;
            case CustomEntryKind.Ssh:
                if (target.Length == 0) list.Add(new(EntryField.Target, L.T("Please enter a computer.")));
                else if (target.Contains(' ') || target.Contains('@')) list.Add(new(EntryField.Target, L.T("Only enter the computer – the user goes in the “User” field.")));
                foreach (var f in e.PortForward.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!IsForward(f)) { list.Add(new(EntryField.PortForward, L.F("“{0}” doesn't have the format port:target:port, e.g. 8080:localhost:80.", f))); break; }
                foreach (var w in e.SshWarnings()) list.Add(new(EntryField.General, w, IsError: false));
                break;
            case CustomEntryKind.Snippet:
                if (e.Code.Trim().Length == 0) list.Add(new(EntryField.Code, L.T("The code is missing.")));
                else if (e.Language == SnippetLanguage.PowerShell && CheckSyntax(e.Code) is { Count: > 0 } errors)
                    list.Add(new(EntryField.Code, L.F("Line {0}: {1}", errors[0].Line, errors[0].Message) + (errors.Count > 1 ? L.F(" (+{0} more)", errors.Count - 1) : string.Empty)));
                break;
            case CustomEntryKind.Tool:
                if (target.Length == 0) list.Add(new(EntryField.Target, L.T("Please choose or enter a tool.")));
                break;
            case CustomEntryKind.Command:
                if (target.Length == 0) list.Add(new(EntryField.Target, L.T("Please enter the program.")));
                break;
            case CustomEntryKind.File:
                if (target.Length == 0) list.Add(new(EntryField.Target, L.T("Please choose a file.")));
                else if (!Path.IsPathRooted(target) && !target.StartsWith("{{", StringComparison.Ordinal)) list.Add(new(EntryField.Target, L.T("Please enter the full path.")));
                break;
        }
        if (e.Kind is CustomEntryKind.Rdp or CustomEntryKind.Ssh && e.Port is < 0 or > 65535)
            list.Add(new(EntryField.Port, L.T("Port between 1 and 65535.")));
        return list;
    }

    /// <summary>Syntaxfehler im PowerShell-Code (PowerShell-Parser, ohne Ausführung).</summary>
    public static IReadOnlyList<SyntaxIssue> CheckSyntax(string code)
    {
        try
        {
            Parser.ParseInput(code, out _, out var errors);
            return errors.Select(x => new SyntaxIssue(x.Extent.StartLineNumber, x.Extent.StartColumnNumber, x.Message)).ToList();
        }
        catch
        {
            return Array.Empty<SyntaxIssue>();
        }
    }

    private static bool LooksLikeUrl(string t)
    {
        if (t.Contains("{{")) return true; // Platzhalter: erst beim Start vollständig
        return Uri.TryCreate(t, UriKind.Absolute, out var u) && u.Scheme.Length > 1 && !t.Contains(' ');
    }

    private static bool IsForward(string f)
    {
        var parts = f.Split(':');
        return parts.Length is 3 or 4 && int.TryParse(parts[0], out _) && int.TryParse(parts[^1], out _) && parts[^2].Length > 0;
    }
}
