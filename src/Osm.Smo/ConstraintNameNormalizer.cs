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
        SmoFormatOptions format)
    {
        if (string.IsNullOrWhiteSpace(originalName))
        {
            return originalName;
        }

        var normalized = ReplaceIgnoreCase(originalName, entity.PhysicalName.Value, entity.LogicalName.Value);
        normalized = ReplaceIgnoreCase(normalized, entity.LogicalName.Value, entity.LogicalName.Value);

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
