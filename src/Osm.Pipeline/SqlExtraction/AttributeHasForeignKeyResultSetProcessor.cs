using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class AttributeHasForeignKeyResultSetProcessor : ResultSetProcessor<OutsystemsAttributeHasFkRow>
{
    private static readonly ResultSetReader<OutsystemsAttributeHasFkRow> Reader = ResultSetReader<OutsystemsAttributeHasFkRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<bool> HasForeignKey = Column.Boolean(1, "HasFk");

    public AttributeHasForeignKeyResultSetProcessor()
        : base("AttributeHasFk", order: 14)
    {
    }

    protected override ResultSetReader<OutsystemsAttributeHasFkRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsAttributeHasFkRow> rows)
        => accumulator.SetAttributeForeignKeys(rows);

    private static OutsystemsAttributeHasFkRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        HasForeignKey.Read(row));
}
