using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Profiling;

public sealed record UniqueCandidateProfile(
    SchemaName Schema,
    TableName Table,
    ColumnName Column,
    bool HasDuplicate)
{
    public static Result<UniqueCandidateProfile> Create(
        SchemaName schema,
        TableName table,
        ColumnName column,
        bool hasDuplicate)
    {
        return Result<UniqueCandidateProfile>.Success(new UniqueCandidateProfile(schema, table, column, hasDuplicate));
    }
}
