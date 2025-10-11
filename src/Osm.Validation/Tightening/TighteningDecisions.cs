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
}
