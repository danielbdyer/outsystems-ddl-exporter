
namespace Osm.Pipeline.SqlExtraction;

internal sealed class ForeignKeyColumnsJsonResultSetProcessor : ResultSetProcessor<OutsystemsForeignKeyColumnsJsonRow>
{
    private static readonly ColumnDefinition<int> FkObjectId = Column.Int32(0, "FkObjectId");
    private static readonly ColumnDefinition<string> ColumnsJson = Column.String(1, "ColumnsJson");

    internal static ResultSetDescriptor<OutsystemsForeignKeyColumnsJsonRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsForeignKeyColumnsJsonRow>(
        "ForeignKeyColumnsJson",
        order: 15,
        builder => builder
            .Columns(FkObjectId, ColumnsJson)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetForeignKeyColumnsJson(rows)));

    public ForeignKeyColumnsJsonResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsForeignKeyColumnsJsonRow MapRow(DbRow row) => new(
        FkObjectId.Read(row),
        ColumnsJson.Read(row));
}
