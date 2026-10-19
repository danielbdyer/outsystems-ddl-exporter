
namespace Osm.Pipeline.SqlExtraction;

internal sealed class ReferencesResultSetProcessor : ResultSetProcessor<OutsystemsReferenceRow>
{
    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<int?> RefEntityId = Column.Int32OrNull(1, "RefEntityId");
    private static readonly ColumnDefinition<string?> RefEntityName = Column.StringOrNull(2, "RefEntityName");
    private static readonly ColumnDefinition<string?> RefPhysicalName = Column.StringOrNull(3, "RefPhysicalName");

    internal static ResultSetDescriptor<OutsystemsReferenceRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsReferenceRow>(
        "References",
        order: 3,
        builder => builder
            .Columns(AttrId, RefEntityId, RefEntityName, RefPhysicalName)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetReferences(rows)));

    public ReferencesResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsReferenceRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        RefEntityId.Read(row),
        RefEntityName.Read(row),
        RefPhysicalName.Read(row));
}
