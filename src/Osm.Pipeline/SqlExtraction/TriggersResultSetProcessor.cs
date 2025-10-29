using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class TriggersResultSetProcessor : ResultSetProcessor<OutsystemsTriggerRow>
{
    private static readonly ResultSetReader<OutsystemsTriggerRow> Reader = ResultSetReader<OutsystemsTriggerRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> TriggerName = Column.String(1, "TriggerName");
    private static readonly ColumnDefinition<bool> IsDisabled = Column.Boolean(2, "IsDisabled");
    private static readonly ColumnDefinition<string> TriggerDefinition = Column.String(3, "TriggerDefinition");

    public TriggersResultSetProcessor()
        : base("Triggers", order: 17)
    {
    }

    protected override ResultSetReader<OutsystemsTriggerRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsTriggerRow> rows)
        => accumulator.SetTriggers(rows);

    private static OutsystemsTriggerRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        TriggerName.Read(row),
        IsDisabled.Read(row),
        TriggerDefinition.Read(row));
}
