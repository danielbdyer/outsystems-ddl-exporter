using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;

namespace Osm.Domain.Profiling;

public sealed record ProfileSnapshot(
    ImmutableArray<ColumnProfile> Columns,
    ImmutableArray<UniqueCandidateProfile> UniqueCandidates,
    ImmutableArray<CompositeUniqueCandidateProfile> CompositeUniqueCandidates,
    ImmutableArray<ForeignKeyReality> ForeignKeys)
{
    public static Result<ProfileSnapshot> Create(
        IEnumerable<ColumnProfile> columns,
        IEnumerable<UniqueCandidateProfile> uniqueCandidates,
        IEnumerable<CompositeUniqueCandidateProfile> compositeUniqueCandidates,
        IEnumerable<ForeignKeyReality> foreignKeys)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        if (uniqueCandidates is null)
        {
            throw new ArgumentNullException(nameof(uniqueCandidates));
        }

        if (compositeUniqueCandidates is null)
        {
            throw new ArgumentNullException(nameof(compositeUniqueCandidates));
        }

        if (foreignKeys is null)
        {
            throw new ArgumentNullException(nameof(foreignKeys));
        }

        var columnArray = columns.ToImmutableArray();
        var uniqueArray = uniqueCandidates.ToImmutableArray();
        var compositeArray = compositeUniqueCandidates.ToImmutableArray();
        var fkArray = foreignKeys.ToImmutableArray();

        return Result<ProfileSnapshot>.Success(new ProfileSnapshot(columnArray, uniqueArray, compositeArray, fkArray));
    }
}
