using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class RelationshipJsonResultSetProcessor : ResultSetProcessor<OutsystemsRelationshipJsonRow>
{
    private static readonly ResultSetReader<OutsystemsRelationshipJsonRow> Reader = ResultSetReader<OutsystemsRelationshipJsonRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> RelationshipsJson = Column.String(1, "RelationshipsJson");

    public RelationshipJsonResultSetProcessor()
        : base("RelationshipJson", order: 19)
    {
    }

    protected override ResultSetReader<OutsystemsRelationshipJsonRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsRelationshipJsonRow> rows)
        => accumulator.SetRelationshipJson(rows);

    private static OutsystemsRelationshipJsonRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        RelationshipsJson.Read(row));
}
