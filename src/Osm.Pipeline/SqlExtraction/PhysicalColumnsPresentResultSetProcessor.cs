using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class PhysicalColumnsPresentResultSetProcessor : ResultSetProcessor<OutsystemsPhysicalColumnPresenceRow>
{
    private static readonly ResultSetReader<OutsystemsPhysicalColumnPresenceRow> Reader = ResultSetReader<OutsystemsPhysicalColumnPresenceRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");

    public PhysicalColumnsPresentResultSetProcessor()
        : base("PhysicalColumnsPresent", order: 8)
    {
    }

    protected override ResultSetReader<OutsystemsPhysicalColumnPresenceRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsPhysicalColumnPresenceRow> rows)
        => accumulator.SetPhysicalColumnsPresent(rows);

    private static OutsystemsPhysicalColumnPresenceRow MapRow(DbRow row) => new(AttrId.Read(row));
}
