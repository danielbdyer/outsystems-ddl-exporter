using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class ColumnCheckJsonResultSetProcessor : ResultSetProcessor<OutsystemsColumnCheckJsonRow>
{
    private static readonly ResultSetReader<OutsystemsColumnCheckJsonRow> Reader = ResultSetReader<OutsystemsColumnCheckJsonRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<string> CheckJson = Column.String(1, "CheckJson");

    public ColumnCheckJsonResultSetProcessor()
        : base("ColumnCheckJson", order: 7)
    {
    }

    protected override ResultSetReader<OutsystemsColumnCheckJsonRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsColumnCheckJsonRow> rows)
        => accumulator.SetColumnCheckJson(rows);

    private static OutsystemsColumnCheckJsonRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        CheckJson.Read(row));
}
