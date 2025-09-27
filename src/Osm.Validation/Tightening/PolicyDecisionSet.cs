using System.Collections.Immutable;

namespace Osm.Validation.Tightening;

public sealed record PolicyDecisionSet(
    ImmutableDictionary<ColumnCoordinate, NullabilityDecision> Nullability,
    ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> ForeignKeys)
{
    public static PolicyDecisionSet Create(
        ImmutableDictionary<ColumnCoordinate, NullabilityDecision> nullability,
        ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> foreignKeys)
        => new(nullability, foreignKeys);
}
