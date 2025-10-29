
namespace Osm.Pipeline.SqlExtraction;

internal sealed class IndexColumnsResultSetProcessor : ResultSetProcessor<OutsystemsIndexColumnRow>
{
    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> IndexName = Column.String(1, "IndexName");
    private static readonly ColumnDefinition<int> Ordinal = Column.Int32(2, "Ordinal");
    private static readonly ColumnDefinition<string> PhysicalColumn = Column.String(3, "PhysicalColumn");
    private static readonly ColumnDefinition<bool> IsIncluded = Column.Boolean(4, "IsIncluded");
    private static readonly ColumnDefinition<string?> Direction = Column.StringOrNull(5, "Direction");
    private static readonly ColumnDefinition<string?> HumanAttr = Column.StringOrNull(6, "HumanAttr");

    internal static ResultSetDescriptor<OutsystemsIndexColumnRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsIndexColumnRow>(
        "IndexColumns",
        order: 10,
        builder => builder
            .Columns(EntityId, IndexName, Ordinal, PhysicalColumn, IsIncluded, Direction, HumanAttr)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetIndexColumns(rows)));

    public IndexColumnsResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsIndexColumnRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        IndexName.Read(row),
        Ordinal.Read(row),
        PhysicalColumn.Read(row),
        IsIncluded.Read(row),
        Direction.Read(row),
        HumanAttr.Read(row));
}
