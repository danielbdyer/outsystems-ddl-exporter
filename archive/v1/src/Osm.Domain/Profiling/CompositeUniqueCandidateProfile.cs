using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Profiling;

public sealed record CompositeUniqueCandidateProfile(
    SchemaName Schema,
    TableName Table,
    ImmutableArray<ColumnName> Columns,
    bool HasDuplicate)
{
    public static Result<CompositeUniqueCandidateProfile> Create(
        SchemaName schema,
        TableName table,
        IEnumerable<ColumnName> columns,
        bool hasDuplicate)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        var columnArray = columns.ToImmutableArray();
        if (columnArray.Length == 0)
        {
            return Result<CompositeUniqueCandidateProfile>.Failure(
                ValidationError.Create("profile.compositeUnique.columns.missing", "Composite unique candidates must define at least one column."));
        }

        return Result<CompositeUniqueCandidateProfile>.Success(new CompositeUniqueCandidateProfile(schema, table, columnArray, hasDuplicate));
    }
}
