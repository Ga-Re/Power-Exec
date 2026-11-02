using System.Management.Automation.Language;
using PowerExec.Core.Models;

namespace PowerExec.Core.Metadata;

/// <summary>Liest Comment-Based Help, param()-Block und #Requires aus einem PowerShell-Script.</summary>
public static class ScriptInfoReader
{
    public const string CopyFolderMarker = "PowerExec: CopyFolder";

    public static void Fill(LaunchItem item)
    {
        ScriptBlockAst ast;
        Token[] tokens;
        ParseError[] errors;
        try
        {
            ast = Parser.ParseFile(item.FullPath, out tokens, out errors);
        }
        catch (Exception ex)
        {
            item.ParseError = ex.Message;
            return;
        }
        Fill(item, ast, tokens, errors);
    }

    public static void FillFromText(LaunchItem item, string scriptText)
    {
        var ast = Parser.ParseInput(scriptText, out var tokens, out var errors);
        Fill(item, ast, tokens, errors);
    }

    private static void Fill(LaunchItem item, ScriptBlockAst ast, Token[] tokens, ParseError[] errors)
    {
        if (errors.Length > 0)
            item.ParseError = $"Zeile {errors[0].Extent.StartLineNumber}: {errors[0].Message}";

        CommentHelpInfo? help = null;
        try { help = ast.GetHelpContent() ?? FindHelpInComments(tokens); } catch { /* Hilfe ist optional */ }

        if (help != null)
        {
            item.Synopsis = Clean(help.Synopsis);
            item.Description = Clean(help.Description);
            if (help.Notes?.Contains(CopyFolderMarker, StringComparison.OrdinalIgnoreCase) == true)
                item.CopyFolderByDefault = true;
        }

        item.Parameters = ReadParameters(ast.ParamBlock, help);

        var modules = ast.ScriptRequirements?.RequiredModules;
        if (modules != null)
            item.RequiredModules = modules.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        item.UsesReadHost = ast.Find(a => a is CommandAst c &&
            string.Equals(c.GetCommandName(), "Read-Host", StringComparison.OrdinalIgnoreCase), true) != null;
    }

    private static IReadOnlyList<ScriptParameter> ReadParameters(ParamBlockAst? block, CommentHelpInfo? help)
    {
        if (block == null) return Array.Empty<ScriptParameter>();
        var list = new List<ScriptParameter>();
        foreach (var p in block.Parameters)
        {
            var name = p.Name.VariablePath.UserPath;
            string typeName = string.Empty;
            bool mandatory = false;
            string helpMessage = string.Empty;
            var validValues = new List<string>();

            foreach (var attr in p.Attributes)
            {
                switch (attr)
                {
                    case TypeConstraintAst tc:
                        typeName = tc.TypeName.FullName;
                        break;
                    case AttributeAst a when IsAttr(a, "Parameter"):
                        foreach (var na in a.NamedArguments)
                        {
                            if (na.ArgumentName.Equals("Mandatory", StringComparison.OrdinalIgnoreCase))
                                mandatory = na.ExpressionOmitted || IsTrue(na.Argument);
                            else if (na.ArgumentName.Equals("HelpMessage", StringComparison.OrdinalIgnoreCase) &&
                                     na.Argument is StringConstantExpressionAst s)
                                helpMessage = s.Value;
                        }
                        break;
                    case AttributeAst a when IsAttr(a, "ValidateSet"):
                        foreach (var pa in a.PositionalArguments)
                        {
                            if (pa is StringConstantExpressionAst s) validValues.Add(s.Value);
                            else if (pa is ConstantExpressionAst c && c.Value != null) validValues.Add(c.Value.ToString()!);
                        }
                        break;
                }
            }

            string paramHelp = helpMessage;
            if (help?.Parameters != null)
            {
                foreach (var kv in help.Parameters)
                {
                    if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        paramHelp = Clean(kv.Value);
                        break;
                    }
                }
            }

            list.Add(new ScriptParameter
            {
                Name = name,
                TypeName = typeName,
                IsMandatory = mandatory,
                ValidValues = validValues,
                DefaultValueText = p.DefaultValue?.Extent.Text,
                HelpText = paramHelp
            });
        }
        return list;
    }

    /// <summary>
    /// PowerShell erkennt die Hilfe nicht, wenn z. B. #Requires davor steht.
    /// Dann wird der erste Kommentarblock mit .SYNOPSIS/.DESCRIPTION separat ausgewertet.
    /// </summary>
    private static CommentHelpInfo? FindHelpInComments(Token[] tokens)
    {
        foreach (var t in tokens)
        {
            if (t.Kind != TokenKind.Comment) continue;
            var text = t.Text;
            if (text.IndexOf(".SYNOPSIS", StringComparison.OrdinalIgnoreCase) < 0 &&
                text.IndexOf(".DESCRIPTION", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var probe = Parser.ParseInput(text + "\nparam()\n", out _, out _);
            return probe.GetHelpContent();
        }
        return null;
    }

    private static bool IsAttr(AttributeAst a, string name)
    {
        var n = a.TypeName.Name;
        return n.Equals(name, StringComparison.OrdinalIgnoreCase) ||
               n.Equals(name + "Attribute", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrue(ExpressionAst expr) => expr switch
    {
        VariableExpressionAst v => v.VariablePath.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase),
        ConstantExpressionAst c => c.Value is true || (c.Value is int i && i != 0),
        _ => false
    };

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lines = text.Replace("\r", string.Empty).Split('\n').Select(l => l.Trim());
        return string.Join(" ", lines.Where(l => l.Length > 0)).Trim();
    }
}
