using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

public readonly record struct ConstraintName(string Value)
{
    public static Result<ConstraintName> Create(string? value)
        => StringValidators.RequiredIdentifier(value, "constraint.name.invalid", "Constraint name")
            .Map(static v => new ConstraintName(v));

    public override string ToString() => Value;
}
