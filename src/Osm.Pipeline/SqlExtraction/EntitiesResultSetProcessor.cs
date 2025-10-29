using System;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class EntitiesResultSetProcessor : ResultSetProcessor<OutsystemsEntityRow>
{
    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> EntityName = Column.String(1, "EntityName");
    private static readonly ColumnDefinition<string> PhysicalTableName = Column.String(2, "PhysicalTableName");
    private static readonly ColumnDefinition<int> EspaceId = Column.Int32(3, "EspaceId");
    private static readonly ColumnDefinition<bool> EntityIsActive = Column.Boolean(4, "EntityIsActive");
    private static readonly ColumnDefinition<bool> IsSystemEntity = Column.Boolean(5, "IsSystemEntity");
    private static readonly ColumnDefinition<bool> IsExternalEntity = Column.Boolean(6, "IsExternalEntity");
    private static readonly ColumnDefinition<string?> DataKind = Column.StringOrNull(7, "DataKind");
    private static readonly ColumnDefinition<Guid?> PrimaryKeySsKey = Column.GuidOrNull(8, "PrimaryKeySSKey");
    private static readonly ColumnDefinition<Guid?> EntitySsKey = Column.GuidOrNull(9, "EntitySSKey");
    private static readonly ColumnDefinition<string?> EntityDescription = Column.StringOrNull(10, "EntityDescription");

    internal static ResultSetDescriptor<OutsystemsEntityRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsEntityRow>(
        "Entities",
        order: 1,
        builder => builder
            .Columns(
                EntityId,
                EntityName,
                PhysicalTableName,
                EspaceId,
                EntityIsActive,
                IsSystemEntity,
                IsExternalEntity,
                DataKind,
                PrimaryKeySsKey,
                EntitySsKey,
                EntityDescription)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetEntities(rows)));

    public EntitiesResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsEntityRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        EntityName.Read(row),
        PhysicalTableName.Read(row),
        EspaceId.Read(row),
        EntityIsActive.Read(row),
        IsSystemEntity.Read(row),
        IsExternalEntity.Read(row),
        DataKind.Read(row),
        PrimaryKeySsKey.Read(row),
        EntitySsKey.Read(row),
        EntityDescription.Read(row));
}
