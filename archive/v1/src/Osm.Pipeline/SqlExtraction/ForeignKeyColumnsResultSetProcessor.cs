
namespace Osm.Pipeline.SqlExtraction;

internal sealed class ForeignKeyColumnsResultSetProcessor : ResultSetProcessor<OutsystemsForeignKeyColumnRow>
{
    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<int> FkObjectId = Column.Int32(1, "FkObjectId");
    private static readonly ColumnDefinition<int> Ordinal = Column.Int32(2, "Ordinal");
    private static readonly ColumnDefinition<string> ParentColumn = Column.String(3, "ParentColumn");
    private static readonly ColumnDefinition<string> ReferencedColumn = Column.String(4, "ReferencedColumn");
    private static readonly ColumnDefinition<int?> ParentAttrId = Column.Int32OrNull(5, "ParentAttrId");
    private static readonly ColumnDefinition<string?> ParentAttrName = Column.StringOrNull(6, "ParentAttrName");
    private static readonly ColumnDefinition<int?> ReferencedAttrId = Column.Int32OrNull(7, "ReferencedAttrId");
    private static readonly ColumnDefinition<string?> ReferencedAttrName = Column.StringOrNull(8, "ReferencedAttrName");

    internal static ResultSetDescriptor<OutsystemsForeignKeyColumnRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsForeignKeyColumnRow>(
        "ForeignKeyColumns",
        order: 12,
        builder => builder
            .Columns(
                EntityId,
                FkObjectId,
                Ordinal,
                ParentColumn,
                ReferencedColumn,
                ParentAttrId,
                ParentAttrName,
                ReferencedAttrId,
                ReferencedAttrName)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetForeignKeyColumns(rows)));

    public ForeignKeyColumnsResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsForeignKeyColumnRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        FkObjectId.Read(row),
        Ordinal.Read(row),
        ParentColumn.Read(row),
        ReferencedColumn.Read(row),
        ParentAttrId.Read(row),
        ParentAttrName.Read(row),
        ReferencedAttrId.Read(row),
        ReferencedAttrName.Read(row));
}
