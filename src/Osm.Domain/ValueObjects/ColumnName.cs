using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

public readonly record struct ColumnName(string Value)
{
    public static Result<ColumnName> Create(string? value)
        => StringValidators.RequiredIdentifier(value, "column.name.invalid", "Column name")
            .Map(static v => new ColumnName(v));

    public override string ToString() => Value;
}
