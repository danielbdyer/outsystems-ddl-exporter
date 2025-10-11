using System;
using System.Collections.Immutable;

namespace Osm.Validation.Tightening;

public sealed record TighteningDecisions(
    ImmutableDictionary<ColumnCoordinate, NullabilityDecision> Nullability,
    ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> ForeignKeys,
    ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> UniqueIndexes)
{
    public static TighteningDecisions Create(
        ImmutableDictionary<ColumnCoordinate, NullabilityDecision> nullability,
        ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> foreignKeys,
        ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> uniqueIndexes)
        => new(nullability, foreignKeys, uniqueIndexes);

    public static TighteningDecisions FromPolicyDecisions(PolicyDecisionSet decisions)
    {
        if (decisions is null)
        {
            throw new ArgumentNullException(nameof(decisions));
        }

        return new TighteningDecisions(
            decisions.NullabilityOutcomes,
            decisions.ForeignKeyOutcomes,
            decisions.UniqueIndexOutcomes);
    }
}
