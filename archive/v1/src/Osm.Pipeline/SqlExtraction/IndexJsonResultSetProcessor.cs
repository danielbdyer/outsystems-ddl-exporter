
namespace Osm.Pipeline.SqlExtraction;

internal sealed class IndexJsonResultSetProcessor : ResultSetProcessor<OutsystemsIndexJsonRow>
{
    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> IndexesJson = Column.String(1, "IndexesJson");

    internal static ResultSetDescriptor<OutsystemsIndexJsonRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsIndexJsonRow>(
        "IndexJson",
        order: 20,
        builder => builder
            .Columns(EntityId, IndexesJson)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetIndexJson(rows)));

    public IndexJsonResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsIndexJsonRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        IndexesJson.Read(row));
}
