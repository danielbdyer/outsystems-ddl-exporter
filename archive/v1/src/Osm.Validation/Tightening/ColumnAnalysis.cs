using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Validation.Tightening;

public sealed record ColumnAnalysis(
    ColumnIdentity Identity,
    NullabilityDecision Nullability,
    ForeignKeyDecision? ForeignKey,
    ImmutableArray<UniqueIndexDecision> UniqueIndexes,
    ImmutableArray<Opportunity> Opportunities)
{
    public ColumnCoordinate Column => Identity.Coordinate;

    public static ColumnAnalysis Create(
        ColumnIdentity identity,
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

        return new ColumnAnalysis(identity, nullability, foreignKey, uniqueArray, opportunityArray);
    }
}
