
namespace Osm.Pipeline.SqlExtraction;

internal sealed class IndexesResultSetProcessor : ResultSetProcessor<OutsystemsIndexRow>
{
    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<int> ObjectId = Column.Int32(1, "object_id");
    private static readonly ColumnDefinition<int> IndexId = Column.Int32(2, "index_id");
    private static readonly ColumnDefinition<string> IndexName = Column.String(3, "IndexName");
    private static readonly ColumnDefinition<bool> IsUnique = Column.Boolean(4, "IsUnique");
    private static readonly ColumnDefinition<bool> IsPrimary = Column.Boolean(5, "IsPrimary");
    private static readonly ColumnDefinition<string> Kind = Column.String(6, "Kind");
    private static readonly ColumnDefinition<string?> FilterDefinition = Column.StringOrNull(7, "FilterDefinition");
    private static readonly ColumnDefinition<bool> IsDisabled = Column.Boolean(8, "IsDisabled");
    private static readonly ColumnDefinition<bool> IsPadded = Column.Boolean(9, "IsPadded");
    private static readonly ColumnDefinition<int?> FillFactor = Column.Int32OrNull(10, "Fill_Factor");
    private static readonly ColumnDefinition<bool> IgnoreDupKey = Column.Boolean(11, "IgnoreDupKey");
    private static readonly ColumnDefinition<bool> AllowRowLocks = Column.Boolean(12, "AllowRowLocks");
    private static readonly ColumnDefinition<bool> AllowPageLocks = Column.Boolean(13, "AllowPageLocks");
    private static readonly ColumnDefinition<bool> NoRecompute = Column.Boolean(14, "NoRecompute");
    private static readonly ColumnDefinition<string?> DataSpaceName = Column.StringOrNull(15, "DataSpaceName");
    private static readonly ColumnDefinition<string?> DataSpaceType = Column.StringOrNull(16, "DataSpaceType");
    private static readonly ColumnDefinition<string?> PartitionColumnsJson = Column.StringOrNull(17, "PartitionColumnsJson");
    private static readonly ColumnDefinition<string?> DataCompressionJson = Column.StringOrNull(18, "DataCompressionJson");

    internal static ResultSetDescriptor<OutsystemsIndexRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsIndexRow>(
        "Indexes",
        order: 9,
        builder => builder
            .Columns(
                EntityId,
                ObjectId,
                IndexId,
                IndexName,
                IsUnique,
                IsPrimary,
                Kind,
                FilterDefinition,
                IsDisabled,
                IsPadded,
                FillFactor,
                IgnoreDupKey,
                AllowRowLocks,
                AllowPageLocks,
                NoRecompute,
                DataSpaceName,
                DataSpaceType,
                PartitionColumnsJson,
                DataCompressionJson)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetIndexes(rows)));

    public IndexesResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsIndexRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        ObjectId.Read(row),
        IndexId.Read(row),
        IndexName.Read(row),
        IsUnique.Read(row),
        IsPrimary.Read(row),
        Kind.Read(row),
        FilterDefinition.Read(row),
        IsDisabled.Read(row),
        IsPadded.Read(row),
        FillFactor.Read(row),
        IgnoreDupKey.Read(row),
        AllowRowLocks.Read(row),
        AllowPageLocks.Read(row),
        NoRecompute.Read(row),
        DataSpaceName.Read(row),
        DataSpaceType.Read(row),
        PartitionColumnsJson.Read(row),
        DataCompressionJson.Read(row));
}
