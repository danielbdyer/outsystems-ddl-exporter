using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class ForeignKeyAttributeJsonResultSetProcessor : ResultSetProcessor<OutsystemsForeignKeyAttributeJsonRow>
{
    private static readonly ResultSetReader<OutsystemsForeignKeyAttributeJsonRow> Reader = ResultSetReader<OutsystemsForeignKeyAttributeJsonRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<string> ConstraintJson = Column.String(1, "ConstraintJson");

    public ForeignKeyAttributeJsonResultSetProcessor()
        : base("ForeignKeyAttributeJson", order: 16)
    {
    }

    protected override ResultSetReader<OutsystemsForeignKeyAttributeJsonRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsForeignKeyAttributeJsonRow> rows)
        => accumulator.SetForeignKeyAttributeJson(rows);

    private static OutsystemsForeignKeyAttributeJsonRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        ConstraintJson.Read(row));
}
