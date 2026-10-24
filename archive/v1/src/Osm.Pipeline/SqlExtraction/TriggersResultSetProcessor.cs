
namespace Osm.Pipeline.SqlExtraction;

internal sealed class TriggersResultSetProcessor : ResultSetProcessor<OutsystemsTriggerRow>
{
    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> TriggerName = Column.String(1, "TriggerName");
    private static readonly ColumnDefinition<bool> IsDisabled = Column.Boolean(2, "IsDisabled");
    private static readonly ColumnDefinition<string> TriggerDefinition = Column.String(3, "TriggerDefinition");

    internal static ResultSetDescriptor<OutsystemsTriggerRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsTriggerRow>(
        "Triggers",
        order: 17,
        builder => builder
            .Columns(EntityId, TriggerName, IsDisabled, TriggerDefinition)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetTriggers(rows)));

    public TriggersResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsTriggerRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        TriggerName.Read(row),
        IsDisabled.Read(row),
        TriggerDefinition.Read(row));
}
