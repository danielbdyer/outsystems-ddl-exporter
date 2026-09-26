using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

public readonly record struct SequenceName(string Value)
{
    public static Result<SequenceName> Create(string? value)
        => StringValidators.RequiredIdentifier(value, "sequence.name.invalid", "Sequence name")
            .Map(static v => new SequenceName(v));

    public override string ToString() => Value;
}
