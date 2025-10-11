using System.Collections.Immutable;
using System.Linq;

namespace Osm.Validation.Tightening;

public sealed record PolicyDecisionSet(
    ImmutableDictionary<ColumnCoordinate, PolicyDecision<NullabilityDecision>> Nullability,
    ImmutableDictionary<ColumnCoordinate, PolicyDecision<ForeignKeyDecision>> ForeignKeys,
    ImmutableDictionary<IndexCoordinate, PolicyDecision<UniqueIndexDecision>> UniqueIndexes,
    ImmutableArray<TighteningDiagnostic> Diagnostics)
{
    public static PolicyDecisionSet Create(
        ImmutableDictionary<ColumnCoordinate, PolicyDecision<NullabilityDecision>> nullability,
        ImmutableDictionary<ColumnCoordinate, PolicyDecision<ForeignKeyDecision>> foreignKeys,
        ImmutableDictionary<IndexCoordinate, PolicyDecision<UniqueIndexDecision>> uniqueIndexes,
        ImmutableArray<TighteningDiagnostic> diagnostics)
        => new(nullability, foreignKeys, uniqueIndexes, diagnostics);

    public ImmutableDictionary<ColumnCoordinate, NullabilityDecision> NullabilityOutcomes
        => Nullability.ToImmutableDictionary(static pair => pair.Key, static pair => pair.Value.Outcome);

    public ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> ForeignKeyOutcomes
        => ForeignKeys.ToImmutableDictionary(static pair => pair.Key, static pair => pair.Value.Outcome);

    public ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> UniqueIndexOutcomes
        => UniqueIndexes.ToImmutableDictionary(static pair => pair.Key, static pair => pair.Value.Outcome);
}
