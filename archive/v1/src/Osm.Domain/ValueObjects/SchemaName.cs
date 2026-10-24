using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

public readonly record struct SchemaName(string Value)
{
    public static Result<SchemaName> Create(string? value)
        => StringValidators.RequiredIdentifier(value, "schema.name.invalid", "Schema name")
            .Map(static v => new SchemaName(v));

    public override string ToString() => Value;
}
