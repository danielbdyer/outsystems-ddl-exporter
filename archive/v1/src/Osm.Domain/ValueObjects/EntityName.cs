using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

public readonly record struct EntityName(string Value)
{
    public static Result<EntityName> Create(string? value)
        => StringValidators.RequiredIdentifier(value, "entity.name.invalid", "Entity logical name")
            .Map(static v => new EntityName(v));

    public override string ToString() => Value;
}
