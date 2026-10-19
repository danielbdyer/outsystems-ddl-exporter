
namespace Osm.Pipeline.SqlExtraction;

internal sealed class PhysicalTablesResultSetProcessor : ResultSetProcessor<OutsystemsPhysicalTableRow>
{
    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> SchemaName = Column.String(1, "SchemaName");
    private static readonly ColumnDefinition<string> TableName = Column.String(2, "TableName");
    private static readonly ColumnDefinition<int> ObjectId = Column.Int32(3, "ObjectId");

    internal static ResultSetDescriptor<OutsystemsPhysicalTableRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsPhysicalTableRow>(
        "PhysicalTables",
        order: 4,
        builder => builder
            .Columns(EntityId, SchemaName, TableName, ObjectId)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetPhysicalTables(rows)));

    public PhysicalTablesResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsPhysicalTableRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        SchemaName.Read(row),
        TableName.Read(row),
        ObjectId.Read(row));
}
