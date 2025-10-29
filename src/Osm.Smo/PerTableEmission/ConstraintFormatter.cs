using System;
using System.Collections.Generic;
using System.Text;

namespace Osm.Smo.PerTableEmission;

internal sealed class ConstraintFormatter
{
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
                AppendFormattedForeignKey(
                    builder,
                    line,
                    trimmed,
                    foreignKeyTrustLookup);
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
                AppendFormattedPrimaryKey(builder, line, trimmed);
                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private void AppendFormattedForeignKey(
        StringBuilder builder,
        string line,
        string trimmed,
        IReadOnlyDictionary<string, bool>? foreignKeyTrustLookup)
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
            return;
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
        builder.Append(' ');
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
            AppendForeignKeyClauses(builder, ownerIndent, trailingComma, onDeleteSegment, onUpdateSegment);
        }

        if (foreignKeyTrustLookup is not null)
        {
            AppendForeignKeyTrustComment(builder, ownerIndent, constraintSegment, foreignKeyTrustLookup);
        }
    }

    private static void AppendForeignKeyClauses(
        StringBuilder builder,
        string ownerIndent,
        string trailingComma,
        string? onDeleteSegment,
        string? onUpdateSegment)
    {
        var clauseIndent = ownerIndent + new string(' ', 4);
        var segments = new List<string>(capacity: 2);
        if (!string.IsNullOrEmpty(onDeleteSegment))
        {
            segments.Add(onDeleteSegment!);
        }

        if (!string.IsNullOrEmpty(onUpdateSegment))
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

    private void AppendForeignKeyTrustComment(
        StringBuilder builder,
        string ownerIndent,
        string constraintSegment,
        IReadOnlyDictionary<string, bool> foreignKeyTrustLookup)
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

    private static void AppendFormattedPrimaryKey(StringBuilder builder, string line, string trimmed)
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
}
