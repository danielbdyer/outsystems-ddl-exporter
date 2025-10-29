using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class ReferencesResultSetProcessor : ResultSetProcessor<OutsystemsReferenceRow>
{
    private static readonly ResultSetReader<OutsystemsReferenceRow> Reader = ResultSetReader<OutsystemsReferenceRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<int?> RefEntityId = Column.Int32OrNull(1, "RefEntityId");
    private static readonly ColumnDefinition<string?> RefEntityName = Column.StringOrNull(2, "RefEntityName");
    private static readonly ColumnDefinition<string?> RefPhysicalName = Column.StringOrNull(3, "RefPhysicalName");

    public ReferencesResultSetProcessor()
        : base("References", order: 3)
    {
    }

    protected override ResultSetReader<OutsystemsReferenceRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsReferenceRow> rows)
        => accumulator.SetReferences(rows);

    private static OutsystemsReferenceRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        RefEntityId.Read(row),
        RefEntityName.Read(row),
        RefPhysicalName.Read(row));
}
