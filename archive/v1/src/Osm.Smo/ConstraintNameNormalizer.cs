using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Osm.Domain.Model;

namespace Osm.Smo;

public static class ConstraintNameNormalizer
{
    public static string Normalize(
        string originalName,
        EntityModel entity,
        IReadOnlyCollection<AttributeModel> referencedAttributes,
        ConstraintNameKind kind,
        SmoFormatOptions format,
        EntityModel? referencedEntity = null)
    {
        if (string.IsNullOrWhiteSpace(originalName))
        {
            return originalName;
        }

        var normalized = ReplaceIgnoreCase(originalName, entity.PhysicalName.Value, entity.LogicalName.Value);
        normalized = ReplaceIgnoreCase(normalized, entity.LogicalName.Value, entity.LogicalName.Value);

        if (kind == ConstraintNameKind.ForeignKey && referencedEntity is not null)
        {
            normalized = ReplaceIgnoreCase(normalized, referencedEntity.PhysicalName.Value, referencedEntity.LogicalName.Value);

            // Also try replacing just the suffix of the physical table name (without the prefix)
            // This handles cases where the FK name contains only the meaningful suffix rather than the full prefixed name
            // Example: FK name contains "ProjectStatus" but physical name is "OSUSR_wzu_ProjectStatus"
            var referencedSuffix = ExtractTableSuffix(referencedEntity.PhysicalName.Value);
            if (!string.IsNullOrEmpty(referencedSuffix) && !string.Equals(referencedSuffix, referencedEntity.PhysicalName.Value, StringComparison.OrdinalIgnoreCase))
            {
                normalized = ReplaceIgnoreCase(normalized, referencedSuffix, referencedEntity.LogicalName.Value);
            }

            normalized = ReplaceIgnoreCase(normalized, referencedEntity.LogicalName.Value, referencedEntity.LogicalName.Value);
        }

        if (referencedAttributes is not null)
        {
            foreach (var attribute in referencedAttributes)
            {
                normalized = ReplaceIgnoreCase(normalized, attribute.ColumnName.Value, attribute.LogicalName.Value);
                normalized = ReplaceIgnoreCase(normalized, attribute.LogicalName.Value, attribute.LogicalName.Value);
            }
        }

        var prefixSeparator = normalized.IndexOf('_');
        var prefix = prefixSeparator > 0 ? normalized[..prefixSeparator] : null;
        var suffix = prefixSeparator > 0 ? normalized[(prefixSeparator + 1)..] : normalized;

        var parts = suffix
            .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeToken)
            .ToArray();

        var rebuiltSuffix = string.Join('_', parts);
        var baseName = string.IsNullOrEmpty(prefix) ? rebuiltSuffix : $"{prefix}_{rebuiltSuffix}";
        return format.IndexNaming.Apply(baseName, kind);
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var isAllUpper = value.All(char.IsUpper);
        var isAllLower = value.All(char.IsLower);

        if (!isAllUpper && !isAllLower)
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }

    private static string ExtractTableSuffix(string physicalTableName)
    {
        if (string.IsNullOrEmpty(physicalTableName))
        {
            return physicalTableName;
        }

        // OutSystems physical table names typically follow the pattern: PREFIX_MODULE_TableName
        // For example: OSUSR_wzu_ProjectStatus where the suffix is "ProjectStatus"
        // We need to extract the meaningful suffix by removing the common prefix pattern

        // Find all underscore positions
        var underscoreCount = 0;
        var lastUnderscoreIndex = -1;

        for (var i = 0; i < physicalTableName.Length; i++)
        {
            if (physicalTableName[i] == '_')
            {
                underscoreCount++;
                lastUnderscoreIndex = i;

                // If we've found at least 2 underscores, the suffix comes after the second one
                // This handles the pattern: OSUSR_MODULE_Suffix or similar PREFIX_MODULE_Suffix
                if (underscoreCount == 2)
                {
                    return physicalTableName[(i + 1)..];
                }
            }
        }

        // If we didn't find 2 underscores but found at least 1, return everything after the last underscore
        if (lastUnderscoreIndex >= 0)
        {
            return physicalTableName[(lastUnderscoreIndex + 1)..];
        }

        // No underscores found, return the original name
        return physicalTableName;
    }

    private static string ReplaceIgnoreCase(string source, string search, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search))
        {
            return source;
        }

        var currentIndex = 0;
        var comparison = StringComparison.OrdinalIgnoreCase;
        var builder = new StringBuilder();

        while (currentIndex < source.Length)
        {
            var matchIndex = source.IndexOf(search, currentIndex, comparison);
            if (matchIndex < 0)
            {
                builder.Append(source, currentIndex, source.Length - currentIndex);
                break;
            }

            builder.Append(source, currentIndex, matchIndex - currentIndex);
            builder.Append(replacement);
            currentIndex = matchIndex + search.Length;
        }

        return builder.ToString();
    }
}
