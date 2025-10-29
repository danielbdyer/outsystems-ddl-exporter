using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class PhysicalTablesResultSetProcessor : ResultSetProcessor<OutsystemsPhysicalTableRow>
{
    private static readonly ResultSetReader<OutsystemsPhysicalTableRow> Reader = ResultSetReader<OutsystemsPhysicalTableRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> SchemaName = Column.String(1, "SchemaName");
    private static readonly ColumnDefinition<string> TableName = Column.String(2, "TableName");
    private static readonly ColumnDefinition<int> ObjectId = Column.Int32(3, "ObjectId");

    public PhysicalTablesResultSetProcessor()
        : base("PhysicalTables", order: 4)
    {
    }

    protected override ResultSetReader<OutsystemsPhysicalTableRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsPhysicalTableRow> rows)
        => accumulator.SetPhysicalTables(rows);

    private static OutsystemsPhysicalTableRow MapRow(DbRow row) => new(
        EntityId.Read(row),
        SchemaName.Read(row),
        TableName.Read(row),
        ObjectId.Read(row));
}
