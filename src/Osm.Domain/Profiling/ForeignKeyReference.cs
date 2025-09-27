using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Profiling;

public sealed record ForeignKeyReference(
    SchemaName FromSchema,
    TableName FromTable,
    ColumnName FromColumn,
    SchemaName ToSchema,
    TableName ToTable,
    ColumnName ToColumn,
    bool HasDatabaseConstraint)
{
    public static Result<ForeignKeyReference> Create(
        SchemaName fromSchema,
        TableName fromTable,
        ColumnName fromColumn,
        SchemaName toSchema,
        TableName toTable,
        ColumnName toColumn,
        bool hasDatabaseConstraint)
    {
        return Result<ForeignKeyReference>.Success(new ForeignKeyReference(
            fromSchema,
            fromTable,
            fromColumn,
            toSchema,
            toTable,
            toColumn,
            hasDatabaseConstraint));
    }
}
