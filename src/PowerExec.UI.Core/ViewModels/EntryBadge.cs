using PowerExec.Core.Config;

namespace PowerExec.ViewModels;

/// <summary>Kürzel eines eigenen Eintrags im Panel (URL, DIR, RDP …).</summary>
public static class EntryBadge
{
    public static string For(CustomEntryKind kind, string target = "", SnippetLanguage language = SnippetLanguage.PowerShell) => kind switch
    {
        CustomEntryKind.Url => "URL",
        CustomEntryKind.Folder => "DIR",
        CustomEntryKind.Rdp => "RDP",
        CustomEntryKind.Ssh => "SSH",
        CustomEntryKind.Snippet => language == SnippetLanguage.Text ? "TXT" : "PS",
        CustomEntryKind.Tool => "WIN",
        CustomEntryKind.Workflow => "FLOW",
        CustomEntryKind.File => Path.GetExtension(target).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext : "DAT",
        _ => "CMD"
    };
}
