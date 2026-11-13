using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Vorschau mit Syntaxfarben.</summary>
public class CodePreviewTests
{
    [Fact]
    public void CodePreviewColorsPowerShell()
    {
        const string code = "param([string]$Name)\nGet-Item 'x' -Force # Kommentar";
        var segments = PowerExec.Core.Metadata.CodePreview.TokenizePowerShell(code);
        Assert.Equal(code, string.Concat(segments.Select(s => s.Text)));
        bool Has(string text, PowerExec.Core.Metadata.CodeTokenKind kind) => segments.Any(s => s.Kind == kind && s.Text.Contains(text));
        Assert.True(Has("param", PowerExec.Core.Metadata.CodeTokenKind.Keyword));
        Assert.True(Has("$Name", PowerExec.Core.Metadata.CodeTokenKind.Variable));
        Assert.True(Has("Get-Item", PowerExec.Core.Metadata.CodeTokenKind.Command));
        Assert.True(Has("'x'", PowerExec.Core.Metadata.CodeTokenKind.String));
        Assert.True(Has("-Force", PowerExec.Core.Metadata.CodeTokenKind.Parameter));
        Assert.True(Has("# Kommentar", PowerExec.Core.Metadata.CodeTokenKind.Comment));
    }

    [Fact]
    public void CodePreviewMarksBatchCommentsAndHead()
    {
        var segments = PowerExec.Core.Metadata.CodePreview.TokenizeBatch("@echo off\nrem Räumt auf\n:: auch Kommentar\n:ende");
        Assert.Contains(segments, s => s.Kind == PowerExec.Core.Metadata.CodeTokenKind.Comment && s.Text.Contains("rem Räumt auf") && s.Text.Contains(":: auch"));
        Assert.Contains(segments, s => s.Kind == PowerExec.Core.Metadata.CodeTokenKind.Keyword && s.Text == ":ende");

        var (head, truncated) = PowerExec.Core.Metadata.CodePreview.Head(string.Join("\n", Enumerable.Range(1, 100)), 10);
        Assert.True(truncated);
        Assert.Equal(10, head.Split('\n').Length);
    }
}
