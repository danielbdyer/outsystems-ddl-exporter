using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Profiling;

public sealed record ColumnProfile(
    SchemaName Schema,
    TableName Table,
    ColumnName Column,
    bool IsNullablePhysical,
    bool IsComputed,
    bool IsPrimaryKey,
    bool IsUniqueKey,
    string? DefaultDefinition,
    long RowCount,
    long NullCount,
    ProfilingProbeStatus NullCountStatus)
{
    public static Result<ColumnProfile> Create(
        SchemaName schema,
        TableName table,
        ColumnName column,
        bool isNullablePhysical,
        bool isComputed,
        bool isPrimaryKey,
        bool isUniqueKey,
        string? defaultDefinition,
        long rowCount,
        long nullCount,
        ProfilingProbeStatus nullCountStatus)
    {
        if (nullCountStatus is null)
        {
            throw new ArgumentNullException(nameof(nullCountStatus));
        }

        if (rowCount < 0)
        {
            return Result<ColumnProfile>.Failure(ValidationError.Create("profile.column.rowCount.invalid", "Row count cannot be negative."));
        }

        if (nullCount < 0)
        {
            return Result<ColumnProfile>.Failure(ValidationError.Create("profile.column.nullCount.invalid", "Null count cannot be negative."));
        }

        if (nullCount > rowCount)
        {
            return Result<ColumnProfile>.Failure(ValidationError.Create("profile.column.nullCount.exceeds", "Null count cannot exceed row count."));
        }

        var trimmedDefault = string.IsNullOrWhiteSpace(defaultDefinition) ? null : defaultDefinition;

        return Result<ColumnProfile>.Success(new ColumnProfile(
            schema,
            table,
            column,
            isNullablePhysical,
            isComputed,
            isPrimaryKey,
            isUniqueKey,
            trimmedDefault,
            rowCount,
            nullCount,
            nullCountStatus));
    }
}
