using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

public readonly record struct ForeignKeyName(string Value)
{
    public static Result<ForeignKeyName> Create(string? value)
        => StringValidators.RequiredIdentifier(value, "fk.name.invalid", "Foreign key name")
            .Map(static v => new ForeignKeyName(v));

    public override string ToString() => Value;
}
