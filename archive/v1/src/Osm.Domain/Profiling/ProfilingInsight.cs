using System;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Profiling;

public enum ProfilingInsightSeverity
{
    Info,
    Recommendation,
    Warning,
    Error
}

public enum ProfilingInsightCategory
{
    Nullability,
    Uniqueness,
    ForeignKey,
    ComputedColumn,
    Evidence
}

public sealed record ProfilingInsightCoordinate(
    SchemaName Schema,
    TableName Table,
    ColumnName? Column,
    SchemaName? RelatedSchema,
    TableName? RelatedTable,
    ColumnName? RelatedColumn)
{
    public static Result<ProfilingInsightCoordinate> Create(
        SchemaName schema,
        TableName table,
        ColumnName? column = null,
        SchemaName? relatedSchema = null,
        TableName? relatedTable = null,
        ColumnName? relatedColumn = null)
    {
        if (string.IsNullOrWhiteSpace(schema.Value))
        {
            return Result<ProfilingInsightCoordinate>.Failure(
                ValidationError.Create("profile.insight.coordinate.schema.missing", "Schema name is required for profiling insights."));
        }

        if (string.IsNullOrWhiteSpace(table.Value))
        {
            return Result<ProfilingInsightCoordinate>.Failure(
                ValidationError.Create("profile.insight.coordinate.table.missing", "Table name is required for profiling insights."));
        }

        return Result<ProfilingInsightCoordinate>.Success(new ProfilingInsightCoordinate(
            schema,
            table,
            column,
            relatedSchema,
            relatedTable,
            relatedColumn));
    }
}

public sealed record ProfilingInsight(
    ProfilingInsightSeverity Severity,
    ProfilingInsightCategory Category,
    string Message,
    ProfilingInsightCoordinate? Coordinate)
{
    public static Result<ProfilingInsight> Create(
        ProfilingInsightSeverity severity,
        ProfilingInsightCategory category,
        string? message,
        ProfilingInsightCoordinate? coordinate)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return Result<ProfilingInsight>.Failure(
                ValidationError.Create("profile.insight.message.required", "Insight message must be provided."));
        }

        return Result<ProfilingInsight>.Success(new ProfilingInsight(
            severity,
            category,
            message.Trim(),
            coordinate));
    }
}
