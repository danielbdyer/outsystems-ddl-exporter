using System;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Profiling;

public sealed record ProfilingInsight(
    string Code,
    ProfilingInsightSeverity Severity,
    SchemaName Schema,
    TableName Table,
    ImmutableArray<ColumnName> Columns,
    string Message,
    ImmutableDictionary<string, string?> Metadata)
{
    public static Result<ProfilingInsight> Create(
        string code,
        ProfilingInsightSeverity severity,
        SchemaName schema,
        TableName table,
        ImmutableArray<ColumnName> columns,
        string message,
        ImmutableDictionary<string, string?> metadata)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ValidationError.Create("profiling.insight.code.required", "Insight code must be provided.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return ValidationError.Create("profiling.insight.message.required", "Insight message must be provided.");
        }

        return Result<ProfilingInsight>.Success(new ProfilingInsight(
            code,
            severity,
            schema,
            table,
            columns.IsDefault ? ImmutableArray<ColumnName>.Empty : columns,
            message,
            metadata == default ? ImmutableDictionary<string, string?>.Empty : metadata));
    }
}
