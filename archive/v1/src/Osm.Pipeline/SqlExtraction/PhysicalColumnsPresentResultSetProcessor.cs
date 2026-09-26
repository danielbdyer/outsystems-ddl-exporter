
namespace Osm.Pipeline.SqlExtraction;

internal sealed class PhysicalColumnsPresentResultSetProcessor : ResultSetProcessor<OutsystemsPhysicalColumnPresenceRow>
{
    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");

    internal static ResultSetDescriptor<OutsystemsPhysicalColumnPresenceRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsPhysicalColumnPresenceRow>(
        "PhysicalColumnsPresent",
        order: 8,
        builder => builder
            .Columns(AttrId)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetPhysicalColumnsPresent(rows)));

    public PhysicalColumnsPresentResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsPhysicalColumnPresenceRow MapRow(DbRow row) => new(AttrId.Read(row));
}
