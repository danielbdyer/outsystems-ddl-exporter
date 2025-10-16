using Osm.Domain.ValueObjects;

namespace Osm.Domain.Profiling.Insights;

public sealed record ProfileInsightAnchor(
    SchemaName? Schema,
    TableName? Table,
    ColumnName? Column,
    string? ReferenceName)
{
    public static ProfileInsightAnchor None { get; } = new(null, null, null, null);

    public static ProfileInsightAnchor Create(
        SchemaName? schema = null,
        TableName? table = null,
        ColumnName? column = null,
        string? referenceName = null)
    {
        var normalizedReference = string.IsNullOrWhiteSpace(referenceName)
            ? null
            : referenceName!.Trim();

        return new ProfileInsightAnchor(schema, table, column, normalizedReference);
    }
}
