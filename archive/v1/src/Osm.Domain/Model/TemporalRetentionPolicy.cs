using System.Collections.Immutable;

using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public enum TemporalRetentionKind
{
    None,
    Infinite,
    Limited,
    UnsupportedYet
}

public enum TemporalRetentionUnit
{
    None,
    Days,
    Weeks,
    Months,
    Years,
    UnsupportedYet
}

public sealed record TemporalRetentionPolicy(
    TemporalRetentionKind Kind,
    int? Value,
    TemporalRetentionUnit Unit)
{
    public static readonly TemporalRetentionPolicy None = new(TemporalRetentionKind.None, null, TemporalRetentionUnit.None);

    public static TemporalRetentionPolicy Create(TemporalRetentionKind kind, int? value, TemporalRetentionUnit unit)
    {
        if (kind == TemporalRetentionKind.Limited && (value is null or < 0))
        {
            return new TemporalRetentionPolicy(TemporalRetentionKind.UnsupportedYet, value, unit);
        }

        if (kind is TemporalRetentionKind.None or TemporalRetentionKind.Infinite)
        {
            return new TemporalRetentionPolicy(kind, null, unit is TemporalRetentionUnit.None ? TemporalRetentionUnit.None : unit);
        }

        return new TemporalRetentionPolicy(kind, value, unit);
    }
}

public enum TemporalTableType
{
    None,
    SystemVersioned,
    HistoryTable,
    UnsupportedYet
}

public sealed record TemporalTableMetadata(
    TemporalTableType Type,
    SchemaName? HistorySchema,
    TableName? HistoryTable,
    ColumnName? PeriodStartColumn,
    ColumnName? PeriodEndColumn,
    TemporalRetentionPolicy RetentionPolicy,
    ImmutableArray<ExtendedProperty> ExtendedProperties)
{
    public static readonly TemporalTableMetadata None = new(
        TemporalTableType.None,
        null,
        null,
        null,
        null,
        TemporalRetentionPolicy.None,
        ExtendedProperty.EmptyArray);

    public static TemporalTableMetadata Create(
        TemporalTableType type,
        SchemaName? historySchema,
        TableName? historyTable,
        ColumnName? periodStart,
        ColumnName? periodEnd,
        TemporalRetentionPolicy? retention,
        ImmutableArray<ExtendedProperty> extendedProperties)
    {
        var normalizedType = type;
        if (type == TemporalTableType.SystemVersioned && (periodStart is null || periodEnd is null))
        {
            normalizedType = TemporalTableType.UnsupportedYet;
        }

        var metadata = new TemporalTableMetadata(
            normalizedType,
            historySchema,
            historyTable,
            periodStart,
            periodEnd,
            retention ?? TemporalRetentionPolicy.None,
            extendedProperties.IsDefault ? ExtendedProperty.EmptyArray : extendedProperties);

        return metadata;
    }
}
