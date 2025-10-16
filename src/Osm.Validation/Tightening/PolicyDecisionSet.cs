using System;
using System.Collections.Immutable;
using Osm.Domain.Configuration;

namespace Osm.Validation.Tightening;

public sealed record PolicyDecisionSet(
    ImmutableDictionary<ColumnCoordinate, NullabilityDecision> Nullability,
    ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> ForeignKeys,
    ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> UniqueIndexes,
    ImmutableArray<TighteningDiagnostic> Diagnostics,
    ImmutableDictionary<ColumnCoordinate, string> ColumnModules,
    ImmutableDictionary<IndexCoordinate, string> IndexModules,
    TighteningToggleSnapshot Toggles)
{
    public static PolicyDecisionSet Create(
        ImmutableDictionary<ColumnCoordinate, NullabilityDecision> nullability,
        ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> foreignKeys,
        ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> uniqueIndexes,
        ImmutableArray<TighteningDiagnostic> diagnostics,
        ImmutableDictionary<ColumnCoordinate, string> columnModules,
        ImmutableDictionary<IndexCoordinate, string> indexModules,
        TighteningOptions options,
        Func<string, ToggleSource?>? sourceResolver = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        columnModules ??= ImmutableDictionary<ColumnCoordinate, string>.Empty;
        indexModules ??= ImmutableDictionary<IndexCoordinate, string>.Empty;

        var toggles = TighteningToggleSnapshot.Create(options, sourceResolver);
        return new PolicyDecisionSet(
            nullability,
            foreignKeys,
            uniqueIndexes,
            diagnostics,
            columnModules,
            indexModules,
            toggles);
    }
}
