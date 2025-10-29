using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class ColumnChecksResultSetProcessor : ResultSetProcessor<OutsystemsColumnCheckRow>
{
    private static readonly ResultSetReader<OutsystemsColumnCheckRow> Reader = ResultSetReader<OutsystemsColumnCheckRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
    private static readonly ColumnDefinition<string> ConstraintName = Column.String(1, "ConstraintName");
    private static readonly ColumnDefinition<string> Definition = Column.String(2, "Definition");
    private static readonly ColumnDefinition<bool> IsNotTrusted = Column.Boolean(3, "IsNotTrusted");

    public ColumnChecksResultSetProcessor()
        : base("ColumnChecks", order: 6)
    {
    }

    protected override ResultSetReader<OutsystemsColumnCheckRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsColumnCheckRow> rows)
        => accumulator.SetColumnChecks(rows);

    private static OutsystemsColumnCheckRow MapRow(DbRow row) => new(
        AttrId.Read(row),
        ConstraintName.Read(row),
        Definition.Read(row),
        IsNotTrusted.Read(row));
}
