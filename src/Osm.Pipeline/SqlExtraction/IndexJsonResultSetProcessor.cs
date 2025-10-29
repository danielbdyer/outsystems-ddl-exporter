using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class IndexJsonResultSetProcessor : ResultSetProcessor<OutsystemsIndexJsonRow>
{
    private static readonly ResultSetReader<OutsystemsIndexJsonRow> Reader = ResultSetReader<OutsystemsIndexJsonRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> IndexesJson = Column.String(1, "IndexesJson");

    public IndexJsonResultSetProcessor()
        : base("IndexJson", order: 20)
    {
    }

    protected override ResultSetReader<OutsystemsIndexJsonRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsIndexJsonRow> rows)
        => accumulator.SetIndexJson(rows);

    private static OutsystemsIndexJsonRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        IndexesJson.Read(row));
}
