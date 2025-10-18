using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Validation.Tightening;

public sealed record ColumnAnalysis(
    ColumnCoordinate Column,
    NullabilityDecision Nullability,
    ForeignKeyDecision? ForeignKey,
    ImmutableArray<UniqueIndexDecision> UniqueIndexes,
    ImmutableArray<Opportunity> Opportunities)
{
    public static ColumnAnalysis Create(
        ColumnCoordinate column,
        NullabilityDecision nullability,
        ForeignKeyDecision? foreignKey,
        IEnumerable<UniqueIndexDecision> uniqueIndexes,
        IEnumerable<Opportunity> opportunities)
    {
        if (nullability is null)
        {
            throw new ArgumentNullException(nameof(nullability));
        }

        var uniqueArray = (uniqueIndexes ?? Array.Empty<UniqueIndexDecision>())
            .Distinct()
            .OrderBy(u => u.Index.Schema.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(u => u.Index.Table.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(u => u.Index.Index.Value, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        var opportunityArray = (opportunities ?? Array.Empty<Opportunity>())
            .ToImmutableArray();

        return new ColumnAnalysis(column, nullability, foreignKey, uniqueArray, opportunityArray);
    }
}
