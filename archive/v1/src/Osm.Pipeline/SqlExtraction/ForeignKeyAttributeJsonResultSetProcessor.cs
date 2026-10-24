
namespace Osm.Pipeline.SqlExtraction;

internal sealed class ForeignKeyAttributeJsonResultSetProcessor : ResultSetProcessor<OutsystemsForeignKeyAttributeJsonRow>
{
    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<string> ConstraintJson = Column.String(1, "ConstraintJson");

    internal static ResultSetDescriptor<OutsystemsForeignKeyAttributeJsonRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsForeignKeyAttributeJsonRow>(
        "ForeignKeyAttributeJson",
        order: 16,
        builder => builder
            .Columns(AttrId, ConstraintJson)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetForeignKeyAttributeJson(rows)));

    public ForeignKeyAttributeJsonResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsForeignKeyAttributeJsonRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        ConstraintJson.Read(row));
}
