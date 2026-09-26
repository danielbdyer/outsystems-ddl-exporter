
namespace Osm.Pipeline.SqlExtraction;

internal sealed class ColumnCheckJsonResultSetProcessor : ResultSetProcessor<OutsystemsColumnCheckJsonRow>
{
    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<string> CheckJson = Column.String(1, "CheckJson");

    internal static ResultSetDescriptor<OutsystemsColumnCheckJsonRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsColumnCheckJsonRow>(
        "ColumnCheckJson",
        order: 7,
        builder => builder
            .Columns(AttrId, CheckJson)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetColumnCheckJson(rows)));

    public ColumnCheckJsonResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsColumnCheckJsonRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        CheckJson.Read(row));
}
