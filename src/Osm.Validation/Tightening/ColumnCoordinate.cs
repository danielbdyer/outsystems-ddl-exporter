using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Osm.Validation.Tightening;

public readonly record struct ColumnCoordinate(SchemaName Schema, TableName Table, ColumnName Column)
{
    public override string ToString() => $"{Schema.Value}.{Table.Value}.{Column.Value}";

    public static ColumnCoordinate From(ColumnProfile profile)
        => new(profile.Schema, profile.Table, profile.Column);

    public static ColumnCoordinate From(UniqueCandidateProfile profile)
        => new(profile.Schema, profile.Table, profile.Column);

    public static ColumnCoordinate From(ForeignKeyReference reference)
        => new(reference.FromSchema, reference.FromTable, reference.FromColumn);
}
