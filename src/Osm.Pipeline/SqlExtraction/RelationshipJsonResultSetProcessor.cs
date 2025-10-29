
namespace Osm.Pipeline.SqlExtraction;

internal sealed class RelationshipJsonResultSetProcessor : ResultSetProcessor<OutsystemsRelationshipJsonRow>
{
    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> RelationshipsJson = Column.String(1, "RelationshipsJson");

    internal static ResultSetDescriptor<OutsystemsRelationshipJsonRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsRelationshipJsonRow>(
        "RelationshipJson",
        order: 19,
        builder => builder
            .Columns(EntityId, RelationshipsJson)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetRelationshipJson(rows)));

    public RelationshipJsonResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsRelationshipJsonRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        RelationshipsJson.Read(row));
}
