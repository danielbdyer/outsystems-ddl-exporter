
namespace Osm.Pipeline.SqlExtraction;

internal sealed class ColumnRealityResultSetProcessor : ResultSetProcessor<OutsystemsColumnRealityRow>
{
    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<bool> IsNullable = Column.Boolean(1, "IsNullable");
    private static readonly ColumnDefinition<string> SqlType = Column.String(2, "SqlType");
    private static readonly ColumnDefinition<int?> MaxLength = Column.Int32OrNull(3, "MaxLength");
    private static readonly ColumnDefinition<int?> Precision = Column.Int32OrNull(4, "Precision");
    private static readonly ColumnDefinition<int?> Scale = Column.Int32OrNull(5, "Scale");
    private static readonly ColumnDefinition<string?> CollationName = Column.StringOrNull(6, "CollationName");
    private static readonly ColumnDefinition<bool> IsIdentity = Column.Boolean(7, "IsIdentity");
    private static readonly ColumnDefinition<bool> IsComputed = Column.Boolean(8, "IsComputed");
    private static readonly ColumnDefinition<string?> ComputedDefinition = Column.StringOrNull(9, "ComputedDefinition");
    private static readonly ColumnDefinition<string?> DefaultConstraintName = Column.StringOrNull(10, "DefaultConstraintName");
    private static readonly ColumnDefinition<string?> DefaultDefinition = Column.StringOrNull(11, "DefaultDefinition");
    private static readonly ColumnDefinition<string> PhysicalColumn = Column.String(12, "PhysicalColumn");

    internal static ResultSetDescriptor<OutsystemsColumnRealityRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsColumnRealityRow>(
        "ColumnReality",
        order: 5,
        builder => builder
            .Columns(
                AttrId,
                IsNullable,
                SqlType,
                MaxLength,
                Precision,
                Scale,
                CollationName,
                IsIdentity,
                IsComputed,
                ComputedDefinition,
                DefaultConstraintName,
                DefaultDefinition,
                PhysicalColumn)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetColumnReality(rows)));

    public ColumnRealityResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsColumnRealityRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        IsNullable.Read(row),
        SqlType.Read(row),
        MaxLength.Read(row),
        Precision.Read(row),
        Scale.Read(row),
        CollationName.Read(row),
        IsIdentity.Read(row),
        IsComputed.Read(row),
        ComputedDefinition.Read(row),
        DefaultConstraintName.Read(row),
        DefaultDefinition.Read(row),
        PhysicalColumn.Read(row));
}
