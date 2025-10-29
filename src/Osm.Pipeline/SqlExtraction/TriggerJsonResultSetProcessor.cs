using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class TriggerJsonResultSetProcessor : ResultSetProcessor<OutsystemsTriggerJsonRow>
{
    private static readonly ResultSetReader<OutsystemsTriggerJsonRow> Reader = ResultSetReader<OutsystemsTriggerJsonRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> TriggersJson = Column.String(1, "TriggersJson");

    public TriggerJsonResultSetProcessor()
        : base("TriggerJson", order: 21)
    {
    }

    protected override ResultSetReader<OutsystemsTriggerJsonRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsTriggerJsonRow> rows)
        => accumulator.SetTriggerJson(rows);

    private static OutsystemsTriggerJsonRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        TriggersJson.Read(row));
}
