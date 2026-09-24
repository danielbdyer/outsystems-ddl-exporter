using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

public readonly record struct TableName(string Value)
{
    public static Result<TableName> Create(string? value)
        => StringValidators.RequiredIdentifier(value, "table.name.invalid", "Table name")
            .Map(static v => new TableName(v));

    public override string ToString() => Value;
}
