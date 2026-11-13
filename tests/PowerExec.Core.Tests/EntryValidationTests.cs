using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;

namespace PowerExec.Core.Tests;

/// <summary>Prüfung der Eingaben eigener Einträge (Syntax, Adresse).</summary>
public class EntryValidationTests
{
    [Fact]
    public void SnippetSyntaxCheck()
    {
        Assert.Empty(EntryValidation.CheckSyntax("Get-Date | Select-Object Year"));
        var errors = EntryValidation.CheckSyntax("if ($true) {\n  Get-Date\n");
        Assert.NotEmpty(errors);
        var e = new CustomEntry { Kind = CustomEntryKind.Snippet, Code = "foreach ($x in" };
        Assert.Contains(EntryValidation.Check(e), i => i.Field == EntryField.Code && i.IsError);
    }

    [Fact]
    public void UrlValidation()
    {
        Assert.Contains(EntryValidation.Check(new CustomEntry { Kind = CustomEntryKind.Url, Target = "intranet firma" }), i => i.IsError);
        Assert.Empty(EntryValidation.Check(new CustomEntry { Kind = CustomEntryKind.Url, Target = "https://ticket/{{Nr}}" }));
    }
}
