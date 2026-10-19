
namespace Osm.Pipeline.SqlExtraction;

internal sealed class ColumnChecksResultSetProcessor : ResultSetProcessor<OutsystemsColumnCheckRow>
{
    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<string> ConstraintName = Column.String(1, "ConstraintName");
    private static readonly ColumnDefinition<string> Definition = Column.String(2, "Definition");
    private static readonly ColumnDefinition<bool> IsNotTrusted = Column.Boolean(3, "IsNotTrusted");

    internal static ResultSetDescriptor<OutsystemsColumnCheckRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsColumnCheckRow>(
        "ColumnChecks",
        order: 6,
        builder => builder
            .Columns(AttrId, ConstraintName, Definition, IsNotTrusted)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetColumnChecks(rows)));

    public ColumnChecksResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsColumnCheckRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        ConstraintName.Read(row),
        Definition.Read(row),
        IsNotTrusted.Read(row));
}
