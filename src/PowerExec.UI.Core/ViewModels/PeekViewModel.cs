using PowerExec.Core.Config;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;

namespace PowerExec.ViewModels;

/// <summary>Zeile in der Vorschau: Bezeichnung und Wert.</summary>
public sealed record PeekFact(string Label, string Value);

/// <summary>Parameter in der Vorschau.</summary>
public sealed record PeekParameter(string Name, string Type, bool IsMandatory, string Default, string Help)
{
    public bool HasHelp => Help.Length > 0;
    public string Summary => (Type.Length > 0 ? Type : "beliebig") + (IsMandatory ? " · Pflicht" : Default.Length > 0 ? " · Vorgabe " + Default : string.Empty);
}

/// <summary>
/// Schnellansicht eines Eintrags (Leertaste): Code mit Syntaxfarben, Parameter, Signatur und letzter Start –
/// ohne den Editor zu öffnen.
/// </summary>
public sealed class PeekViewModel
{
    public PeekViewModel(IPanelHost app, ItemViewModel item)
    {
        Item = item;
        item.LoadDetails();
        var li = item.Item;
        var facts = new List<PeekFact>();

        // Code bzw. Inhalt
        string? code = null;
        bool truncated = false;
        // {{$Variablen}} mit ihren Werten zeigen (Geheimes verdeckt, nichts wird ausgeführt)
        string Vars(string? text) => Placeholders.PreviewVariables(text, app.VariablePreview);
        if (li.Source is { Kind: CustomEntryKind.Snippet } snippet)
            (code, truncated) = CodePreview.Head(Vars(snippet.Code));
        else if ((li.Type is ItemType.PowerShell or ItemType.Batch) && (!li.IsCustom || li.CustomKind == CustomEntryKind.File))
        {
            if (CodePreview.ReadHead(li.FullPath) is { } head) (code, truncated) = head;
            else facts.Add(new PeekFact("Inhalt", "Datei nicht lesbar oder zu groß für die Vorschau"));
        }
        if (code != null)
        {
            var isPs = li.Type == ItemType.PowerShell || li.Source is { Kind: CustomEntryKind.Snippet, Language: SnippetLanguage.PowerShell };
            Code = li.Type == ItemType.Batch ? CodePreview.TokenizeBatch(code)
                : isPs ? CodePreview.TokenizePowerShell(code)
                : new[] { new CodeSegment(code, CodeTokenKind.Text) };
            CodeTruncated = truncated;
        }

        // Fakten
        if (li.IsCustom && li.CustomKind != CustomEntryKind.File)
        {
            var target = Vars(((li.LaunchTarget ?? string.Empty) + " " + li.LaunchArguments).Trim());
            if (target.Length > 0 && li.CustomKind != CustomEntryKind.Snippet) facts.Add(new PeekFact("Ziel", target));
        }
        else facts.Add(new PeekFact("Pfad", li.FullPath));
        if (li.Source is { } src && Placeholders.VariableNames(src.Target, src.Arguments, src.Code, src.User, src.RemoteCommand, src.WorkingDirectory) is { Count: > 0 } used)
            facts.Add(new PeekFact("Variablen", string.Join(" · ", used.Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Select(n => $"{n} = {app.VariablePreview(n)}"))));
        if (li.RequiresAdmin) facts.Add(new PeekFact("Rechte", "benötigt Administrator (#Requires)"));
        if (li.RequiredPsVersion.Length > 0) facts.Add(new PeekFact("PowerShell", li.RequiredPsVersion + " oder neuer"));
        if (li.RequiredModules.Count > 0) facts.Add(new PeekFact("Module", string.Join(", ", li.RequiredModules)));
        if (li.IsBlocked) facts.Add(new PeekFact("Blockiert", item.BlockedText));
        if (li.UsesReadHost) facts.Add(new PeekFact("Hinweis", "verwendet Read-Host – remote nicht geeignet"));
        if (item.HasNotes) facts.Add(new PeekFact("Notiz", item.Notes));
        if (li.LastWriteTimeUtc > DateTime.MinValue.AddYears(1) && !li.IsCustom)
            facts.Add(new PeekFact("Geändert", li.LastWriteTimeUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm")));
        var stat = app.Settings.RunStats.FirstOrDefault(kv => kv.Key.Equals(li.FullPath, StringComparison.OrdinalIgnoreCase)).Value;
        if (stat != null && stat.Count > 0)
            facts.Add(new PeekFact("Gestartet", $"{stat.Count}× · zuletzt {RecentRunList.Ago(stat.LastUtc)}"));
        Facts = facts;

        Parameters = li.Parameters.Select(p => new PeekParameter("-" + p.Name, p.TypeName, p.IsMandatory, p.DefaultValueText ?? string.Empty,
            p.ValidValues.Count > 0 ? (p.HelpText.Length > 0 ? p.HelpText + " · " : string.Empty) + "Werte: " + string.Join(", ", p.ValidValues) : p.HelpText)).ToList();
    }

    public ItemViewModel Item { get; }
    public IReadOnlyList<PeekFact> Facts { get; }
    public IReadOnlyList<PeekParameter> Parameters { get; }
    public bool HasParameters => Parameters.Count > 0;
    public IReadOnlyList<CodeSegment> Code { get; } = Array.Empty<CodeSegment>();
    public bool HasCode => Code.Count > 0;
    public bool CodeTruncated { get; }
    public string Description => Item.Item.Description.Length > 0 ? Item.Item.Description : Item.Synopsis;
}
