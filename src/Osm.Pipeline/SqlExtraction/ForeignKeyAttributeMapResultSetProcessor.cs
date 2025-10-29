using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class ForeignKeyAttributeMapResultSetProcessor : ResultSetProcessor<OutsystemsForeignKeyAttrMapRow>
{
    private static readonly ResultSetReader<OutsystemsForeignKeyAttrMapRow> Reader = ResultSetReader<OutsystemsForeignKeyAttrMapRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<int> FkObjectId = Column.Int32(1, "FkObjectId");

    public ForeignKeyAttributeMapResultSetProcessor()
        : base("ForeignKeyAttrMap", order: 13)
    {
    }

    protected override ResultSetReader<OutsystemsForeignKeyAttrMapRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsForeignKeyAttrMapRow> rows)
        => accumulator.SetForeignKeyAttributeMap(rows);

    private static OutsystemsForeignKeyAttrMapRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        FkObjectId.Read(row));
}
