using System.Text;
using PowerExec.Core.Localization;
using PowerExec.Core.Updates;

// CHANGELOG.md aus ReleaseNotes.All (deutsch). Aufruf aus dem Wurzelordner: dotnet run --project tools/Changelog
L.Use("de");
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "PowerExec.sln")))
    root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("PowerExec.sln nicht gefunden.");

var text = new StringBuilder();
text.AppendLine("# Änderungen");
text.AppendLine();
text.AppendLine("Erzeugt aus `src/PowerExec.Core/Updates/ReleaseNotes.cs` (`dotnet run --project tools/Changelog`) – nicht von Hand bearbeiten.");
foreach (var note in ReleaseNotes.All.OrderByDescending(n => n.Version))
{
    text.AppendLine();
    text.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"## {note.Version.ToString(3)} – {note.Title}");
    text.AppendLine();
    foreach (var line in note.Highlights) text.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- {line}");
}
File.WriteAllText(Path.Combine(root, "CHANGELOG.md"), text.ToString().ReplaceLineEndings("\n"));
Console.WriteLine($"CHANGELOG.md: {ReleaseNotes.All.Count} Versionen");
