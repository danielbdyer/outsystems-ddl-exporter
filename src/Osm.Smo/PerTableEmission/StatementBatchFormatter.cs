using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Osm.Smo.PerTableEmission;

internal sealed class StatementBatchFormatter
{
    private readonly ConstraintFormatter _constraintFormatter;

    public StatementBatchFormatter(ConstraintFormatter constraintFormatter)
    {
        _constraintFormatter = constraintFormatter ?? throw new ArgumentNullException(nameof(constraintFormatter));
    }

    public string NormalizeWhitespace(string script)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        var lines = script.Split(Environment.NewLine);
        var builder = new StringBuilder(script.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(lines[i].TrimEnd());
        }

        return builder.ToString();
    }

    public string JoinStatements(IReadOnlyList<string> statements, SmoFormatOptions format)
    {
        if (statements is null)
        {
            throw new ArgumentNullException(nameof(statements));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        var builder = new StringBuilder();
        for (var i = 0; i < statements.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
                builder.AppendLine("GO");
                builder.AppendLine();
            }

            builder.AppendLine(statements[i]);
        }

        var script = builder.ToString().TrimEnd();
        return format.NormalizeWhitespace ? NormalizeWhitespace(script) : script;
    }

    public string FormatCreateTableScript(
        string script,
        CreateTableStatement statement,
        IReadOnlyDictionary<string, bool>? foreignKeyTrustLookup,
        SmoFormatOptions format)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        if (!format.NormalizeWhitespace)
        {
            return script;
        }

        if (statement?.Definition?.ColumnDefinitions is null ||
            statement.Definition.ColumnDefinitions.Count == 0)
        {
            return script;
        }

        var lines = script.Split(Environment.NewLine);
        var builder = new StringBuilder(script.Length + 32);
        var insideColumnBlock = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            var line = lines[i];
            var trimmedLine = line.TrimStart();

            if (!insideColumnBlock)
            {
                builder.Append(line);
                if (trimmedLine.EndsWith("(", StringComparison.Ordinal))
                {
                    insideColumnBlock = true;
                }

                continue;
            }

            if (trimmedLine.StartsWith(")", StringComparison.Ordinal))
            {
                insideColumnBlock = false;
                builder.Append(line);
                continue;
            }

            if (trimmedLine.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase) &&
                !trimmedLine.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(FormatInlineDefault(line));
                continue;
            }

            if (trimmedLine.Contains("CONSTRAINT", StringComparison.OrdinalIgnoreCase) &&
                !trimmedLine.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(FormatInlineConstraint(line));
                continue;
            }

            if (!trimmedLine.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(FormatTrailingComma(line));
                continue;
            }
        }

        var withDefaults = builder.ToString();
        var withForeignKeys = _constraintFormatter.FormatForeignKeyConstraints(withDefaults, foreignKeyTrustLookup);
        return _constraintFormatter.FormatPrimaryKeyConstraints(withForeignKeys);
    }

    private static string FormatTrailingComma(string line)
    {
        var trimmed = line.TrimEnd();
        if (!trimmed.EndsWith(",", StringComparison.Ordinal))
        {
            return line;
        }

        var indentLength = line.Length - line.TrimStart().Length;
        var indent = line[..indentLength];
        var content = trimmed[..^1].TrimEnd();
        var withoutIndent = content.TrimStart();

        return indent + withoutIndent + ',';
    }

    private static string FormatInlineDefault(string line)
    {
        var trimmedLine = line.TrimStart();
        var indentLength = line.Length - trimmedLine.Length;
        var indent = line[..indentLength];
        var extraIndent = indent + new string(' ', 4);

        var working = trimmedLine;
        var trailingComma = string.Empty;

        var trimmedWorking = working.TrimEnd();
        if (trimmedWorking.EndsWith(",", StringComparison.Ordinal))
        {
            trailingComma = ",";
            trimmedWorking = trimmedWorking[..^1];
        }

        working = trimmedWorking;
        var defaultIndex = working.IndexOf(" DEFAULT", StringComparison.OrdinalIgnoreCase);
        if (defaultIndex < 0)
        {
            return line;
        }

        var beforeDefault = working[..defaultIndex].TrimEnd();
        var afterDefault = working[defaultIndex..].TrimStart();

        string? leadingConstraint = null;
        var constraintIndex = beforeDefault.IndexOf(" CONSTRAINT ", StringComparison.OrdinalIgnoreCase);
        if (constraintIndex >= 0)
        {
            leadingConstraint = beforeDefault[constraintIndex..].Trim();
            beforeDefault = beforeDefault[..constraintIndex].TrimEnd();
        }

        var builder = new StringBuilder(line.Length + 32);
        builder.Append(indent);
        builder.Append(beforeDefault);
        builder.Append(Environment.NewLine);
        builder.Append(extraIndent);

        if (!string.IsNullOrEmpty(leadingConstraint))
        {
            builder.Append(leadingConstraint);
            builder.Append(' ');
        }

        var formattedDefault = InsertConstraintLineBreaks(afterDefault.TrimEnd(), Environment.NewLine + extraIndent + "CONSTRAINT ");
        builder.Append(formattedDefault);
        builder.Append(trailingComma);

        return builder.ToString();
    }

    private static string InsertConstraintLineBreaks(string segment, string replacement)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return segment;
        }

        const string token = " CONSTRAINT ";
        var builder = new StringBuilder(segment.Length + 16);
        var index = 0;

        while (true)
        {
            var match = segment.IndexOf(token, index, StringComparison.OrdinalIgnoreCase);
            if (match < 0)
            {
                builder.Append(segment, index, segment.Length - index);
                break;
            }

            builder.Append(segment, index, match - index);
            builder.Append(replacement);
            index = match + token.Length;
        }

        return builder.ToString();
    }

    private static string FormatInlineConstraint(string line)
    {
        var trimmedLine = line.TrimStart();
        var indentLength = line.Length - trimmedLine.Length;
        var indent = line[..indentLength];

        var working = trimmedLine.TrimEnd();
        var trailingComma = string.Empty;

        if (working.EndsWith(",", StringComparison.Ordinal))
        {
            trailingComma = ",";
            working = working[..^1];
        }

        var constraintIndex = working.IndexOf(" CONSTRAINT ", StringComparison.OrdinalIgnoreCase);
        if (constraintIndex < 0)
        {
            return line;
        }

        var columnSegment = working[..constraintIndex].TrimEnd();
        var constraintSegment = working[constraintIndex..].Trim();

        var builder = new StringBuilder(line.Length + 16);
        builder.Append(indent);
        builder.Append(columnSegment);
        builder.AppendLine();
        builder.Append(indent);
        builder.Append(new string(' ', 4));
        builder.Append(constraintSegment);
        builder.Append(trailingComma);

        return builder.ToString();
    }
}
