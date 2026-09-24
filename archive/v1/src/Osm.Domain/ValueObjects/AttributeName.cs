using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

public readonly record struct AttributeName(string Value)
{
    public static Result<AttributeName> Create(string? value)
        => StringValidators.RequiredIdentifier(value, "attribute.name.invalid", "Attribute logical name")
            .Map(static v => new AttributeName(v));

    public override string ToString() => Value;
}
