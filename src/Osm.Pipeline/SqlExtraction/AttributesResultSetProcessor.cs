using System;
using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class AttributesResultSetProcessor : ResultSetProcessor<OutsystemsAttributeRow>
{
    private static readonly ResultSetReader<OutsystemsAttributeRow> Reader = ResultSetReader<OutsystemsAttributeRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<int> EntityId = Column.Int32(1, "EntityId");
    private static readonly ColumnDefinition<string> AttrName = Column.String(2, "AttrName");
    private static readonly ColumnDefinition<Guid?> AttrSsKey = Column.GuidOrNull(3, "AttrSSKey");
    private static readonly ColumnDefinition<string?> DataType = Column.StringOrNull(4, "DataType");
    private static readonly ColumnDefinition<int?> Length = Column.Int32OrNull(5, "Length");
    private static readonly ColumnDefinition<int?> Precision = Column.Int32OrNull(6, "Precision");
    private static readonly ColumnDefinition<int?> Scale = Column.Int32OrNull(7, "Scale");
    private static readonly ColumnDefinition<string?> DefaultValue = Column.StringOrNull(8, "DefaultValue");
    private static readonly ColumnDefinition<bool> IsMandatory = Column.Boolean(9, "IsMandatory");
    private static readonly ColumnDefinition<bool> AttrIsActive = Column.Boolean(10, "AttrIsActive");
    private static readonly ColumnDefinition<bool?> IsAutoNumber = Column.BooleanOrNull(11, "IsAutoNumber");
    private static readonly ColumnDefinition<bool?> IsIdentifier = Column.BooleanOrNull(12, "IsIdentifier");
    private static readonly ColumnDefinition<int?> RefEntityId = Column.Int32OrNull(13, "RefEntityId");
    private static readonly ColumnDefinition<string?> OriginalName = Column.StringOrNull(14, "OriginalName");
    private static readonly ColumnDefinition<string?> ExternalColumnType = Column.StringOrNull(15, "ExternalColumnType");
    private static readonly ColumnDefinition<string?> DeleteRule = Column.StringOrNull(16, "DeleteRule");
    private static readonly ColumnDefinition<string?> PhysicalColumnName = Column.StringOrNull(17, "PhysicalColumnName");
    private static readonly ColumnDefinition<string?> DatabaseColumnName = Column.StringOrNull(18, "DatabaseColumnName");
    private static readonly ColumnDefinition<string?> LegacyType = Column.StringOrNull(19, "LegacyType");
    private static readonly ColumnDefinition<int?> Decimals = Column.Int32OrNull(20, "Decimals");
    private static readonly ColumnDefinition<string?> OriginalType = Column.StringOrNull(21, "OriginalType");
    private static readonly ColumnDefinition<string?> AttrDescription = Column.StringOrNull(22, "AttrDescription");

    public AttributesResultSetProcessor()
        : base("Attributes", order: 2)
    {
    }

    protected override ResultSetReader<OutsystemsAttributeRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsAttributeRow> rows)
        => accumulator.SetAttributes(rows);

    private static OutsystemsAttributeRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        EntityId.Read(row),
        AttrName.Read(row),
        AttrSsKey.Read(row),
        DataType.Read(row),
        Length.Read(row),
        Precision.Read(row),
        Scale.Read(row),
        DefaultValue.Read(row),
        IsMandatory.Read(row),
        AttrIsActive.Read(row),
        IsAutoNumber.Read(row),
        IsIdentifier.Read(row),
        RefEntityId.Read(row),
        OriginalName.Read(row),
        ExternalColumnType.Read(row),
        DeleteRule.Read(row),
        PhysicalColumnName.Read(row),
        DatabaseColumnName.Read(row),
        LegacyType.Read(row),
        Decimals.Read(row),
        OriginalType.Read(row),
        AttrDescription.Read(row));
}
