
namespace Osm.Pipeline.SqlExtraction;

internal sealed class AttributeHasForeignKeyResultSetProcessor : ResultSetProcessor<OutsystemsAttributeHasFkRow>
{
    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<bool> HasForeignKey = Column.Boolean(1, "HasFk");

    internal static ResultSetDescriptor<OutsystemsAttributeHasFkRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsAttributeHasFkRow>(
        "AttributeHasFk",
        order: 14,
        builder => builder
            .Columns(AttrId, HasForeignKey)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetAttributeForeignKeys(rows)));

    public AttributeHasForeignKeyResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsAttributeHasFkRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        HasForeignKey.Read(row));
}
