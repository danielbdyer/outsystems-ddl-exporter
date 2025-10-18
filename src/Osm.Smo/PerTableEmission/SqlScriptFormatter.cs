using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Osm.Smo;

namespace Osm.Smo.PerTableEmission;

internal sealed class SqlScriptFormatter
{
    public Identifier CreateIdentifier(string value, SmoFormatOptions format)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        return new Identifier
        {
            Value = value,
            QuoteType = MapQuoteType(format.IdentifierQuoteStrategy),
        };
    }

    public SchemaObjectName BuildSchemaObjectName(string schema, string name, SmoFormatOptions format)
    {
        if (schema is null)
        {
            throw new ArgumentNullException(nameof(schema));
        }

        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        return new SchemaObjectName
        {
            Identifiers =
            {
                CreateIdentifier(schema, format),
                CreateIdentifier(name, format),
            }
        };
    }

    public ColumnReferenceExpression BuildColumnReference(string columnName, SmoFormatOptions format)
    {
        if (columnName is null)
        {
            throw new ArgumentNullException(nameof(columnName));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        return new ColumnReferenceExpression
        {
            MultiPartIdentifier = new MultiPartIdentifier
            {
                Identifiers = { CreateIdentifier(columnName, format) }
            }
        };
    }

    public string ResolveConstraintName(
        string originalName,
        string originalTableName,
        string logicalTableName,
        string effectiveTableName)
    {
        if (string.IsNullOrWhiteSpace(originalName) ||
            string.Equals(originalTableName, effectiveTableName, StringComparison.OrdinalIgnoreCase))
        {
            return originalName;
        }

        var renamed = ReplaceIgnoreCase(originalName, originalTableName, effectiveTableName);
        renamed = ReplaceIgnoreCase(renamed, logicalTableName, effectiveTableName);
        return renamed;
    }

    public string QuoteIdentifier(string identifier, SmoFormatOptions format)
    {
        if (identifier is null)
        {
            throw new ArgumentNullException(nameof(identifier));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        return format.IdentifierQuoteStrategy switch
        {
            IdentifierQuoteStrategy.DoubleQuote => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"",
            IdentifierQuoteStrategy.None => identifier,
            _ => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]",
        };
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
        var withForeignKeys = FormatForeignKeyConstraints(withDefaults, foreignKeyTrustLookup);
        return FormatPrimaryKeyConstraints(withForeignKeys);
    }

    public string FormatForeignKeyConstraints(
        string script,
        IReadOnlyDictionary<string, bool>? foreignKeyTrustLookup)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        var lines = script.Split(Environment.NewLine);
        var builder = new StringBuilder(script.Length + 64);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("REFERENCES", StringComparison.OrdinalIgnoreCase))
            {
                var indentLength = line.Length - trimmed.Length;
                var indent = line[..indentLength];
                var working = trimmed;
                var trailingComma = string.Empty;

                if (working.EndsWith(",", StringComparison.Ordinal))
                {
                    trailingComma = ",";
                    working = working[..^1].TrimEnd();
                }

                var foreignKeyIndex = working.IndexOf("FOREIGN KEY", StringComparison.OrdinalIgnoreCase);
                var referencesIndex = working.IndexOf("REFERENCES", StringComparison.OrdinalIgnoreCase);

                if (foreignKeyIndex <= 0 || referencesIndex <= foreignKeyIndex)
                {
                    builder.AppendLine(line);
                    continue;
                }

                var constraintSegment = working[..foreignKeyIndex].TrimEnd();
                var ownerSegment = working[(foreignKeyIndex + "FOREIGN KEY".Length)..referencesIndex].Trim();
                var referencesSegment = working[referencesIndex..].Trim();

                var onDeleteIndex = referencesSegment.IndexOf("ON DELETE", StringComparison.OrdinalIgnoreCase);
                string? onDeleteSegment = null;
                if (onDeleteIndex >= 0)
                {
                    onDeleteSegment = referencesSegment[onDeleteIndex..].TrimEnd();
                    referencesSegment = referencesSegment[..onDeleteIndex].TrimEnd();
                }

                var onUpdateIndex = referencesSegment.IndexOf("ON UPDATE", StringComparison.OrdinalIgnoreCase);
                string? onUpdateSegment = null;
                if (onUpdateIndex >= 0)
                {
                    onUpdateSegment = referencesSegment[onUpdateIndex..].TrimEnd();
                    referencesSegment = referencesSegment[..onUpdateIndex].TrimEnd();
                }

                var hasOnDelete = !string.IsNullOrEmpty(onDeleteSegment);
                var hasOnUpdate = !string.IsNullOrEmpty(onUpdateSegment);
                var hasOnClauses = hasOnDelete || hasOnUpdate;

                var ownerIndent = indent + new string(' ', 4);
                builder.Append(indent);
                builder.Append(constraintSegment);
                builder.AppendLine();
                builder.Append(ownerIndent);
                builder.Append("FOREIGN KEY ");
                builder.Append(ownerSegment);
                builder.Append(" ");
                builder.Append(referencesSegment);
                if (hasOnClauses)
                {
                    builder.AppendLine();
                }
                else
                {
                    builder.AppendLine(trailingComma);
                }

                if (hasOnClauses)
                {
                    var clauseIndent = ownerIndent + new string(' ', 4);
                    var segments = new List<string>(capacity: 2);
                    if (hasOnDelete)
                    {
                        segments.Add(onDeleteSegment!);
                    }

                    if (hasOnUpdate)
                    {
                        segments.Add(onUpdateSegment!);
                    }

                    for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                    {
                        var segment = segments[segmentIndex];
                        var isLastSegment = segmentIndex == segments.Count - 1;

                        builder.Append(clauseIndent);
                        if (isLastSegment && !string.IsNullOrEmpty(trailingComma))
                        {
                            builder.Append(segment);
                            builder.AppendLine(trailingComma);
                        }
                        else
                        {
                            builder.AppendLine(segment);
                        }
                    }
                }

                if (foreignKeyTrustLookup is not null)
                {
                    var constraintName = ExtractConstraintName(constraintSegment);
                    if (!string.IsNullOrEmpty(constraintName) &&
                        foreignKeyTrustLookup.TryGetValue(constraintName, out var isNoCheck) &&
                        isNoCheck)
                    {
                        builder.Append(ownerIndent);
                        builder.AppendLine("-- Source constraint was not trusted (WITH NOCHECK)");
                    }
                }

                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    public string FormatPrimaryKeyConstraints(string script)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        var lines = script.Split(Environment.NewLine);
        var builder = new StringBuilder(script.Length + 32);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
            {
                var indentLength = line.Length - trimmed.Length;
                var indent = line[..indentLength];
                var working = trimmed;
                var trailingComma = string.Empty;

                if (working.EndsWith(",", StringComparison.Ordinal))
                {
                    trailingComma = ",";
                    working = working[..^1].TrimEnd();
                }

                var primaryIndex = working.IndexOf("PRIMARY KEY", StringComparison.OrdinalIgnoreCase);
                var constraintSegment = working[..primaryIndex].TrimEnd();
                var primarySegment = working[primaryIndex..].Trim();

                builder.Append(indent);
                builder.Append(constraintSegment);
                builder.AppendLine();
                builder.Append(indent);
                builder.Append(new string(' ', 4));
                builder.Append(primarySegment);
                builder.AppendLine(trailingComma);
                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private static QuoteType MapQuoteType(IdentifierQuoteStrategy strategy) => strategy switch
    {
        IdentifierQuoteStrategy.DoubleQuote => QuoteType.DoubleQuote,
        IdentifierQuoteStrategy.None => QuoteType.NotQuoted,
        _ => QuoteType.SquareBracket,
    };

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
        var defaultSegment = working[defaultIndex..].TrimStart();

        string? constraintSegment = null;
        var constraintIndex = beforeDefault.IndexOf(" CONSTRAINT ", StringComparison.OrdinalIgnoreCase);
        if (constraintIndex >= 0)
        {
            constraintSegment = beforeDefault[constraintIndex..].TrimStart();
            beforeDefault = beforeDefault[..constraintIndex].TrimEnd();
        }

        var builder = new StringBuilder(line.Length + 16);
        builder.Append(indent);
        builder.Append(beforeDefault);
        builder.Append(Environment.NewLine);
        builder.Append(extraIndent);
        if (!string.IsNullOrEmpty(constraintSegment))
        {
            builder.Append(constraintSegment);
            builder.Append(" ");
        }

        builder.Append(defaultSegment);
        builder.Append(trailingComma);

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

    private static string ExtractConstraintName(string constraintSegment)
    {
        if (string.IsNullOrWhiteSpace(constraintSegment))
        {
            return string.Empty;
        }

        var working = constraintSegment.Trim();
        if (working.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
        {
            working = working["CONSTRAINT".Length..].Trim();
        }

        if (working.Length == 0)
        {
            return string.Empty;
        }

        if (working.StartsWith("[", StringComparison.Ordinal) && working.EndsWith("]", StringComparison.Ordinal) && working.Length > 2)
        {
            working = working[1..^1];
        }
        else if (working.StartsWith("\"", StringComparison.Ordinal) && working.EndsWith("\"", StringComparison.Ordinal) && working.Length > 2)
        {
            working = working[1..^1];
        }

        return working;
    }

    private static string ReplaceIgnoreCase(string source, string search, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search))
        {
            return source;
        }

        var result = new StringBuilder();
        var currentIndex = 0;
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;

        while (true)
        {
            var matchIndex = source.IndexOf(search, currentIndex, comparison);
            if (matchIndex < 0)
            {
                result.Append(source, currentIndex, source.Length - currentIndex);
                break;
            }

            result.Append(source, currentIndex, matchIndex - currentIndex);
            result.Append(replacement);
            currentIndex = matchIndex + search.Length;
        }

        return result.ToString();
    }
}
