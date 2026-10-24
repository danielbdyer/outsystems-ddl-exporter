
namespace Osm.Pipeline.SqlExtraction;

internal sealed class ForeignKeyAttributeMapResultSetProcessor : ResultSetProcessor<OutsystemsForeignKeyAttrMapRow>
{
    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<int> FkObjectId = Column.Int32(1, "FkObjectId");

    internal static ResultSetDescriptor<OutsystemsForeignKeyAttrMapRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsForeignKeyAttrMapRow>(
        "ForeignKeyAttrMap",
        order: 13,
        builder => builder
            .Columns(AttrId, FkObjectId)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetForeignKeyAttributeMap(rows)));

    public ForeignKeyAttributeMapResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsForeignKeyAttrMapRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        FkObjectId.Read(row));
}
