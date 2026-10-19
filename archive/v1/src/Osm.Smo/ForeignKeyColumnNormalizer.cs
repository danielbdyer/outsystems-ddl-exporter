using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Model;

namespace Osm.Smo;

internal static class ForeignKeyColumnNormalizer
{
    public static ImmutableArray<string> Normalize(
        ImmutableArray<string> columns,
        IReadOnlyDictionary<string, AttributeModel> attributeLookup)
    {
        if (columns.IsDefaultOrEmpty)
        {
            return columns;
        }

        if (attributeLookup is null)
        {
            throw new ArgumentNullException(nameof(attributeLookup));
        }

        var builder = ImmutableArray.CreateBuilder<string>(columns.Length);
        foreach (var column in columns)
        {
            if (!string.IsNullOrWhiteSpace(column) &&
                attributeLookup.TryGetValue(column, out var attribute) &&
                !string.IsNullOrWhiteSpace(attribute.LogicalName.Value))
            {
                builder.Add(attribute.LogicalName.Value);
            }
            else
            {
                builder.Add(column);
            }
        }

        return builder.ToImmutable();
    }
}
