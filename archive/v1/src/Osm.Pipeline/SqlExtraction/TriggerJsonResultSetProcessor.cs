
namespace Osm.Pipeline.SqlExtraction;

internal sealed class TriggerJsonResultSetProcessor : ResultSetProcessor<OutsystemsTriggerJsonRow>
{
    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> TriggersJson = Column.String(1, "TriggersJson");

    internal static ResultSetDescriptor<OutsystemsTriggerJsonRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsTriggerJsonRow>(
        "TriggerJson",
        order: 21,
        builder => builder
            .Columns(EntityId, TriggersJson)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetTriggerJson(rows)));

    public TriggerJsonResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsTriggerJsonRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        TriggersJson.Read(row));
}
