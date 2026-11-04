using System.Management.Automation.Language;
using System.Text;

namespace PowerExec.Core.Metadata;

public enum CodeTokenKind { Text, Keyword, Variable, String, Comment, Command, Type, Number, Operator, Parameter }

/// <summary>Ein Abschnitt Quelltext mit seiner Art (für die Syntaxfarben in der Vorschau).</summary>
public sealed record CodeSegment(string Text, CodeTokenKind Kind);

/// <summary>Vorschau eines Scripts: die ersten Zeilen, bei PowerShell mit Syntaxfarben.</summary>
public static class CodePreview
{
    public const int DefaultMaxLines = 60;
    public const long MaxFileBytes = 512 * 1024;

    /// <summary>Datei lesen (höchstens <paramref name="maxLines"/> Zeilen); null, wenn sie fehlt oder zu groß ist.</summary>
    public static (string Text, bool Truncated)? ReadHead(string path, int maxLines = DefaultMaxLines)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return null;
            var sb = new StringBuilder();
            int n = 0;
            bool truncated = false;
            foreach (var line in File.ReadLines(path))
            {
                if (n++ >= maxLines) { truncated = true; break; }
                sb.Append(line).Append('\n');
            }
            return (sb.ToString().TrimEnd('\n'), truncated);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Text auf die ersten Zeilen kürzen.</summary>
    public static (string Text, bool Truncated) Head(string text, int maxLines = DefaultMaxLines)
    {
        var lines = (text ?? string.Empty).Replace("\r", "").Split('\n');
        return (string.Join('\n', lines.Take(maxLines)), lines.Length > maxLines);
    }

    /// <summary>PowerShell-Code in farbige Abschnitte zerlegen (über den PowerShell-Tokenizer, auch bei Syntaxfehlern).</summary>
    public static IReadOnlyList<CodeSegment> TokenizePowerShell(string code)
    {
        var result = new List<CodeSegment>();
        if (string.IsNullOrEmpty(code)) return result;
        Token[] tokens;
        var commandStarts = new HashSet<int>();
        try
        {
            var ast = Parser.ParseInput(code, out tokens, out _);
            // Befehlsnamen über den Syntaxbaum (das Token-Merkmal CommandName ist nicht immer gesetzt)
            foreach (var cmd in ast.FindAll(a => a is CommandAst, true).Cast<CommandAst>())
                if (cmd.CommandElements.Count > 0 && cmd.CommandElements[0] is StringConstantExpressionAst name)
                    commandStarts.Add(name.Extent.StartOffset);
        }
        catch { return new[] { new CodeSegment(code, CodeTokenKind.Text) }; }

        int pos = 0;
        foreach (var t in Flatten(tokens))
        {
            int start = t.Extent.StartOffset, end = t.Extent.EndOffset;
            if (start < pos || end > code.Length || end <= start) continue;
            if (start > pos) result.Add(new CodeSegment(code[pos..start], CodeTokenKind.Text));
            result.Add(new CodeSegment(code[start..end], commandStarts.Contains(start) && t is not StringToken { Kind: not TokenKind.Generic } ? CodeTokenKind.Command : KindOf(t)));
            pos = end;
        }
        if (pos < code.Length) result.Add(new CodeSegment(code[pos..], CodeTokenKind.Text));
        return Merge(result);
    }

    /// <summary>Batch-Datei: Kommentarzeilen (REM, ::) und Sprungmarken hervorheben.</summary>
    public static IReadOnlyList<CodeSegment> TokenizeBatch(string code)
    {
        var result = new List<CodeSegment>();
        var lines = (code ?? string.Empty).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart().TrimStart('@');
            var kind = trimmed.StartsWith("::") || trimmed.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("rem", StringComparison.OrdinalIgnoreCase)
                ? CodeTokenKind.Comment
                : trimmed.StartsWith(':') ? CodeTokenKind.Keyword : CodeTokenKind.Text;
            result.Add(new CodeSegment(line + (i < lines.Length - 1 ? "\n" : string.Empty), kind));
        }
        return Merge(result);
    }

    private static IEnumerable<Token> Flatten(IEnumerable<Token> tokens)
    {
        foreach (var t in tokens)
        {
            if (t.Kind == TokenKind.EndOfInput) continue;
            // Ausdrücke in "…$(…)…": den ganzen String als String färben
            yield return t;
        }
    }

    private static CodeTokenKind KindOf(Token t)
    {
        if (t.Kind == TokenKind.Comment) return CodeTokenKind.Comment;
        if (t.Kind is TokenKind.Variable or TokenKind.SplattedVariable) return CodeTokenKind.Variable;
        if (t is StringToken) return CodeTokenKind.String;
        if (t.Kind == TokenKind.Number) return CodeTokenKind.Number;
        if (t.Kind == TokenKind.Parameter) return CodeTokenKind.Parameter;
        if (t.TokenFlags.HasFlag(TokenFlags.Keyword)) return CodeTokenKind.Keyword;
        if (t.TokenFlags.HasFlag(TokenFlags.CommandName)) return CodeTokenKind.Command;
        if (t.TokenFlags.HasFlag(TokenFlags.TypeName)) return CodeTokenKind.Type;
        if (t.TokenFlags.HasFlag(TokenFlags.BinaryOperator) || t.TokenFlags.HasFlag(TokenFlags.UnaryOperator) || t.TokenFlags.HasFlag(TokenFlags.AssignmentOperator))
            return CodeTokenKind.Operator;
        return CodeTokenKind.Text;
    }

    /// <summary>Gleichartige Nachbarn zusammenfassen (weniger Runs im TextBlock).</summary>
    private static IReadOnlyList<CodeSegment> Merge(List<CodeSegment> segments)
    {
        var merged = new List<CodeSegment>();
        foreach (var s in segments)
        {
            if (s.Text.Length == 0) continue;
            if (merged.Count > 0 && merged[^1].Kind == s.Kind) merged[^1] = merged[^1] with { Text = merged[^1].Text + s.Text };
            else merged.Add(s);
        }
        return merged;
    }
}
