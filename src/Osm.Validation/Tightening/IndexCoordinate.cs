using Osm.Domain.ValueObjects;

namespace Osm.Validation.Tightening;

public readonly record struct IndexCoordinate(SchemaName Schema, TableName Table, IndexName Index)
{
    public override string ToString() => $"{Schema.Value}.{Table.Value}.{Index.Value}";
}
