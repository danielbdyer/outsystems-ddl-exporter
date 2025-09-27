using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public sealed record IndexModel(
    IndexName Name,
    bool IsUnique,
    bool IsPrimary,
    bool IsPlatformAuto,
    ImmutableArray<IndexColumnModel> Columns)
{
    public static Result<IndexModel> Create(
        IndexName name,
        bool isUnique,
        bool isPrimary,
        bool isPlatformAuto,
        IEnumerable<IndexColumnModel> columns)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        var materialized = columns.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            return Result<IndexModel>.Failure(ValidationError.Create("index.columns.empty", "Index must include at least one column."));
        }

        if (HasDuplicateOrdinals(materialized))
        {
            return Result<IndexModel>.Failure(ValidationError.Create("index.columns.duplicateOrdinal", "Index columns must have unique ordinals."));
        }

        return Result<IndexModel>.Success(new IndexModel(name, isUnique, isPrimary, isPlatformAuto, materialized));
    }

    private static bool HasDuplicateOrdinals(ImmutableArray<IndexColumnModel> columns)
    {
        var set = new HashSet<int>();
        foreach (var column in columns)
        {
            if (!set.Add(column.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
