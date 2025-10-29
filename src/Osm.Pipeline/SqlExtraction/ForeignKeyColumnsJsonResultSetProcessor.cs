using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class ForeignKeyColumnsJsonResultSetProcessor : ResultSetProcessor<OutsystemsForeignKeyColumnsJsonRow>
{
    private static readonly ResultSetReader<OutsystemsForeignKeyColumnsJsonRow> Reader = ResultSetReader<OutsystemsForeignKeyColumnsJsonRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> FkObjectId = Column.Int32(0, "FkObjectId");
    private static readonly ColumnDefinition<string> ColumnsJson = Column.String(1, "ColumnsJson");

    public ForeignKeyColumnsJsonResultSetProcessor()
        : base("ForeignKeyColumnsJson", order: 15)
    {
    }

    protected override ResultSetReader<OutsystemsForeignKeyColumnsJsonRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsForeignKeyColumnsJsonRow> rows)
        => accumulator.SetForeignKeyColumnsJson(rows);

    private static OutsystemsForeignKeyColumnsJsonRow MapRow(DbRow row) => new(
        FkObjectId.Read(row),
        ColumnsJson.Read(row));
}
