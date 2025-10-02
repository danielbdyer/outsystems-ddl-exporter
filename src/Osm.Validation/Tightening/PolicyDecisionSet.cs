using System.Collections.Immutable;

namespace Osm.Validation.Tightening;

public sealed record PolicyDecisionSet(
    ImmutableDictionary<ColumnCoordinate, NullabilityDecision> Nullability,
    ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> ForeignKeys,
    ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> UniqueIndexes,
    ImmutableArray<TighteningDiagnostic> Diagnostics)
{
    public static PolicyDecisionSet Create(
        ImmutableDictionary<ColumnCoordinate, NullabilityDecision> nullability,
        ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> foreignKeys,
        ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> uniqueIndexes,
        ImmutableArray<TighteningDiagnostic> diagnostics)
        => new(nullability, foreignKeys, uniqueIndexes, diagnostics);
}
