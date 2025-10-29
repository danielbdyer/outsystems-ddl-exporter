using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class ForeignKeysResultSetProcessor : ResultSetProcessor<OutsystemsForeignKeyRow>
{
    private static readonly ResultSetReader<OutsystemsForeignKeyRow> Reader = ResultSetReader<OutsystemsForeignKeyRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<int> FkObjectId = Column.Int32(1, "FkObjectId");
    private static readonly ColumnDefinition<string> FkName = Column.String(2, "FkName");
    private static readonly ColumnDefinition<string> DeleteAction = Column.String(3, "DeleteAction");
    private static readonly ColumnDefinition<string> UpdateAction = Column.String(4, "UpdateAction");
    private static readonly ColumnDefinition<int> ReferencedObjectId = Column.Int32(5, "ReferencedObjectId");
    private static readonly ColumnDefinition<int?> ReferencedEntityId = Column.Int32OrNull(6, "ReferencedEntityId");
    private static readonly ColumnDefinition<string> ReferencedSchema = Column.String(7, "ReferencedSchema");
    private static readonly ColumnDefinition<string> ReferencedTable = Column.String(8, "ReferencedTable");
    private static readonly ColumnDefinition<bool> IsNoCheck = Column.Boolean(9, "IsNoCheck");

    public ForeignKeysResultSetProcessor()
        : base("ForeignKeys", order: 11)
    {
    }

    protected override ResultSetReader<OutsystemsForeignKeyRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsForeignKeyRow> rows)
        => accumulator.SetForeignKeys(rows);

    private static OutsystemsForeignKeyRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        FkObjectId.Read(row),
        FkName.Read(row),
        DeleteAction.Read(row),
        UpdateAction.Read(row),
        ReferencedObjectId.Read(row),
        ReferencedEntityId.Read(row),
        ReferencedSchema.Read(row),
        ReferencedTable.Read(row),
        IsNoCheck.Read(row));
}
